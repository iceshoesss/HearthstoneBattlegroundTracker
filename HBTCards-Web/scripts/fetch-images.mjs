// 构建时把全部图片下载到 public/img/（图片本地化随站发布）
// 幂等：已存在的文件跳过，只补新卡；8 路并发
// 包含：卡牌肖像(256x)、整卡渲染图(512x)、英雄头像(256x)
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';

const db = JSON.parse(readFileSync(new URL('../public/data/cards.json', import.meta.url), 'utf8'));

// 目录定义
const cardsDir = new URL('../public/img/cards/', import.meta.url);
const rendersDir = new URL('../public/img/renders/', import.meta.url);
const heroesDir = new URL('../public/img/heroes-portrait/', import.meta.url);
mkdirSync(cardsDir, { recursive: true });
mkdirSync(rendersDir, { recursive: true });
mkdirSync(heroesDir, { recursive: true });

const list = [...db.cards]; // 复制数组，避免修改原数组
let downloaded = 0;
let skipped = 0;
let failed = 0;

async function worker() {
  while (true) {
    const c = list.pop();
    if (!c) break;

    // 1. 下载卡牌肖像 (256x, jpg) - 仅随从
    if (!c.cardType || c.cardType === 'minion') {
      const portraitFile = new URL(c.cardId + '.jpg', cardsDir);
      if (!existsSync(portraitFile)) {
        try {
          const res = await fetch(
            `https://art.hearthstonejson.com/v1/256x/${encodeURIComponent(c.cardId)}.jpg`,
          );
          if (res.ok) {
            writeFileSync(portraitFile, Buffer.from(await res.arrayBuffer()));
            downloaded++;
          } else {
            failed++;
          }
        } catch {
          failed++;
        }
      } else {
        skipped++;
      }
    }

    // 2. 下载整卡渲染图 (512x, png) - 所有类型
    // 使用 bgs/latest/zhCN 路径，包含中文卡牌文本
    const renderId = c.cardType === 'hero' ? c.cardId : c.cardId;
    const renderFile = new URL(renderId + '.png', rendersDir);
    if (!existsSync(renderFile)) {
      try {
        const res = await fetch(
          `https://art.hearthstonejson.com/v1/bgs/latest/zhCN/512x/${encodeURIComponent(renderId)}.png`,
        );
        if (res.ok) {
          writeFileSync(renderFile, Buffer.from(await res.arrayBuffer()));
          downloaded++;
        } else {
          failed++;
        }
      } catch {
        failed++;
      }
    } else {
      skipped++;
    }

    // 3. 下载英雄头像 (256x, png) - 仅英雄
    if (c.cardType === 'hero') {
      const heroFile = new URL(c.cardId + '.png', heroesDir);
      if (!existsSync(heroFile)) {
        try {
          const res = await fetch(
            `https://art.hearthstonejson.com/v1/heroes/latest/256x/${encodeURIComponent(c.cardId)}.png`,
          );
          if (res.ok) {
            writeFileSync(heroFile, Buffer.from(await res.arrayBuffer()));
            downloaded++;
          } else {
            failed++;
          }
        } catch {
          failed++;
        }
      } else {
        skipped++;
      }
    }
  }
}

await Promise.all(Array.from({ length: 8 }, worker));
console.log(`图片同步完成: 下载 ${downloaded}, 跳过 ${skipped}, 失败 ${failed}`);
