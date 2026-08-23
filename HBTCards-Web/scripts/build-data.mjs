// 从 BattlegroundDB 导出的 raw_bg_cards.json 生成精简版 web 数据
// 用法: node scripts/build-data.mjs   （需先存在 raw_bg_cards.json）
import { readFileSync, writeFileSync } from 'node:fs';

const raw = JSON.parse(readFileSync(new URL('../raw_bg_cards.json', import.meta.url), 'utf8'));

// dbfId → cardId 全量表，用于解析金色版本 cardId
const byDbfId = new Map(raw.cards.map(c => [c.id, c]));

const cards = raw.cards
  .filter(c => c.cardType === 'minion' && !c.isToken && !c.isDuosOnly)
  .map(c => ({
    cardId: c.cardId,
    goldenCardId: c.dbfIdGold ? byDbfId.get(c.dbfIdGold)?.cardId ?? `${c.cardId}_G` : `${c.cardId}_G`,
    name: (c.name || '').trim(),
    nameZh: (c.nameZh || '').trim(),
    textZh: c.textZh || '',
    cardType: 'minion',
    tier: c.tier ?? 0,
    manaCost: null,
    trinketTier: null,
    minionType: c.minionType ?? '',
    attack: c.attack ?? 0,
    health: c.health ?? 0,
    keywords: c.keywords ?? [],
    isBuddy: !!c.isBuddy,
    isTimewarped: !!c.isTimewarped,
    dbfIdGold: c.dbfIdGold ?? null,
  }))
  .sort((a, b) => (a.tier - b.tier) || a.nameZh.localeCompare(b.nameZh, 'zh'));

// 非随从类型：法术 / 异变 / 任务 / 奖励 / 饰品（hero 与 hero_power 不收录）
// 注：仅双人模式的异变（isDuosOnly）是有意保留的 —— 异变本身就是双人模式机制
const otherTypes = ['spell', 'anomaly', 'quest', 'reward', 'trinket'];
const others = raw.cards
  .filter(c => otherTypes.includes(c.cardType) && !c.isToken)
  .map(c => ({
    cardId: c.cardId,
    goldenCardId: c.dbfIdGold ? byDbfId.get(c.dbfIdGold)?.cardId ?? `${c.cardId}_G` : `${c.cardId}_G`,
    name: (c.name || '').trim(),
    nameZh: (c.nameZh || '').trim(),
    textZh: c.textZh || '',
    cardType: c.cardType,
    tier: c.tier ?? null,
    manaCost: c.manaCost ?? null,
    trinketTier: c.trinketTier ?? null,
    minionType: '',
    attack: 0,
    health: 0,
    keywords: c.keywords ?? [],
    isBuddy: false,
    isTimewarped: false,
    dbfIdGold: c.dbfIdGold ?? null,
  }));

// 排序：按类型顺序（spell → anomaly → quest → reward → trinket），组内 tier → manaCost → 名称
others.sort((a, b) =>
  (otherTypes.indexOf(a.cardType) - otherTypes.indexOf(b.cardType)) ||
  ((a.tier ?? 99) - (b.tier ?? 99)) ||
  ((a.manaCost ?? 0) - (b.manaCost ?? 0)) ||
  a.nameZh.localeCompare(b.nameZh, 'zh'),
);

const all = [...cards, ...others];

const out = {
  version: raw.meta?.version ?? 'unknown',
  generatedAt: new Date().toISOString().slice(0, 10),
  count: all.length,
  cards: all,
};

writeFileSync(new URL('../public/data/cards.json', import.meta.url), JSON.stringify(out));

const byType = {};
for (const c of all) byType[c.cardType] = (byType[c.cardType] || 0) + 1;
const breakdown = Object.entries(byType).map(([t, n]) => `${t} ${n}`).join(', ');
console.log(`cards.json 生成完毕: v${out.version}, 共 ${out.count} 张 (${breakdown})`);
