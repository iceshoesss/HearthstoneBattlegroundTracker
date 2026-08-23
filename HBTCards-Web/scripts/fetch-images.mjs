// 构建时把全部随从图下载到 public/img/cards/（方案C：图片本地化随站发布）
// 幂等：已存在的文件跳过，只补新卡；8 路并发
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';

const db = JSON.parse(readFileSync(new URL('../public/data/cards.json', import.meta.url), 'utf8'));
const outDir = new URL('../public/img/cards/', import.meta.url);
mkdirSync(outDir, { recursive: true });

const list = db.cards;
let downloaded = 0;
let skipped = 0;
let failed = 0;

async function worker() {
  while (true) {
    const c = list.pop();
    if (!c) break;
    // 只下载随从图；非随从类型（法术/异变/任务/奖励/饰品）不渲染整卡图
    if (c.cardType && c.cardType !== 'minion') continue;
    const file = new URL(c.cardId + '.jpg', outDir);
    if (existsSync(file)) {
      skipped++;
      continue;
    }
    try {
      const res = await fetch(
        `https://art.hearthstonejson.com/v1/256x/${encodeURIComponent(c.cardId)}.jpg`,
      );
      if (!res.ok) {
        failed++;
        continue;
      }
      writeFileSync(file, Buffer.from(await res.arrayBuffer()));
      downloaded++;
    } catch {
      failed++;
    }
  }
}

await Promise.all(Array.from({ length: 8 }, worker));
console.log(`图片同步完成: 下载 ${downloaded}, 跳过 ${skipped}, 失败 ${failed}`);
