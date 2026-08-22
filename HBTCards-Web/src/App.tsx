import { useEffect, useMemo, useState } from 'react';
import type { CardData, CardsDb, Filters, Section } from './core/cards';
import { applyFilters, KEYWORD_FILTERS, RACE_CN, RACE_ORDER } from './core/cards';

// 随从卡图：构建时已本地化到 /img/cards/（方案C），加载零外部依赖
const TILE_URL = (cardId: string) => `/img/cards/${encodeURIComponent(cardId)}.jpg`;
// 大图预览：bgs 全卡渲染（与桌面版查询器同源）
const RENDER_URL = (id: string) =>
  `https://art.hearthstonejson.com/v1/bgs/latest/zhCN/512x/${encodeURIComponent(id)}.png`;
const OVERLAY = (name: string) => `/img/minions/${name}.png`;

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
    <div className="h-full w-full rounded-full bg-gradient-to-b from-[#3a2f52] to-[#17101f]" />
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
        src={`/img/tiers/tier-${Math.max(1, Math.min(7, c.tier))}.png`}
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

  return (
    <div className="flex min-h-screen items-start justify-center p-[14px]">
      <div className="wood-frame w-full max-w-[1560px] rounded-[22px] border-2 border-[#241708] p-1 lg:h-[calc(100vh-28px)]">
        <div className="h-full rounded-[20px] border border-[#668a6a3f] p-3">
          <div className="grid h-full min-h-0 gap-y-4 lg:grid-cols-[216px_1fr_216px]">

            {/* ───── 左：等级 + 关键词 ───── */}
            <Panel>
              <Banner label="等 级" />
              <div className="grid grid-cols-2 px-[14px] pt-2">
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
                  <span className="text-base font-bold tracking-widest text-[#3a2c18]">随 从</span>
                </div>
                <span className="mt-1 text-[11px] tracking-wide text-[#d9c184]/55">
                  BattlegroundDB v{db?.version ?? '…'} · 共 {db?.count ?? 0} 名随从 · 点击卡片查看详情
                </span>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-2 pt-1">
                {!db ? (
                  <div className="py-24 text-center text-zinc-300/70">加载中…</div>
                ) : total === 0 ? (
                  <div className="py-24 text-center text-zinc-300/60">没有符合条件的随从</div>
                ) : (
                  sections.map(s => (
                    <section key={s.title}>
                      <SectionHeader title={s.title} count={s.cards.length} />
                      <div className="flex flex-wrap justify-center pb-2">
                        {s.cards.map(c => (
                          <MinionCard key={c.cardId} c={c} onSelect={() => setPreviewCard(c)} />
                        ))}
                      </div>
                    </section>
                  ))
                )}
              </div>
            </main>

            {/* ───── 右：类型 + 特殊 ───── */}
            <Panel>
              <Banner label="类 型" />
              <div className="grid grid-cols-2 px-[14px] pt-2">
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
              </div>
            </Panel>
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

        <div className="flex gap-3">
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
        </div>

        <div className="min-w-[568px] border-t border-[#77572e]/50 pt-2.5">
          <div className="text-base font-bold text-amber-200">
            {card.nameZh || card.name}
            <span className="ml-2 text-xs font-normal text-zinc-400">
              {card.tier}★{card.minionType ? ' · ' + (RACE_CN[card.minionType] ?? '') : ''}
            </span>
          </div>
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

