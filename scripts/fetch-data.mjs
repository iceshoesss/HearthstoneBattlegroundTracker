// 从 BattlegroundDB 仓库最新 Release 拉取 cards.json（数据自动跟进）
// 失败时保留本地已提交的 public/data/cards.json（构建不中断）
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

const REPO = 'iceshoesss/BattlegroundDB';
const OUT = new URL('../public/data/cards.json', import.meta.url);
const LOCAL = readFileSync(OUT, 'utf8');
const localVersion = safe(() => JSON.parse(LOCAL).version, '?');

try {
  // 1. 查最新 release
  const rel = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
    headers: { 'User-Agent': 'hbt-cards' },
  });
  if (!rel.ok) throw new Error(`GitHub API ${rel.status}`);
  const data = await rel.json();
  // CI 生成的文件名为 web-cards.json（gh release 上传时 #标签 不改变资产名）
  const asset = (data.assets || []).find(a => a.name === 'web-cards.json');
  if (!asset) throw new Error('Release 中无 web-cards.json 资产');

  // 2. 下载
  const res = await fetch(asset.browser_download_url, { headers: { 'User-Agent': 'hbt-cards' } });
  if (!res.ok) throw new Error(`下载 ${res.status}`);
  const json = await res.text();
  const ver = safe(() => JSON.parse(json).version, '?');

  writeFileSync(OUT, json);
  console.log(`数据更新: v${localVersion} → v${ver}（来自 Release ${data.tag_name}）`);
} catch (err) {
  console.warn(`拉取最新数据失败(${err.message})，使用本地版本 v${localVersion}`);
}

function safe(fn, fallback) {
  try {
    return fn();
  } catch {
    return fallback;
  }
}
