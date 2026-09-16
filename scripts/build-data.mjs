// 从 BattlegroundDB 导出的 raw_bg_cards.json 生成精简版 web 数据
// 用法: node scripts/build-data.mjs   （需先存在 raw_bg_cards.json）
import { readFileSync, writeFileSync, existsSync } from 'node:fs';

const raw = JSON.parse(readFileSync(new URL('../raw_bg_cards.json', import.meta.url), 'utf8'));

// dbfId → cardId 全量表，用于解析金色版本 cardId
// 优先使用 cards.battlegrounds.json（HearthstoneJSON），覆盖更全
const hsbgJsonPath = new URL('../../HBTCards/Data/cards.battlegrounds.json', import.meta.url);
let getCardIdByDbfId;
if (existsSync(hsbgJsonPath)) {
  const hsbgCards = JSON.parse(readFileSync(hsbgJsonPath, 'utf8'));
  const byDbfId = new Map(hsbgCards.filter(c => c.dbfId > 0).map(c => [c.dbfId, c]));
  // HearthstoneJSON 使用 `id` 字段，需要适配
  getCardIdByDbfId = (dbfId) => byDbfId.get(dbfId)?.id ?? null;
  console.log(`使用 HearthstoneJSON 索引: ${byDbfId.size} 条`);
} else {
  const byDbfId = new Map(raw.cards.map(c => [c.id, c]));
  // BattlegroundDB 使用 `cardId` 字段
  getCardIdByDbfId = (dbfId) => byDbfId.get(dbfId)?.cardId ?? null;
  console.log('使用 BattlegroundDB 索引（金色版本解析可能不完整）');
}

// 黑暗之赐：由选取法术 BG36_MidGameEffect_000 的 childIds 精确圈定
// （不能用 cardId 前缀 _000t 匹配 —— 会混入黑暗之赐衍生出的二级 token，如 t28t/t29t）
const darkGiftParent = raw.cards.find(c => c.cardId === 'BG36_MidGameEffect_000');
const darkGiftIds = new Set(darkGiftParent?.childIds ?? []);

const cards = raw.cards
  .filter(c => c.cardType === 'minion' && !c.isToken && !c.isDuosOnly)
  .map(c => ({
    cardId: c.cardId,
    goldenCardId: c.dbfIdGold ? getCardIdByDbfId(c.dbfIdGold) ?? `${c.cardId}_G` : `${c.cardId}_G`,
    name: (c.name || '').trim(),
    nameZh: (c.nameZh || '').trim(),
    textZh: c.textZh || '',
    cardType: 'minion',
    tier: c.tier ?? 0,
    manaCost: null,
    trinketTier: null,
    armor: null,
    minionType: c.minionType ?? '',
    attack: c.attack ?? 0,
    health: c.health ?? 0,
    keywords: c.keywords ?? [],
    isBuddy: !!c.isBuddy,
    isTimewarped: !!c.isTimewarped,
    isDarkGift: false,
    dbfIdGold: c.dbfIdGold ?? null,
  }))
  .sort((a, b) => (a.tier - b.tier) || a.nameZh.localeCompare(b.nameZh, 'zh'));

// 构建英雄技能 id → 技能数据 映射（用于给英雄附加 heroPower 信息）
const heroPowerById = new Map(
  raw.cards
    .filter(c => c.cardType === 'hero_power' && !c.isToken)
    .map(c => [c.id, c])
);

// 非随从类型：法术 / 异变 / 任务 / 奖励 / 饰品 / 英雄（hero_power 不单独收录，仅附加到英雄）
// 注：仅双人模式的异变（isDuosOnly）是有意保留的 —— 异变本身就是双人模式机制
const otherTypes = ['spell', 'anomaly', 'quest', 'reward', 'trinket', 'hero'];
const others = raw.cards
  .filter(c => otherTypes.includes(c.cardType) && !c.isToken)
  .map(c => {
    // 英雄：从 childIds 中提取 hero_power 技能信息
    let heroPower = null;
    if (c.cardType === 'hero' && c.childIds) {
      const hpId = c.childIds.find(id => heroPowerById.has(id));
      if (hpId) {
        const hp = heroPowerById.get(hpId);
        heroPower = {
          cardId: hp.cardId,
          name: (hp.name || '').trim(),
          nameZh: (hp.nameZh || '').trim(),
          textZh: hp.textZh || '',
          manaCost: hp.manaCost ?? null,
          keywords: hp.keywords ?? [],
        };
      }
    }
    return {
      cardId: c.cardId,
    goldenCardId: c.dbfIdGold ? getCardIdByDbfId(c.dbfIdGold) ?? `${c.cardId}_G` : `${c.cardId}_G`,
      name: (c.name || '').trim(),
      nameZh: (c.nameZh || '').trim(),
      textZh: c.textZh || '',
      cardType: c.cardType,
      tier: c.tier ?? null,
      manaCost: c.manaCost ?? null,
      trinketTier: c.trinketTier ?? null,
      armor: c.armor ?? null, // 英雄护甲值
      minionType: '',
      attack: 0,
      health: 0,
      keywords: c.keywords ?? [],
      isBuddy: false,
      isTimewarped: !!c.isTimewarped, // 注：时空扭曲法术确实存在（如各英雄「XX之力」，共 32 张）
      isDarkGift: darkGiftIds.has(c.id),
      dbfIdGold: c.dbfIdGold ?? null,
      heroPower, // 英雄技能（仅英雄类型有值）
    };
  });

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
const darkGiftCount = all.filter(c => c.isDarkGift).length;
const breakdown = Object.entries(byType).map(([t, n]) => `${t} ${n}`).join(', ');
console.log(
  `cards.json 生成完毕: v${out.version}, 共 ${out.count} 张 (${breakdown}), 黑暗之赐 ${darkGiftCount}`,
);
