// 拉取 BattlegroundDB 最新全量 bg_cards.json → raw_bg_cards.json
// GitHub Actions 与 Cloudflare Pages 共用（prebuild 会先跑本脚本）。
import { writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';

const URL_BG =
  'https://raw.githubusercontent.com/iceshoesss/BattlegroundDB/master/BattlegroundDB/Data/bg_cards.json';
const OUT = new URL('../raw_bg_cards.json', import.meta.url);

async function downloadViaFetch() {
  const res = await fetch(URL_BG, { headers: { 'User-Agent': 'hbt-cards-build' } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return await res.text();
}

function downloadViaCurl() {
  const r = spawnSync(
    'curl',
    ['-fsSL', '-A', 'hbt-cards-build', URL_BG],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  if (r.status !== 0) {
    throw new Error(r.stderr || `curl exit ${r.status}`);
  }
  return r.stdout;
}

let text;
try {
  text = await downloadViaFetch();
} catch (err) {
  console.warn(`fetch 失败(${err.message})，改用 curl`);
  text = downloadViaCurl();
}

const parsed = JSON.parse(text);
if (!parsed?.meta?.version || !Array.isArray(parsed.cards)) {
  throw new Error('bg_cards.json 结构异常（缺 meta.version 或 cards）');
}

writeFileSync(OUT, text);
console.log(
  `raw_bg_cards.json 已更新: v${parsed.meta.version}, ${parsed.meta.totalCards ?? parsed.cards.length} 张`,
);
