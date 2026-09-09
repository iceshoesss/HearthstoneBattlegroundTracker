import { useEffect, useMemo, useState } from 'react';
import type { CardData, CardsDb, ExtraSpecial, Filters, Section } from './core/cards';
import {
  applyFilters,
  EXTRA_META,
  isExtraSpecial,
  KEYWORD_FILTERS,
  RACE_CN,
  RACE_ORDER,
} from './core/cards';

// 随从卡图：构建时已本地化到 /img/cards/（方案C），加载零外部依赖
const TILE_URL = (cardId: string) => `/img/cards/${encodeURIComponent(cardId)}.jpg`;
// 大图预览：bgs 全卡渲染（与桌面版查询器同源）
const RENDER_URL = (id: string) =>
  `https://art.hearthstonejson.com/v1/bgs/latest/zhCN/512x/${encodeURIComponent(id)}.png`;
const OVERLAY = (name: string) => `/img/minions/${name}.png`;
// 英雄带框头像：拱形边框已烘焙在图内（256x272 透明底），与 HDT 同源
const HERO_URL = (cardId: string) =>
  `https://art.hearthstonejson.com/v1/heroes/latest/256x/${encodeURIComponent(cardId)}.png`;

// 种族 → 图标（与桌面版一致：Beast 用 pet.jpg，中立用 other.jpg）
const RACE_ICON: Record<string, string> = {
  Beast: 'pet.jpg',
  Demon: 'demon.jpg',
  Dragon: 'dragon.jpg',
  Elemental: 'elemental.jpg',
  Mech: 'mech.jpg',
  Murloc: 'murloc.jpg',
  Naga: 'naga.jpg',
  Pirate: 'pirate.jpg',
  Quilboar: 'quilboar.jpg',
  Undead: 'undead.jpg',
};

/* ═══════════ 斜角横幅（双层 clip-path 模拟描边） ═══════════ */
function Banner({ label }: { label: string }) {
  return (
    <div className="relative mx-3.5 mt-3 h-8">
      <div className="banner-shape-outer absolute inset-0">
        <div className="banner-shape-inner absolute inset-[1px] flex items-center justify-center">
          <span className="text-[13px] font-bold tracking-[0.35em] text-[#e8d5a2]">{label}</span>
        </div>
      </div>
    </div>
  );
}

function Panel({ children }: { children: React.ReactNode }) {
  return (
    <aside className="panel-bg min-h-0 overflow-y-auto rounded-xl border border-[#77572e] lg:h-full">
      {children}
    </aside>
  );
}

/* ═══════════ 星级按钮（HDT tier 盾徽 + 选中光晕） ═══════════ */
function TierButton({
  tier,
  active,
  onClick,
}: {
  tier: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      title={`${tier} 星`}
      className="relative mx-auto block h-[96px] w-[80px] cursor-pointer transition-opacity hover:opacity-85"
    >
      {active && (
        <img
          src="/img/tiers/tier-glow.png"
          alt=""
          className="pointer-events-none absolute inset-0 h-full w-full"
        />
      )}
      <img
        src={`/img/tiers/tier-${tier}.png`}
        alt={`${tier}星`}
        className="absolute inset-0 m-auto w-[74%]"
      />
    </button>
  );
}

/* ═══════════ 圆形类型/特殊按钮 ═══════════ */
function CircleButton({
  caption,
  active,
  onClick,
  children,
}: {
  caption: string;
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button onClick={onClick} className="relative mx-auto mb-1 h-[86px] w-[76px] cursor-pointer">
      {/* 光晕 */}
      <div
        className={`absolute inset-0 rounded-full transition-shadow ${
          active ? 'bg-amber-400/15 shadow-[0_0_16px_rgba(255,215,94,0.95)]' : ''
        }`}
      />
      {/* 外环 */}
      <div
        className={`absolute inset-x-1.5 top-1.5 bottom-[18px] rounded-full border-[3px] transition-colors ${
          active ? 'border-[#ffd75e]' : 'border-[#6b5433] hover:border-[#ffe9a8]'
        }`}
      />
      {/* 内容 */}
      <div className="absolute left-[9px] right-[9px] top-[9px] bottom-[24px]">{children}</div>
      {/* 标签 */}
      <span
        className={`absolute bottom-0 left-1/2 -translate-x-1/2 rounded bg-[#141008cc] px-1.5 py-px text-[10px] ${
          active ? 'text-[#ffd75e]' : 'text-[#d9c184]'
        }`}
      >
        {caption}
      </span>
    </button>
  );
}

