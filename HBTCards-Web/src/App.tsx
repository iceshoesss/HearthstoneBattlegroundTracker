import { useEffect, useMemo, useState } from 'react';
import type { CardData, CardsDb, Filters, Section } from './core/cards';
import { applyFilters, KEYWORD_FILTERS, RACE_CN, RACE_ORDER } from './core/cards';

const TILE_URL = (cardId: string) =>
  `https://art.hearthstonejson.com/v1/tiles/${encodeURIComponent(cardId)}.png`;

function Chip({
  label,
  active,
  count,
  onClick,
}: {
  label: string;
  active: boolean;
  count?: number;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`rounded-full px-3.5 py-1.5 text-sm transition-colors ${
        active
          ? 'bg-amber-400/90 font-semibold text-zinc-900 shadow-[0_0_10px_rgba(251,191,36,0.45)]'
          : 'bg-zinc-800/80 text-zinc-300 hover:bg-zinc-700 hover:text-white'
      }`}
    >
      {label}
      {count != null && <span className="ml-1 opacity-70">{count}</span>}
    </button>
  );
}

function MinionCard({ c }: { c: CardData }) {
  const buffed = false; // 图鉴展示基础属性，无增益着色需求
  return (
    <div
      className="card-tile relative w-[150px] cursor-default overflow-hidden rounded-xl bg-zinc-900/80 ring-1 ring-zinc-700/60"
      title={`${c.nameZh}（${c.tier}星）`}
    >
      {/* 星级 */}
      <div className="absolute top-1.5 left-2 z-10 flex items-center gap-1 rounded-full bg-purple-950/90 px-2 py-0.5 text-xs font-bold text-amber-300 ring-1 ring-purple-500/50">
        {'★'.repeat(Math.max(1, Math.min(7, c.tier)))}
      </div>
      {/* 特殊角标 */}
      {(c.isBuddy || c.isTimewarped) && (
        <div className="absolute top-1.5 right-2 z-10 rounded-full bg-fuchsia-900/90 px-2 py-0.5 text-[10px] font-bold text-fuchsia-200">
          {c.isBuddy ? '伙伴' : '时空'}
        </div>
      )}
      <img
        src={TILE_URL(c.cardId)}
        alt={c.nameZh}
        loading="lazy"
        className="aspect-square w-full object-cover"
      />
      <div className="px-2 pt-1.5 pb-2">
        <div className="truncate text-sm font-medium text-zinc-100" title={c.nameZh}>
          {c.nameZh}
        </div>
        <div className="mt-1 flex items-center justify-between">
          <span className="rounded-full bg-amber-500/20 px-2 py-0.5 text-sm font-bold text-amber-300">
            {c.attack}
          </span>
          <span className="text-[11px] text-zinc-500">{c.minionType && RACE_CN[c.minionType]}</span>
          <span className="rounded-full bg-red-500/20 px-2 py-0.5 text-sm font-bold text-red-300">
            {c.health}
          </span>
        </div>
      </div>
    </div>
  );
}

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

  const sections: Section[] = useMemo(
    () => (db ? applyFilters(db, filters) : []),
    [db, filters],
  );
  const total = sections.reduce((n, s) => n + s.cards.length, 0);

  if (error)
    return (
      <div className="flex h-screen items-center justify-center text-red-400">数据加载失败：{error}</div>
    );

  const toggle = <K extends keyof Filters>(key: K, value: Filters[K]) =>
    setFilters(f => ({ ...f, [key]: f[key] === value ? (key === 'race' ? null : null) : value }));

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      {/* 头部 */}
      <header className="mb-5 flex items-end justify-between">
        <h1 className="text-xl font-bold tracking-wide text-amber-300">
          酒馆战棋图鉴
          {db && (
            <span className="ml-3 align-middle text-xs font-normal text-zinc-500">
              v{db.version} · {db.count} 随从 · 当前 {total}
            </span>
          )}
        </h1>
        <a href="/" className="text-xs text-zinc-600 hover:text-zinc-400">
          HBT Tools
        </a>
      </header>

      {/* 筛选区 */}
      <div className="space-y-2.5 rounded-2xl border border-zinc-800 bg-zinc-900/50 p-4">
        <FilterRow label="星级">
          {[1, 2, 3, 4, 5, 6, 7].map(t => (
            <Chip key={t} label={`${t}★`} active={filters.tier === t} onClick={() => toggle('tier', t)} />
          ))}
        </FilterRow>

        <FilterRow label="类型">
          <Chip label="全部种族" active={filters.race === null && !filters.special} onClick={() => setFilters(f => ({ ...f, race: null, special: null }))} />
          {RACE_ORDER.map(r => (
            <Chip
              key={r}
              label={RACE_CN[r]}
              active={filters.race === r}
              onClick={() => toggle('race', r)}
            />
          ))}
          <Chip label="中立" active={filters.race === 'NEUTRAL'} onClick={() => toggle('race', 'NEUTRAL')} />
        </FilterRow>

        <FilterRow label="特殊">
          <Chip label="伙伴" active={filters.special === 'BUDDY'} onClick={() => toggle('special', 'BUDDY')} />
          <Chip label="时空扭曲" active={filters.special === 'TIMEWARPED'} onClick={() => toggle('special', 'TIMEWARPED')} />
        </FilterRow>

        <FilterRow label="关键词">
          {KEYWORD_FILTERS.map(k => (
            <Chip
              key={k.cn}
              label={k.cn}
              active={filters.keyword === k.cn}
              onClick={() => toggle('keyword', k.cn)}
            />
          ))}
        </FilterRow>
      </div>

      {/* 分组列表 */}
      {!db ? (
        <div className="py-24 text-center text-zinc-500">加载中…</div>
      ) : total === 0 ? (
        <div className="py-24 text-center text-zinc-500">没有符合条件的随从</div>
      ) : (
        sections.map(s => (
          <section key={s.raw ?? s.title} className="mt-8">
            <h2 className="mb-3 flex items-center gap-3 text-base font-bold text-amber-100/90">
              <span className="text-amber-500/60">❖</span> {s.title}
              <span className="text-xs font-normal text-zinc-600">{s.cards.length}</span>
              <span className="h-px flex-1 bg-gradient-to-r from-amber-500/30 to-transparent" />
            </h2>
            <div className="flex flex-wrap gap-3">
              {s.cards.map(c => (
                <MinionCard key={c.cardId} c={c} />
              ))}
            </div>
          </section>
        ))
      )}

      <footer className="mt-16 pb-6 text-center text-[11px] leading-relaxed text-zinc-700">
        数据来源 BattlegroundDB v{db?.version} · 图片 HearthstoneJSON CDN
        <br />
        HBT League Tools — 非官方粉丝工具，与暴雪娱乐无关
      </footer>
    </div>
  );
}

function FilterRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      <span className="w-12 shrink-0 text-right text-xs font-semibold tracking-widest text-amber-200/60">
        {label}
      </span>
      <div className="flex flex-wrap gap-1.5">{children}</div>
    </div>
  );
}
