// 卡牌数据模型 + 筛选/分组逻辑（纯函数，可被将来 Taro 小程序复用）

export interface CardData {
  cardId: string;
  goldenCardId: string;
  name: string;
  nameZh: string;
  textZh: string;
  tier: number;
  minionType: string; // Title Case（"Beast"…"All"；"" = 中立）
  attack: number;
  health: number;
  keywords: string[];
  isBuddy: boolean;
  isTimewarped: boolean;
  dbfIdGold: number | null;
}

export interface CardsDb {
  version: string;
  generatedAt: string;
  count: number;
  cards: CardData[];
}

export type SpecialFilter = 'BUDDY' | 'TIMEWARPED' | null;

export interface Filters {
  tier: number | null;
  race: string | null; // Title Case / "NEUTRAL" / null=全部
  special: SpecialFilter;
  keyword: string | null; // 中文关键词
}

export const RACE_ORDER = [
  'Beast',
  'Demon',
  'Dragon',
  'Elemental',
  'Mech',
  'Murloc',
  'Naga',
  'Pirate',
  'Quilboar',
  'Undead',
] as const;

export const RACE_CN: Record<string, string> = {
  Beast: '野兽',
  Demon: '恶魔',
  Dragon: '龙',
  Elemental: '元素',
  Mech: '机械',
  Murloc: '鱼人',
  Naga: '纳迦',
  Pirate: '海盗',
  Quilboar: '野猪人',
  Undead: '亡灵',
  All: '全部',
};

export const KEYWORD_FILTERS: { cn: string; en: string[] }[] = [
  { cn: '战吼', en: ['Battlecry'] },
  { cn: '亡语', en: ['Deathrattle'] },
  { cn: '复生', en: ['Reborn'] },
  { cn: '圣盾', en: ['Divine Shield'] },
  { cn: '烈毒', en: ['Venomous'] },
  { cn: '剧毒', en: ['Poisonous'] },
  { cn: '风怒', en: ['Windfury', 'Mega-Windfury'] },
  { cn: '嘲讽', en: ['Taunt'] },
  { cn: '发动', en: ['Activate'] },
  { cn: '抉择', en: ['Choose One'] },
  { cn: '光环', en: ['Aura'] },
  { cn: '回合开始', en: ['Start of Turn'] },
  { cn: '回合结束', en: ['End of Turn'] },
];

export function raceName(raw: string): string {
  if (!raw) return '';
  return RACE_CN[raw] ?? raw;
}

export interface Section {
  title: string;
  order: number;
  cards: CardData[];
}

/** 与桌面版一致的筛选 + 分组逻辑 */
export function applyFilters(db: CardsDb, f: Filters): Section[] {
  let list = db.cards;

  if (f.special === 'BUDDY') list = list.filter(c => c.isBuddy);
  else if (f.special === 'TIMEWARPED') list = list.filter(c => c.isTimewarped);
  else {
    // 常规浏览排除特殊类别
    list = list.filter(c => !c.isBuddy && !c.isTimewarped);
  }

  // 种族筛选对所有模式生效（含伙伴/时空扭曲）
  if (f.race === 'NEUTRAL') list = list.filter(c => !c.minionType);
  else if (f.race) list = list.filter(c => c.minionType === f.race);

  // 种族筛选对所有模式生效（含伙伴/时空扭曲）
  if (f.race === 'NEUTRAL') list = list.filter(c => !c.minionType);
  else if (f.race) list = list.filter(c => c.minionType === f.race);

  if (f.tier != null) list = list.filter(c => c.tier === f.tier);

  if (f.keyword) {
    const ens = KEYWORD_FILTERS.find(k => k.cn === f.keyword)?.en ?? [];
    list = list.filter(c => ens.some(en => c.keywords.includes(en)));
  }

  const groupOrder = (raw: string): number =>
    raw === 'All' ? 0 : raw === '' ? 1 : 2 + Math.max(0, RACE_ORDER.indexOf(raw as never));

  const map = new Map<string, CardData[]>();
  for (const c of list) {
    const key = c.minionType || '';
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(c);
  }

  return [...map.entries()]
    .map(([raw, cards]) => ({
      raw,
      title: raw === 'All' ? '全部' : raw === '' ? '中立' : raceName(raw),
      order: groupOrder(raw),
      cards: [...cards].sort((a, b) => a.tier - b.tier || a.nameZh.localeCompare(b.nameZh, 'zh')),
    }))
    .filter(s => s.cards.length > 0)
    .sort((a, b) => a.order - b.order);
}
