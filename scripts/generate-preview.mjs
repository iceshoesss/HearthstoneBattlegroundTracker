#!/usr/bin/env node
/**
 * 一次性预览卡池生成器
 *
 * 用法:
 *   node scripts/generate-preview.mjs           # 默认最新 currentPatch
 *   node scripts/generate-preview.mjs 36.6      # 指定补丁版本
 *
 * 流程:
 *   1. BattlegroundDB 全量 bg_cards.json（正式池）
 *   2. hsbg.cards /api/v1/patches/{ver}（added / changed / removed / returning）
 *   3. hsbg.cards /api/v1/cards 分页表（dbfId → externalId 映射）
 *   4. 应用补丁 → raw_bg_cards.json（与 BGDB 同构）
 *   5. node scripts/build-data.mjs → public/data/cards.json
 *
 * 之后: npm run build  （prebuild 会再跑 build-data + fetch-images；请用 npm run build:preview）
 */
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const BGDB_URL =
  'https://raw.githubusercontent.com/iceshoesss/BattlegroundDB/master/BattlegroundDB/Data/bg_cards.json';
const HSJSON_LOCAL = path.join(root, 'data-src', 'cards.battlegrounds.json');

const argVer = process.argv[2];
const USER_AGENT = 'hbt-cards-preview';

async function fetchJson(url) {
  try {
    const res = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } catch (err) {
    console.warn(`fetch 失败(${err.message}): ${url} → curl`);
    const r = spawnSync('curl', ['-fsSL', '-A', USER_AGENT, url], {
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
    });
    if (r.status !== 0) throw new Error(r.stderr || `curl ${url} failed`);
    return JSON.parse(r.stdout);
  }
}

async function fetchText(url) {
  try {
    const res = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.text();
  } catch {
    const r = spawnSync('curl', ['-fsSL', '-A', USER_AGENT, url], {
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
    });
    if (r.status !== 0) throw new Error(r.stderr || `curl ${url} failed`);
    return r.stdout;
  }
}

/** 解析 HTML 卡文中的关键词 */
function parseKeywords(text) {
  if (!text) return [];
  const map = [
    ['Deathrattle', 'Deathrattle'],
    ['Battlecry', 'Battlecry'],
    ['Divine Shield', 'Divine Shield'],
    ['Taunt', 'Taunt'],
    ['Reborn', 'Reborn'],
    ['Venomous', 'Venomous'],
    ['Poisonous', 'Poisonous'],
    ['Windfury', 'Windfury'],
    ['Mega-Windfury', 'Mega-Windfury'],
    ['Start of Combat', 'Start of Combat'],
    ['End of Turn', 'End of Turn'],
    ['Start of Turn', 'Start of Turn'],
    ['Rally', 'Rally'],
    ['Magnetic', 'Magnetic'],
    ['Stealth', 'Stealth'],
    ['Spellcraft', 'Spellcraft'],
    ['Discover', 'Discover'],
    ['Aura', 'Aura'],
    ['Choose One', 'Choose One'],
    ['Activate', 'Activate'],
    ['Cleave', 'Cleave'],
    ['Elusive', 'Elusive'],
  ];
  const out = [];
  const plain = text.replace(/<[^>]+>/g, '');
  for (const [en] of map) {
    if (plain.includes(en) || text.includes(`>${en}<`)) out.push(en);
  }
  return [...new Set(out)];
}

function stripHtml(s) {
  return (s || '').replace(/<[^>]+>/g, '').trim();
}