function TribeCircle({ file }: { file: string }) {
  return (
    <img
      src={`/img/tribes/${file}`}
      alt=""
      className="h-full w-full rounded-full object-cover"
      draggable={false}
    />
  );
}

function CrownCircle() {
  return (
    <div className="flex h-full w-full items-center justify-center rounded-full bg-gradient-to-b from-[#9b30c0] to-[#5c1568] text-[28px] leading-none text-[#ffd75e]">
      ♛
    </div>
  );
}

function VoidCircle() {
  return (
    <img
      src="/img/special/BG34_HERO_004.png"
      alt=""
      className="h-full w-full rounded-full object-cover"
      draggable={false}
    />
  );
}

/* ═══════════ 新特殊类别圆形图标（真实图片） ═══════════ */
const EXTRA_IMAGE: Record<ExtraSpecial, string> = {
  SPELLS: '/img/special/spell.jpg',
  ANOMALIES: '/img/special/anomaly.png',
  QUESTS: '/img/special/BG24_HERO_100.png',
  TRINKETS: '/img/special/BG30_HERO_304.png',
  DARK_GIFTS: '/img/special/BG36_HERO_105.png',
  HEROES: '/img/special/BG20_HERO_202.png',
};
const EXTRA_POS: Partial<Record<ExtraSpecial, string>> = {
  DARK_GIFTS: 'translateX(4%)',
};

function GlyphCircle({ es }: { es: ExtraSpecial }) {
  const pos = EXTRA_POS[es];
  return (
    <div className="h-full w-full overflow-hidden rounded-full">
      <img
        src={EXTRA_IMAGE[es]}
        alt=""
        className="h-full w-full object-cover"
        style={pos ? { transform: pos } : undefined}
        draggable={false}
      />
    </div>
  );
}

/* ═══════════ 特殊类别平铺整卡渲染图（非随从样式；渲染图自带费用/等级，无需额外徽章） ═══════════ */
function SpecialTile({ c, onSelect }: { c: CardData; onSelect: () => void }) {
  const [failed, setFailed] = useState(false);
  const isHero = c.cardType === 'hero';

  if (failed)
    return (
      <div
        className="mx-[5px] my-[5px] flex w-[min(315px,calc(50vw-36px))] aspect-square cursor-pointer items-center justify-center rounded-md border border-[#77572e]/60 bg-[#1a1410] p-4 text-center text-base leading-relaxed text-[#d9c184]"
        onClick={onSelect}
        title={c.nameZh}
      >
        {c.nameZh || c.name}
      </div>
    );

  return (
    <div
      className="relative mx-[5px] my-[5px] w-[min(315px,calc(50vw-36px))] aspect-square cursor-pointer transition-transform hover:scale-[1.03]"
      onClick={onSelect}
      title={c.nameZh}
    >
      <img
        src={isHero ? HERO_URL(c.cardId) : RENDER_URL(c.cardId)}
        alt={c.nameZh}
        loading="lazy"
        decoding="async"
        className="h-full w-full object-contain drop-shadow-lg"
        onError={() => setFailed(true)}
      />
      {/* 英雄护甲盾徽（图源：HDT Resources/armor.png） */}
      {isHero && typeof c.armor === 'number' && (
        <div className="absolute bottom-[4px] right-[4px] h-[104px] w-[104px] drop-shadow-md">
          <img src="/img/heroes/armor.png" alt="" className="absolute inset-0 h-full w-full" />
          <span className="absolute inset-0 z-10 flex items-center justify-center text-[30px] font-bold text-white [text-shadow:_0_1px_3px_rgb(0_0_0_/_90%)]">
            {c.armor}
          </span>
        </div>
      )}
    </div>
  );
}

