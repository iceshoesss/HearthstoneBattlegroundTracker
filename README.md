# HBT 卡牌图鉴 — 临时预览站（preview/36.6）

在 **BattlegroundDB 正式全量表** 上叠 hsbg **补丁 diff**（新增 / 调整 / 移除 / 回归），
手动构建后部署到 Cloudflare Pages。**不接入主站自动流水线。**

> 主站仍使用 `feature/card-browser` + Actions（正式数据）。
>
> 当前补丁：**36.6.1**（hsbg 路径参数用 `36.6.1`，不是 `36.6`）。

---

## 完整流程（下次换补丁）

```powershell
# 0. 依赖
npm ci

# 1. 生成预览卡池（版本以 https://hsbg.cards/api/v1/patches 为准）
node scripts/generate-preview.mjs 36.6.1
# 或自动取最新: npm run generate-preview

# 2. 拉图 + 构建 → dist/
npm run build:preview

# 3. 本地检查
npm run dev
# http://localhost:5173

# 4. 部署（首次会创建 Pages 项目）
npx wrangler login          # 仅首次
npm run deploy:preview
# 等价于: npm run build:preview && npx wrangler pages deploy dist --project-name=hbt-cards-preview
```

开新补丁分支：

```bash
git checkout -b preview/36.8   # 换成目标补丁号
```

---

## 脚本职责

| 脚本 | 作用 |
|------|------|
| `scripts/generate-preview.mjs` | BGDB 基线 + hsbg patch → `raw_bg_cards.json` + `public/data/cards.json` |
| `scripts/build-data.mjs` | raw → 精简 web 数据（过滤 `pool=false`，透传 preview 徽章） |
| `scripts/fetch-images.mjs` | HSJSON 肖像 / 整卡 / 英雄头像 → `public/img/` |
| `scripts/fetch-patch-images.mjs` | hsbg 补丁站整卡原画兜底（正式 ID 尚无图时） |
| `npm run deploy:preview` | `build:preview` + wrangler 上传 `dist/` |

`generate-preview` 内部还会：

1. 拉 HSJSON **全量 enUS**，把补丁 dbfId 解析成正式 cardId（如 `BG36_110`）
2. 拉 HSJSON **zhCN**，回填中文名与描述（缓存到 `data-src/cards.zhCN.json`，已 gitignore）

---

## 数据口径

| 项 | 说明 |
|----|------|
| 基线 | BattlegroundDB master 全量 |
| 补丁 | hsbg `/api/v1/patches/{ver}` 分区 diff |
| cardId | HSJSON 全量按 dbfId 解析；仍未知才用 `PREVIEW_{dbfId}` 兜底 |
| `added` / `returning` / `changed` | 写入池，并标 `previewChangeType` |
| `removed` | `pool=false`，构建时过滤掉 |
| 中文 | HSJSON zhCN；无中文则回退英文名 |
| 原画 | 优先 HSJSON 正式图；缺图时 UI 回退补丁站整卡渲染 |

`public/data/cards.json` 的 meta：

```json
"preview": {
  "enabled": true,
  "patchVersion": "36.6.1",
  "baseVersion": "36.6.1",
  "summary": { "added": 82, "changed": 9, "removed": 68, "returning": 30 }
}
```

---

## 与正式站的关系

| | 正式站 | 预览站 |
|--|--------|--------|
| 分支 | `feature/card-browser` | `preview/*` |
| 部署 | Actions 自动 | **手动** `npm run deploy:preview` |
| 数据 | 仅 BGDB 全量 | BGDB + patch diff |
| 生产项目 | `hbt-cards` | `hbt-cards-preview` |

正式版收录后：删预览分支 / Pages 项目即可，主站无需回滚。

---

## 部署备忘

```powershell
# 首次登录 Cloudflare
npx wrangler login

# 构建并上传
npm run deploy:preview

# 只重新上传已有 dist（不重新构建）
npx wrangler pages deploy dist --project-name=hbt-cards-preview
```

也可在 Cloudflare Dashboard → Workers & Pages → Upload assets，直接拖 `dist/` 文件夹。

绑定自定义域名：Pages 项目 → **Custom domains** → 按提示加 CNAME。
