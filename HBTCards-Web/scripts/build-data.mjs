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
    tier: c.tier ?? 0,
    minionType: c.minionType ?? '',
    attack: c.attack ?? 0,
    health: c.health ?? 0,
    keywords: c.keywords ?? [],
    isBuddy: !!c.isBuddy,
    isTimewarped: !!c.isTimewarped,
    dbfIdGold: c.dbfIdGold ?? null,
  }))
  .sort((a, b) => (a.tier - b.tier) || a.nameZh.localeCompare(b.nameZh, 'zh'));

const out = {
  version: raw.meta?.version ?? 'unknown',
  generatedAt: new Date().toISOString().slice(0, 10),
  count: cards.length,
  cards,
};

writeFileSync(new URL('../public/data/cards.json', import.meta.url), JSON.stringify(out));
console.log(`cards.json 生成完毕: v${out.version}, ${out.count} 个随从`);