/* ═══════════ 关键词 chip（桌面版 PanelBg 风格） ═══════════ */
function KeywordChip({ cn, active, onClick }: { cn: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`m-0.5 cursor-pointer rounded border px-2 py-0.5 text-xs transition-colors ${
        active
          ? 'border-[#ffd75e] bg-[#4a3410] text-[#ffd75e]'
          : 'border-[#77572e] bg-[#261207]/70 text-[#cbb98a] hover:border-[#ffe9a8]'
      }`}
    >
      {cn}
    </button>
  );
}

/* ═══════════ 分组标题 ═══════════ */
function SectionHeader({ title, count }: { title: string; count: number }) {
  return (
    <div className="mx-[30px] my-[14px] grid h-[34px] place-items-center">
      <div className="col-start-1 row-start-1 h-[2px] w-full self-center bg-gradient-to-r from-transparent via-[#b99a5e]/70 to-transparent" />
      <div className="z-10 col-start-1 row-start-1 rounded-sm border-b border-[#b99a5e] bg-[#45305c] px-6 py-1 text-base font-bold text-[#e3cf9b]">
        {title} <span className="ml-1 text-xs font-normal opacity-60">{count}</span>
      </div>
    </div>
  );
}

/* ═══════════ 随从卡（256 画布等比缩放，坐标与桌面版完全一致） ═══════════ */
function MinionCard({ c, onSelect }: { c: CardData; onSelect: () => void }) {
  const has = (k: string) => c.keywords.includes(k);
  const overlay = (name: string) => (
    <img src={OVERLAY(name)} alt="" className="overlay-img" style={{ left: -24, top: -36, width: 300, height: 350 }} />
  );

  return (
    <div
      className="minion-card relative mx-[5px] my-[5px] cursor-pointer"
      style={{ width: 168, height: 200 }}
      onClick={onSelect}
    >
      {/* 星级盾徽（约 1/3 压在头像上） */}
      <img
        src={`/img/tiers/tier-${Math.max(1, Math.min(7, c.tier ?? 1))}.png`}
        alt=""
        className="absolute z-30 drop-shadow-md"
        style={{ top: 4, left: '50%', marginLeft: -26, width: 52 }}
      />
      {/* 256 画布 */}
      <div className="absolute" style={{ bottom: 0, left: 2, width: 164, height: 164 }}>
        <div className="canvas-256">
          {has('Taunt') && overlay('taunt')}
          {/* 肖像：椭圆裁剪（Fill 拉伸与桌面 ImageBrush 行为一致） */}
          <img
            src={TILE_URL(c.cardId)}
            alt={c.nameZh}
            decoding="async"
            className="absolute"
            style={{
              width: 256,
              height: 256,
              objectFit: 'fill',
              clipPath: 'ellipse(87px 120px at 128px 128px)',
            }}
          />
          {overlay('border')}
          {has('Reborn') && overlay('reborn')}
          {has('Deathrattle') && overlay('deathrattle')}
          {has('Poisonous') && overlay('poisonous')}
          {has('Venomous') && overlay('venomous')}
          {/* 属性面板 */}
          {overlay('stats')}
          {has('Divine Shield') && (
            <img
              src={OVERLAY('divine-shield')}
              alt=""
              className="overlay-img"
              style={{ left: -24, top: -36, width: 300, height: 350 }}
            />
          )}
          {/* 攻 / 血 */}
          <div className="stat-num" style={{ left: 29, top: 185 }}>
            {c.attack}
          </div>
          <div className="stat-num" style={{ left: 151, top: 185 }}>
            {c.health}
          </div>
        </div>
      </div>
    </div>
  );
}