/** 拉 hsbg 全量 cards，建 dbfId → externalId(cardId) */
async function loadDbfToCardId() {
  const map = new Map();
  // 本地 HearthstoneJSON 优先
  if (existsSync(HSJSON_LOCAL)) {
    const list = JSON.parse(readFileSync(HSJSON_LOCAL, 'utf8'));
    for (const c of list) {
      if (c.dbfId > 0 && c.id) map.set(c.dbfId, c.id);
    }
    console.log(`dbf 映射(本地 HSJSON): ${map.size}`);
  }
  // hsbg cards 分页补齐
  let offset = 0;
  for (;;) {
    const page = await fetchJson(
      `https://hsbg.cards/api/v1/cards?limit=100&offset=${offset}`,
    );
    const rows = page?.data || [];
    if (!rows.length) break;
    for (const c of rows) {
      if (c.id && c.externalId) map.set(c.id, c.externalId);
    }
    const next = page?.pagination?.nextOffset;
    if (next == null || rows.length < 100) break;
    offset = next;
  }
  console.log(`dbf 映射合计: ${map.size}`);
  return map;
}

/** patch 条目 → raw 卡（BGDB 风格） */
function patchCardToRaw(entry, dbfMap, changeType) {
  const dbfId = entry.id;
  const card =
    changeType === 'removed' ? entry.oldCard || entry : entry.newCard || entry.oldCard;
  if (!card) return null;

  const cardId =
    dbfMap.get(dbfId) ||
    entry.externalId ||
    // 兜底：用英文名生成占位 id，避免丢失
    `PREVIEW_${dbfId}`;

  const minionTypes = card.minionTypes || [];
  const cardType = card.cardType || (minionTypes.length ? 'minion' : 'spell');
  const isHero = cardType === 'hero';
  const name = (card.name || entry.name || '').trim();

  return {
    id: dbfId,
    cardId,
    name,
    nameZh: name, // 预览期暂无中文时用英文
    text: card.text || '',
    textZh: card.text || '',
    tier: card.tier ?? null,
    cardType,
    minionType: minionTypes[0] || '',
    minionTypes,
    attack: card.attack ?? 0,
    health: card.health ?? 0,
    attackGold: card.attackGold ?? null,
    healthGold: card.healthGold ?? null,
    manaCost: card.manaCost ?? null,
    keywords: card.keywords || parseKeywords(card.text),
    pool: changeType !== 'removed',
    isToken: false,
    isBuddy: false,
    isDuosOnly: cardType === 'minion' && /BGDUO/i.test(String(entry.id)) || /duos/i.test(entry.name || ''),
    isTimewarped: false,
    childIds: [],
    dbfIdGold: card.dbfIdGold ?? null,
    preview: true,
    previewChangeType: changeType,
  };
}

function indexRaw(cards) {
  const byCardId = new Map();
  const byDbf = new Map();
  for (const c of cards) {
    if (c.cardId) byCardId.set(c.cardId, c);
    if (c.id != null) byDbf.set(c.id, c);
  }
  return { byCardId, byDbf };
}

function applyPatch(rawCards, patch, dbfMap) {
  const stats = { added: 0, changed: 0, removed: 0, returning: 0, skipped: 0 };
  const { byCardId, byDbf } = indexRaw(rawCards);

  const upsert = (raw) => {
    if (!raw) {
      stats.skipped++;
      return;
    }
    const existing = byDbf.get(raw.id) || byCardId.get(raw.cardId);
    if (existing) {
      Object.assign(existing, raw, { preview: true, previewChangeType: raw.previewChangeType });
    } else {
      rawCards.push(raw);
      byDbf.set(raw.id, raw);
      byCardId.set(raw.cardId, raw);
    }
  };

  for (const sec of patch.sections || []) {
    const t = sec.changeType;
    for (const entry of sec.cards || []) {
      if (t === 'added') {
        upsert(patchCardToRaw(entry, dbfMap, 'added'));
        stats.added++;
      } else if (t === 'returning') {
        upsert(patchCardToRaw(entry, dbfMap, 'returning'));
        stats.returning++;
      } else if (t === 'changed') {
        const raw = patchCardToRaw(entry, dbfMap, 'changed');
        if (raw) {
          // 尽量保留原 cardId / 中文名
          const existing = byDbf.get(raw.id) || byCardId.get(raw.cardId);
          if (existing) {
            raw.cardId = existing.cardId;
            if (existing.nameZh) raw.nameZh = existing.nameZh;
            if (existing.textZh && raw.previewChangeType === 'changed') {
              // 英文 text 已是新文案；中文可能仍是旧的，保留字段但可被覆盖
            }
            upsert(raw);
          } else {
            upsert(raw);
          }
          stats.changed++;
        } else stats.skipped++;
      } else if (t === 'removed') {
        const dbfId = entry.id;
        const target = byDbf.get(dbfId) || byCardId.get(dbfMap.get(dbfId));
        if (target) {
          target.pool = false;
          target.preview = true;
          target.previewChangeType = 'removed';
          stats.removed++;
        } else {
          // 仍写入一条 removed 供 UI 过滤，但 pool=false
          const raw = patchCardToRaw(entry, dbfMap, 'removed');
          if (raw) {
            raw.pool = false;
            rawCards.push(raw);
            byDbf.set(raw.id, raw);
            stats.removed++;
          } else stats.skipped++;
        }
      }
      // notes 分区通常无 cards
    }
  }

  return stats;
}

