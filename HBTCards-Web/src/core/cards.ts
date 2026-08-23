// 卡牌数据模型 + 筛选/分组逻辑（纯函数，可被将来 Taro 小程序复用）

export interface CardData {
  cardId: string;
  goldenCardId: string;
  name: string;
  nameZh: string;
  textZh: string;
  tier: number | null; // 随从星级 1~7；法术等级 1~7；无等级法术为 null
  minionType: string; // Title Case（"Beast"…"All"；"" = 中立）
  attack: number;
  health: number;
  keywords: string[];
  isBuddy: boolean;
  isTimewarped: boolean;
  dbfIdGold: number | null;
  // ── 全类型扩展（旧版 cards.json 无 cardType 时按随从处理）──
  cardType?: 'minion' | 'spell' | 'anomaly' | 'quest' | 'reward' | 'trinket';
  manaCost?: number | null; // 法术/畸变/饰品费用
  trinketTier?: 'lesser' | 'greater' | null; // 饰品：小/大
}

export interface CardsDb {
  version: string;
  generatedAt: string;
  count: number;
  cards: CardData[];
}

export type SpecialFilter =
  | 'BUDDY'
  | 'TIMEWARPED'
  // ── 特殊栏新增四类（主区域平铺整卡渲染图，非随从样式）──
  | 'SPELLS'
  | 'ANOMALIES'
  | 'QUESTS'
  | 'TRINKETS'
  | null;

export type ExtraSpecial = Exclude<SpecialFilter, 'BUDDY' | 'TIMEWARPED' | null>;

/** 是否为特殊栏新增的非随从类别 */
export function isExtraSpecial(s: SpecialFilter): s is ExtraSpecial {
  return s === 'SPELLS' || s === 'ANOMALIES' || s === 'QUESTS' || s === 'TRINKETS';
}

/** 新类别的展示元数据：面板标题 / 计数量词 */
export const EXTRA_META: Record<ExtraSpecial, { label: string; unit: string }> = {
  SPELLS: { label: '法术', unit: '张法术' },
  ANOMALIES: { label: '畸变', unit: '个畸变' },
  QUESTS: { label: '任务', unit: '个任务（含奖励）' },
  TRINKETS: { label: '饰品', unit: '件饰品' },
};

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

// ── 排序辅助 ──

const typeOf = (c: CardData): NonNullable<CardData['cardType']> => c.cardType ?? 'minion';

const byNameZh = (a: CardData, b: CardData) => a.nameZh.localeCompare(b.nameZh, 'zh');
const byTierThenName = (a: CardData, b: CardData) =>
  (a.tier ?? 99) - (b.tier ?? 99) || byNameZh(a, b);
const byCostThenName = (a: CardData, b: CardData) =>
  (a.manaCost ?? 999) - (b.manaCost ?? 999) || byNameZh(a, b);

/** 空分组直接丢弃 */
function buildSection(
  title: string,
  order: number,
  cards: CardData[],
  sort: (a: CardData, b: CardData) => number,
): Section[] {
  if (cards.length === 0) return [];
  return [{ title, order, cards: [...cards].sort(sort) }];
}

/** 特殊非随从类别：筛选 + 固定分段 */
function applyExtraSpecial(db: CardsDb, f: Filters): Section[] {
  let sub = db.cards.filter(c => {
    switch (f.special) {
      case 'SPELLS':
        return typeOf(c) === 'spell';
      case 'ANOMALIES':
        return typeOf(c) === 'anomaly';
      case 'QUESTS':
        return typeOf(c) === 'quest' || typeOf(c) === 'reward';
      case 'TRINKETS':
        return typeOf(c) === 'trinket';
      default:
        return false;
    }
  });

  // 关键词筛选对所有类别生效
  if (f.keyword) {
    const ens = KEYWORD_FILTERS.find(k => k.cn === f.keyword)?.en ?? [];
    sub = sub.filter(c => ens.some(en => c.keywords.includes(en)));
  }

  // 等级筛选仅对法术生效；种族筛选对特殊类别不生效
  if (f.special === 'SPELLS' && f.tier != null) sub = sub.filter(c => c.tier === f.tier);

  switch (f.special) {
    case 'SPELLS':
      return buildSection('法术', 0, sub, byTierThenName);
    case 'ANOMALIES':
      return buildSection('畸变', 0, sub, byNameZh);
    case 'QUESTS':
      return [
        ...buildSection('任务', 0, sub.filter(c => typeOf(c) === 'quest'), byNameZh),
        ...buildSection('任务奖励', 1, sub.filter(c => typeOf(c) === 'reward'), byNameZh),
      ];
    case 'TRINKETS':
      return [
        ...buildSection('小饰品', 0, sub.filter(c => c.trinketTier === 'lesser'), byCostThenName),
        ...buildSection('大饰品', 1, sub.filter(c => c.trinketTier !== 'lesser'), byCostThenName),
      ];
    default:
      return [];
  }
}

/** 与桌面版一致的筛选 + 分组逻辑（含特殊非随从类别） */
export function applyFilters(db: CardsDb, f: Filters): Section[] {
  if (isExtraSpecial(f.special)) return applyExtraSpecial(db, f);

  let list = db.cards;

  if (f.special === 'BUDDY') list = list.filter(c => c.isBuddy);
  else if (f.special === 'TIMEWARPED') list = list.filter(c => c.isTimewarped);
  else {
    // 常规浏览排除特殊类别（伙伴/时空扭曲及所有非随从类型）
    list = list.filter(c => !c.isBuddy && !c.isTimewarped && typeOf(c) === 'minion');
  }

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
      cards: [...cards].sort(
        (a, b) => (a.tier ?? 0) - (b.tier ?? 0) || a.nameZh.localeCompare(b.nameZh, 'zh'),
      ),
    }))
    .filter(s => s.cards.length > 0)
    .sort((a, b) => a.order - b.order);
}