/* ═══════════ 移动端：横向滚动过滤器条 ═══════════ */
function MobileFilterBar({
  filters,
  setFilters,
  selectRace,
  selectSpecial,
}: {
  filters: Filters;
  setFilters: React.Dispatch<React.SetStateAction<Filters>>;
  selectRace: (race: string | null) => void;
  selectSpecial: (special: Filters['special']) => void;
}) {
  const [showKeywords, setShowKeywords] = useState(false);

  return (
    <div className="panel-bg sticky top-0 z-30 border-b border-[#77572e]/60 px-2 py-2">
      {/* 种族筛选 */}
      <div className="scroll-chips">
        <button
          className={`scroll-chip ${!filters.race ? 'active' : ''}`}
          onClick={() => selectRace(null)}
        >
          全部种族
        </button>
        {RACE_ORDER.map(r => (
          <button
            key={r}
            className={`scroll-chip ${filters.race === r ? 'active' : ''}`}
            onClick={() => selectRace(r)}
          >
            {RACE_CN[r]}
          </button>
        ))}
        <button
          className={`scroll-chip ${filters.race === 'NEUTRAL' ? 'active' : ''}`}
          onClick={() => selectRace('NEUTRAL')}
        >
          中立
        </button>
      </div>

      {/* 特殊类别筛选 */}
      <div className="scroll-chips mt-1.5">
        {(
          ['BUDDY', 'TIMEWARPED', 'SPELLS', 'ANOMALIES', 'QUESTS', 'TRINKETS', 'DARK_GIFTS', 'HEROES'] as Filters['special'][]
        ).map(s => {
          const label =
            s === 'BUDDY'
              ? '伙伴'
              : s === 'TIMEWARPED'
                ? '时空扭曲'
                : EXTRA_META[s as ExtraSpecial]?.label ?? s;
          return (
            <button
              key={s}
              className={`scroll-chip ${filters.special === s ? 'active' : ''}`}
              onClick={() => selectSpecial(s)}
            >
              {label}
            </button>
          );
        })}
      </div>

      {/* 等级筛选 */}
      <div className="scroll-chips mt-1.5">
        <button
          className={`scroll-chip ${filters.tier === null ? 'active' : ''}`}
          onClick={() => setFilters(f => ({ ...f, tier: null }))}
        >
          全
        </button>
        {[1, 2, 3, 4, 5, 6, 7].map(t => (
          <button
            key={t}
            className={`scroll-chip ${filters.tier === t ? 'active' : ''}`}
            onClick={() => setFilters(f => ({ ...f, tier: f.tier === t ? null : t }))}
          >
            {t}★
          </button>
        ))}
      </div>

      {/* 关键词（可折叠） */}
      <div className="mt-1.5">
        <button
          className="flex items-center gap-1 text-xs text-[#cbb98a] hover:text-[#ffd75e] transition-colors"
          onClick={() => setShowKeywords(!showKeywords)}
        >
          <span className="text-[10px]">{showKeywords ? '▼' : '▶'}</span>
          关键词
        </button>
        {showKeywords && (
          <div className="scroll-chips mt-1">
            {KEYWORD_FILTERS.map(k => (
              <button
                key={k.cn}
                className={`scroll-chip ${filters.keyword === k.cn ? 'active' : ''}`}
                onClick={() =>
                  setFilters(f => ({ ...f, keyword: f.keyword === k.cn ? null : k.cn }))
                }
              >
                {k.cn}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

/* ═══════════ 主应用 ═══════════ */
export default function App() {
  const [db, setDb] = useState<CardsDb | null>(null);
  const [error, setError] = useState('');
  const [filters, setFilters] = useState<Filters>({
    tier: null,
    race: null,
    special: null,
    keyword: null,
  });

  useEffect(() => {
    fetch('/data/cards.json')
      .then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json() as Promise<CardsDb>;
      })
      .then(setDb)
      .catch(ex => setError(String(ex)));
  }, []);

  const sections: Section[] = useMemo(() => (db ? applyFilters(db, filters) : []), [db, filters]);
  const total = sections.reduce((n, s) => n + s.cards.length, 0);

  // ── 点击卡片 → 详情弹窗（普通 + 金色 + 文本） ──
  const [previewCard, setPreviewCard] = useState<CardData | null>(null);

  if (error)
    return (
      <div className="flex h-screen items-center justify-center text-red-400">
        数据加载失败:{error}
      </div>
    );

  /* ── 类型/特殊独立筛选（可叠加） ── */
  const selectRace = (race: string | null) =>
    setFilters(f => ({ ...f, race: f.race === race ? null : race }));
  const selectSpecial = (special: Filters['special']) =>
    setFilters(f => ({ ...f, special: f.special === special ? null : special }));

  /* ── 特殊非随从类别模式（法术/畸变/任务/饰品：平铺整卡渲染图） ── */
  const extraSpecial: ExtraSpecial | null = isExtraSpecial(filters.special)
    ? filters.special
    : null;
  const meta = extraSpecial ? EXTRA_META[extraSpecial] : null;
  const extraMode = extraSpecial != null;
  // 各类别总数（不受筛选影响，用于副标题计数；旧数据无 cardType 按随从计）
  const catTotals = useMemo(() => {
    const t = {
      minion: 0,
      buddy: 0,
      timewarpMinion: 0,
      timewarpSpell: 0,
      spell: 0,
      anomaly: 0,
      quest: 0,
      reward: 0,
      trinket: 0,
      darkGift: 0,
      hero: 0,
    };
    db?.cards.forEach(c => {
      // 伙伴 / 时空扭曲优先归类（随从与法术都可能带时空扭曲标记）
      if (c.isBuddy) {
        t.buddy++;
        return;
      }
      if (c.isTimewarped) {
        if ((c.cardType ?? 'minion') === 'minion') t.timewarpMinion++;
        else if (c.cardType === 'spell') t.timewarpSpell++;
        return;
      }
      // 黑暗之赐虽是 spell 类型，但已拆分为独立类别，单独计数
      if ((c.cardType ?? 'minion') === 'spell' && c.isDarkGift) {
        t.darkGift++;
        return;
      }
      const k = c.cardType ?? 'minion';
      if (k in t) t[k as keyof typeof t]++;
    });
    return t;
  }, [db]);
  // 副标题计数文案
  const extraTotal = (es: ExtraSpecial): number => {
    if (es === 'QUESTS') return catTotals.quest + catTotals.reward;
    if (es === 'DARK_GIFTS') return catTotals.darkGift;
    const key = {
      SPELLS: 'spell',
      ANOMALIES: 'anomaly',
      TRINKETS: 'trinket',
      HEROES: 'hero',
    } as const;
    return catTotals[key[es]] ?? 0;
  };
  let countLabel: string;
  if (filters.special === 'TIMEWARPED') {
    countLabel = `共 ${catTotals.timewarpMinion + catTotals.timewarpSpell} 张（随从 ${catTotals.timewarpMinion} · 法术 ${catTotals.timewarpSpell}）· 点击查看详情`;
  } else if (filters.special === 'BUDDY') {
    countLabel = `共 ${catTotals.buddy} 名伙伴 · 点击查看详情`;
  } else if (meta && extraSpecial) {
    countLabel = `共 ${extraTotal(extraSpecial)} ${meta.unit} · 点击查看详情`;
  } else {
    countLabel = `共 ${catTotals.minion} 名随从 · 点击卡片查看详情`;
  }

  return (
    <div className="min-h-screen">
      {/* ═══════════ 移动端布局 ═══════════ */}
      <div className="block lg:hidden">
        <MobileFilterBar
          filters={filters}
          setFilters={setFilters}
          selectRace={selectRace}
          selectSpecial={selectSpecial}
        />

        <div className="wood-frame mx-1 mt-1 rounded-[16px] border-2 border-[#241708] p-1">
          <div className="felt-bg rounded-[14px] border border-[#668a6a3f] p-3">
            {/* 移动端标题 */}
            <div className="py-2 text-center">
              <div className="parchment inline-flex h-[30px] items-center justify-center rounded-md border border-[#8a7345] px-4 shadow-lg">
                <span className="text-sm font-bold tracking-widest text-[#3a2c18]">
                  {meta ? meta.label.split('').join(' ') : '随 从'}
                </span>
              </div>
              <div className="mt-1 text-[10px] text-[#d9c184]/55">
                {countLabel}
              </div>
            </div>

            {/* 移动端卡牌网格 */}
            {!db ? (
              <div className="py-24 text-center text-zinc-300/70">加载中…</div>
            ) : total === 0 ? (
              <div className="py-24 text-center text-zinc-300/60">
                没有符合条件的{meta?.label ?? '卡牌'}
              </div>
            ) : (
              <div className="flex flex-wrap justify-center pb-2">
                {sections.map(s => (
                  <section key={`${s.kind}-${s.title}`}>
                    {/* 移动端分组标题 */}
                    <div className="my-2 text-center">
                      <span className="inline-block rounded bg-[#45305c] px-3 py-0.5 text-xs font-bold text-[#e3cf9b]">
                        {s.title} <span className="ml-1 font-normal opacity-60">{s.cards.length}</span>
                      </span>
                    </div>
                    <div className="flex flex-wrap justify-center">
                      {s.cards.map(c =>
                        s.kind === 'render' ? (
                          <SpecialTile
                            key={c.cardId}
                            c={c}
                            onSelect={() => setPreviewCard(c)}
                          />
                        ) : (
                          <MinionCard
                            key={c.cardId}
                            c={c}
                            onSelect={() => setPreviewCard(c)}
                          />
                        ),
                      )}
                    </div>
                  </section>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* ═══════════ 桌面端布局 ═══════════ */}
      <div className="hidden lg:flex lg:min-h-screen lg:items-start lg:justify-center lg:p-[14px]">
        <div className="wood-frame w-full max-w-[1560px] rounded-[22px] border-2 border-[#241708] p-1 lg:h-[calc(100vh-28px)]">
          <div className="h-full rounded-[20px] border border-[#668a6a3f] p-3">
            <div className="grid h-full min-h-0 gap-y-4 lg:grid-cols-[216px_1fr_216px]">

              {/* ───── 左：等级 + 关键词 ───── */}
              <Panel>
                <Banner label="等 级" />
                <div
                  aria-disabled={extraMode && filters.special !== 'SPELLS'}
                  className={`grid grid-cols-2 px-[14px] pt-2 transition-opacity ${
                    extraMode && filters.special !== 'SPELLS' ? 'pointer-events-none opacity-40' : ''
                  }`}
                >
                  {[1, 2, 3, 4, 5, 6, 7].map(t => (
                    <TierButton
                      key={t}
                      tier={t}
                      active={filters.tier === t}
                      onClick={() =>
                        setFilters(f => ({ ...f, tier: f.tier === t ? null : t }))
                      }
                    />
                  ))}
                </div>
                <Banner label="关 键 词" />
                <div className="flex flex-wrap px-2.5 pb-3 pt-1.5">
                  {KEYWORD_FILTERS.map(k => (
                    <KeywordChip
                      key={k.cn}
                      cn={k.cn}
                      active={filters.keyword === k.cn}
                      onClick={() =>
                        setFilters(f => ({ ...f, keyword: f.keyword === k.cn ? null : k.cn }))
                      }
                    />
                  ))}
                </div>
              </Panel>

              {/* ───── 中：随从列表 ───── */}
              <main className="felt-bg relative mx-0 flex min-h-0 flex-col overflow-hidden rounded-[14px] border-2 border-[#20142e] lg:mx-0">
                <div className="flex shrink-0 flex-col items-center pt-[10px]">
                  <div className="parchment flex h-[34px] w-[430px] max-w-[92%] items-center justify-center rounded-md border border-[#8a7345] shadow-lg">
                    <span className="text-base font-bold tracking-widest text-[#3a2c18]">
                      {meta ? meta.label.split('').join(' ') : '随 从'}
                    </span>
                  </div>
                  <span className="mt-1 text-[11px] tracking-wide text-[#d9c184]/55">
                    BattlegroundDB v{db?.version ?? '…'} · {countLabel}
                  </span>
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-2 pt-1">
                  {!db ? (
                    <div className="py-24 text-center text-zinc-300/70">加载中…</div>
                  ) : total === 0 ? (
                    <div className="py-24 text-center text-zinc-300/60">
                      没有符合条件的{meta?.label ?? '卡牌'}
                    </div>
                  ) : (
                    sections.map(s => (
                      <section key={`${s.kind}-${s.title}`}>
                        <SectionHeader title={s.title} count={s.cards.length} />
                        <div className="flex flex-wrap justify-center pb-2">
                          {s.cards.map(c =>
                            s.kind === 'render' ? (
                              <SpecialTile
                                key={c.cardId}
                                c={c}
                                onSelect={() => setPreviewCard(c)}
                              />
                            ) : (
                              <MinionCard
                                key={c.cardId}
                                c={c}
                                onSelect={() => setPreviewCard(c)}
                              />
                            ),
                          )}
                        </div>
                      </section>
                    ))
                  )}
                </div>
              </main>

              {/* ───── 右：类型 + 特殊 ───── */}
              <Panel>
                <Banner label="类 型" />
                <div
                  aria-disabled={extraMode}
                  className={`grid grid-cols-2 px-[14px] pt-2 transition-opacity ${
                    extraMode ? 'pointer-events-none opacity-40' : ''
                  }`}
                >
                  <CircleButton
                    caption="全部种族"
                    active={!filters.race}
                    onClick={() => selectRace(null)}
                  >
                    <CrownCircle />
                  </CircleButton>
                  {RACE_ORDER.map(r => (
                    <CircleButton
                      key={r}
                      caption={RACE_CN[r]}
                      active={filters.race === r}
                      onClick={() => selectRace(r)}
                    >
                      <TribeCircle file={RACE_ICON[r]} />
                    </CircleButton>
                  ))}
                  <CircleButton
                    caption="中立"
                    active={filters.race === 'NEUTRAL'}
                    onClick={() => selectRace('NEUTRAL')}
                  >
                    <TribeCircle file="other.jpg" />
                  </CircleButton>
                </div>

                <Banner label="特 殊" />
                <div className="grid grid-cols-2 px-[14px] pt-2">
                  <CircleButton
                    caption="伙伴"
                    active={filters.special === 'BUDDY'}
                    onClick={() => selectSpecial('BUDDY')}
                  >
                    <TribeCircle file="buddy.jpg" />
                  </CircleButton>
                  <CircleButton
                    caption="时空扭曲"
                    active={filters.special === 'TIMEWARPED'}
                    onClick={() => selectSpecial('TIMEWARPED')}
                  >
                    <VoidCircle />
                  </CircleButton>
                  {(
                    ['SPELLS', 'ANOMALIES', 'QUESTS', 'TRINKETS', 'DARK_GIFTS', 'HEROES'] as ExtraSpecial[]
                  ).map(es => (
                    <CircleButton
                      key={es}
                      caption={EXTRA_META[es].label}
                      active={filters.special === es}
                      onClick={() => selectSpecial(es)}
                    >
                      <GlyphCircle es={es} />
                    </CircleButton>
                  ))}
                </div>
              </Panel>
            </div>
          </div>
        </div>
      </div>

      {/* ═══════════ 详情弹窗（普通 + 金色 + 文本） ═══════════ */}
      {previewCard && (
        <CardModal card={previewCard} onClose={() => setPreviewCard(null)} />
      )}
    </div>
  );
}

/* ═══════════ 详情弹窗：普通 + 金色整卡渲染 + 卡牌文本 ═══════════ */
function CardModal({ card, onClose }: { card: CardData; onClose: () => void }) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-4"
      onClick={onClose}
    >
      <div
        className="relative flex flex-col gap-3 rounded-xl border border-[#b08d4f]/70 bg-[#141008f2] p-4 shadow-[0_16px_60px_rgba(0,0,0,0.8)]"
        onClick={e => e.stopPropagation()}
      >
        <button
          onClick={onClose}
          title="关闭 (Esc)"
          className="absolute -right-3 -top-3 h-8 w-8 cursor-pointer rounded-full border border-[#77572e] bg-[#261207] text-sm text-[#d9c184] transition-colors hover:border-[#ffd75e] hover:text-[#ffd75e]"
        >
          ✕
        </button>

        <div className="flex justify-center gap-3">
          {/* 非随从单位（法术/畸变/任务/饰品/黑暗之赐/英雄）无金色版本，只显示一张图片 */}
          {(card.cardType && card.cardType !== 'minion') ? (
            card.cardType === 'hero' ? (
              <img
                src={HERO_URL(card.cardId)}
                alt={card.nameZh}
                className="w-[280px] rounded-md bg-zinc-900/80"
              />
            ) : (
              <img
                src={RENDER_URL(card.cardId)}
                alt={card.nameZh}
                className="w-[280px] rounded-md bg-zinc-900/80"
              />
            )
          ) : (
            <>
              <img
                src={RENDER_URL(card.cardId)}
                alt={card.nameZh}
                className="w-[280px] rounded-md bg-zinc-900/80"
              />
              <img
                key={card.goldenCardId}
                src={RENDER_URL(card.goldenCardId + '_triple')}
                alt=""
                className="w-[280px] rounded-md ring-1 ring-amber-400/50 bg-zinc-900/80"
                onError={e => ((e.target as HTMLImageElement).style.display = 'none')}
              />
            </>
          )}
        </div>

        <div className="min-w-[568px] border-t border-[#77572e]/50 pt-2.5">
          <div className="text-base font-bold text-amber-200">
            {card.nameZh || card.name}
            <span className="ml-2 text-xs font-normal text-zinc-400">
              {card.tier != null && card.tier > 0 ? `${card.tier}★` : ''}
              {card.minionType
                ? `${card.tier != null && card.tier > 0 ? ' · ' : ''}${RACE_CN[card.minionType] ?? ''}`
                : ''}
              {card.armor != null && card.armor > 0 ? ` · 护甲 ${card.armor}` : ''}
            </span>
          </div>
          {/* 英雄技能 */}
          {card.cardType === 'hero' && card.heroPower && (
            <div className="mt-2 rounded-md border border-[#77572e]/40 bg-[#1a1208]/80 p-2.5">
              <div className="flex items-center gap-2 text-xs text-zinc-400">
                <span className="font-bold text-amber-300">英雄技能</span>
                {card.heroPower.manaCost != null && (
                  <span className="rounded bg-blue-900/50 px-1.5 py-0.5 text-[10px] text-blue-300">
                    {card.heroPower.manaCost} 费
                  </span>
                )}
              </div>
              <div className="mt-1 text-sm font-bold text-amber-200">
                {card.heroPower.nameZh || card.heroPower.name}
              </div>
              {card.heroPower.textZh && (
                <div
                  className="mt-1 text-xs leading-relaxed text-zinc-300 [&_b]:text-amber-200"
                  dangerouslySetInnerHTML={{ __html: card.heroPower.textZh }}
                />
              )}
            </div>
          )}
          {card.textZh && (
            <div
              className="mt-1.5 text-sm leading-relaxed text-zinc-300 [&_b]:text-amber-200"
              dangerouslySetInnerHTML={{ __html: card.textZh }}
            />
          )}
        </div>
      </div>
    </div>
  );
}

