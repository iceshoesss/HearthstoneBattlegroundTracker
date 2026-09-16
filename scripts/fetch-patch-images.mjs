// 从 hsbg 补丁站下载新增/回归/调整卡的原画（HSJSON 尚未收录 PREVIEW_* 时使用）
// 用法: node scripts/fetch-patch-images.mjs [patchVersion]
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const argVer = process.argv[2];
const USER_AGENT = 'hbt-cards-preview';

async function fetchJson(url) {
  const res = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${url}`);
  return res.json();
}

async function download(url, dest) {
  if (existsSync(dest)) return 'skip';
  const res = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
  if (!res.ok) return 'fail';
  writeFileSync(dest, Buffer.from(await res.arrayBuffer()));
  return 'ok';
}

async function main() {
  let ver = argVer;
  if (!ver) {
    const patches = await fetchJson('https://hsbg.cards/api/v1/patches');
    const list = Array.isArray(patches?.data) ? patches.data : patches;
    ver = list?.[0]?.currentPatch;
    if (!ver) throw new Error('无法解析最新 currentPatch');
  }
  console.log(`拉取补丁图: ${ver}`);

  const patch = await fetchJson(`https://hsbg.cards/api/v1/patches/${ver}`);
  const pdata = patch?.data || patch;
  if (!pdata?.sections) throw new Error('patch 结构无效');

  const rendersDir = path.join(root, 'public', 'img', 'renders');
  mkdirSync(rendersDir, { recursive: true });

  const jobs = [];
  for (const sec of pdata.sections || []) {
    for (const entry of sec.cards || []) {
      const nc = entry.newCard || entry.oldCard || {};
      const cardId = `PREVIEW_${entry.id}`;
      if (nc.image) {
        jobs.push({
          url: `https://hsbg.cards${nc.image}`,
          dest: path.join(rendersDir, `${cardId}.png`),
          kind: 'normal',
          cardId,
        });
      }
      if (nc.imageGold) {
        jobs.push({
          url: `https://hsbg.cards${nc.imageGold}`,
          dest: path.join(rendersDir, `${cardId}_G_triple.png`),
          kind: 'golden',
          cardId,
        });
      }
    }
  }

  console.log(`待下载: ${jobs.length}`);
  let ok = 0, skip = 0, fail = 0;
  const failed = [];
  // 6 路并发
  let i = 0;
  async function worker() {
    while (i < jobs.length) {
      const j = jobs[i++];
      const r = await download(j.url, j.dest);
      if (r === 'ok') ok++;
      else if (r === 'skip') skip++;
      else {
        fail++;
        failed.push(j.url);
      }
    }
  }
  await Promise.all(Array.from({ length: 6 }, worker));
  console.log(`补丁图完成: 下载 ${ok}, 跳过 ${skip}, 失败 ${fail}`);
  if (failed.length) console.log(failed.slice(0, 5).join('\n'));
}

main().catch(e => {
  console.error(e);
  process.exit(1);
});
