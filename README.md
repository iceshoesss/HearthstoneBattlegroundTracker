# HBT 卡牌图鉴 — 临时预览站（preview/36.6）

一次性预览分支：在 **BattlegroundDB 正式全量表** 上，叠上 hsbg **补丁 diff**
（新增 / 改动 / 移除 / 回归，含随从、法术、英雄、饰品等），手动构建后部署到
Cloudflare Pages。**不接入主站自动流水线。**

> 主站仍使用 `feature/card-browser` + `bgdb-update.yml`（正式数据）。

---

## 本分支有什么

- 完整 Web 工程（React + Vite），**仓库根目录即站点根**
- `scripts/generate-preview.mjs` — 可复用的预览池生成器
- `data-src/cards.battlegrounds.json` — dbfId → cardId 映射辅助
- `public/data/cards.json` — 已生成的预览卡池（可直接 build）

---

## 下次再做临时预览

### 1. 开新分支（从本分支或从 `feature/card-browser` 的 web 部分）

```bash
git checkout -b preview/36.8   # 换成目标补丁号
```

### 2. 生成预览卡池

```powershell
# 自动取 hsbg 最新 currentPatch
npm run generate-preview

# 或指定补丁
node scripts/generate-preview.mjs 36.6
```

脚本会：

1. 拉 BattlegroundDB `bg_cards.json`（正式基线）
2. 拉 `https://hsbg.cards/api/v1/patches/{ver}`
3. 拉 hsbg cards API 做 dbfId → `externalId` 映射
4. 应用 **added / changed / removed / returning**
5. 写出 `raw_bg_cards.json`（版本号形如 `36.6-preview`）
6. 跑 `build-data.mjs` → `public/data/cards.json`（附 `preview` 元数据）

### 3. 本地构建

```powershell
npm ci
npm run build:preview
# 产物: dist/
```

本地预览：

```powershell
npm run dev
# http://localhost:5173
```

> `build:preview` **不会**再 `fetch-raw`，避免把合成池冲回正式表。

### 4. 手动部署 Cloudflare Pages

任选其一：

```powershell
# A. wrangler（项目名可新建 hbt-cards-preview）
npx wrangler pages deploy dist --project-name=hbt-cards-preview

# B. 在 CF 控制台 → Pages → Create/Upload → 直接上传 dist 文件夹
```

---

## 数据口径说明

| 项 | 说明 |
|----|------|
| 基线 | BattlegroundDB master 全量（当前正式池） |
| 补丁 | hsbg `/api/v1/patches/{ver}` 分区 diff |
| `changed` | 用 `newCard` 覆盖，并标 `previewChangeType=changed` |
| `removed` | `pool=false`，构建时会被过滤出正式池展示 |
| 中文名 | 补丁源多为英文，`nameZh` 暂回退英文名 |
| 数值 | 预览卡可能带 `preview:true`，上线后可能调整 |

在 `raw_bg_cards.json` / `cards.json` 的 meta 上可看到：

```json
"preview": {
  "enabled": true,
  "patchVersion": "36.6",
  "baseVersion": "36.6",
  "summary": { "added": 60, "changed": 9, "removed": 68, "returning": 30 }
}
```

---

## 与正式站的关系

| | 正式站 | 预览站 |
|--|--------|--------|
| 分支 | `feature/card-browser` | `preview/*` |
| 部署 | Actions 自动 | **手动** |
| 数据 | 仅 BGDB 全量 | BGDB + patch diff |
| 生产项目 | `hbt-cards` | 建议 `hbt-cards-preview` |

正式版收录后：直接删预览分支/项目即可，主站无需回滚。