async function main() {
  console.log('=== 生成临时预览卡池 ===');
  let ver = argVer;
  if (!ver) {
    const patches = await fetchJson('https://hsbg.cards/api/v1/patches');
    const list = Array.isArray(patches?.data) ? patches.data : patches;
    ver = list?.[0]?.currentPatch;
    if (!ver) throw new Error('无法解析最新 currentPatch');
  }
  console.log(`目标补丁: ${ver}`);

  const rawText = await fetchText(BGDB_URL);
  const rawDoc = JSON.parse(rawText);
  if (!rawDoc?.cards) throw new Error('bg_cards.json 无效');
  console.log(`BGDB 基线: v${rawDoc.meta?.version}, ${rawDoc.cards.length} 张`);

  const patch = await fetchJson(`https://hsbg.cards/api/v1/patches/${ver}`);
  const pdata = patch?.data || patch;
  if (!pdata?.sections) throw new Error('patch 结构无效');
  console.log(
    `补丁摘要: ${JSON.stringify(pdata.summary)}  sections=${pdata.sections.length}`,
  );

  const dbfMap = await loadDbfToCardId();
  const cards = rawDoc.cards.slice();
  const stats = applyPatch(cards, pdata, dbfMap);
  console.log('应用结果:', stats);

  // 构建期版本号标记为预览补丁
  const out = {
    meta: {
      version: `${ver}-preview`,
      fetchedAt: new Date().toISOString(),
      totalCards: cards.length,
      preview: true,
      baseVersion: rawDoc.meta?.version,
      patchVersion: ver,
      patchSummary: pdata.summary,
    },
    cards,
  };

  const rawOut = path.join(root, 'raw_bg_cards.json');
  writeFileSync(rawOut, JSON.stringify(out));
  console.log(`已写入 ${rawOut}  (v${out.meta.version}, ${cards.length})`);

  // 生成 public/data/cards.json
  const buildData = spawnSync(
    process.execPath,
    [path.join(root, 'scripts', 'build-data.mjs')],
    { stdio: 'inherit' },
  );
  if (buildData.status !== 0) {
    throw new Error('build-data.mjs 失败');
  }

  // 在 cards.json 上打 preview 元数据（build-data 会读 raw 的 meta.version）
  const cardsPath = path.join(root, 'public', 'data', 'cards.json');
  const cardsJson = JSON.parse(readFileSync(cardsPath, 'utf8'));
  cardsJson.preview = {
    enabled: true,
    patchVersion: ver,
    baseVersion: rawDoc.meta?.version,
    summary: pdata.summary,
    stats,
    generatedAt: out.meta.fetchedAt,
  };
  mkdirSync(path.dirname(cardsPath), { recursive: true });
  writeFileSync(cardsPath, JSON.stringify(cardsJson));
  console.log(`预览 cards.json 已更新: v${cardsJson.version} count=${cardsJson.count}`);
  console.log('下一步: npm run build:preview');
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
