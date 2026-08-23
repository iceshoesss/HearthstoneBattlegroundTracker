# HBT 酒馆战棋图鉴（网页版）

炉石传说酒馆战棋随从图鉴，纯前端静态站。样式与桌面版（`HBTCards/`）统一：木纹外框、斜角横幅、椭圆卡面、星级盾徽。

在线地址：`https://hbt-cards.pages.dev`（Cloudflare Pages）

## 功能

- **筛选**：星级（1~7）、种族（10 族 + 中立）、特殊（伙伴 / 时空扭曲 / 法术 / 畸变 / 任务 / 饰品）、关键词（战吼/亡语/复生/圣盾/烈毒/剧毒/风怒/嘲讽/发动/抉择/光环/回合开始/回合结束），可叠加组合
- **特殊图鉴**：法术、畸变、任务（含任务奖励）、饰品（小 / 大分段）以平铺整卡渲染图浏览，点击同样弹出详情弹窗
- **分组展示**：全部 → 中立 → 各种族，与游戏内浏览器一致
- **点击详情**：点击随从弹出普通 + 金色整卡渲染与完整效果文本（Esc / 点击遮罩 / ✕ 关闭）
- **版本显示**：牌匾下方显示当前 BattlegroundDB 数据版本

## 技术栈

React 19 + Vite + Tailwind CSS 4 + TypeScript，纯静态零后端。

## 本地开发

```powershell
cd HBTCards-Web
npm ci
npm run dev        # http://localhost:5173
```

## 构建与数据管线

```
npm run build
 ├─ scripts/build-data.mjs   生成 public/data/cards.json（从 BattlegroundDB 数据提取）
 ├─ scripts/fetch-images.mjs 下载全部随从图到 public/img/cards/（幂等，只补新卡）
 └─ vite build               打包静态产物到 dist/
```

- 构建产物 `dist/` 即完整站点（含数据 + 图片 + 字体），**无任何运行时外部依赖**
- 卡图已本地化，共约 475 张 / 6MB，随站发布

### 更新数据库

```powershell
# 1. 拿到新版 BattlegroundDB.dll（桌面版自动更新会下载，或手动从 API 拉取）
Copy-Item <新dll> ..\Lib\BattlegroundDB.dll -Force

# 2. 重新导出数据（需先从 DLL 提取 raw_bg_cards.json 放到项目根目录）
node scripts/build-data.mjs

# 3. 提交推送 → 自动重新部署
```

## 部署到 Cloudflare Pages

### 方式 A：Git 连仓自动部署（推荐）

1. 推送分支：
   ```powershell
   git push -u origin feature/card-browser
   ```

2. [Cloudflare Dashboard](https://dash.cloudflare.com) → **Workers & Pages** → **Create** → **Pages** → **Connect to Git**，授权并选择仓库 `HearthstoneBattlegroundTracker`

3. 构建配置：

   | 配置项 | 值 |
   |---|---|
   | Project name | `hbt-cards` |
   | Production branch | `feature/card-browser` |
   | Framework preset | `Vite` |
   | Build command | `npm ci && npm run build` |
   | Build output directory | `dist` |

4. **Save and Deploy** → 得到 `https://hbt-cards.pages.dev`

之后每次 push 到该分支自动重新构建发布；PR 自动附带预览链接。

### 方式 B：wrangler 手动部署

```powershell
cd HBTCards-Web
npm ci
npm run build
npx wrangler login                                    # 首次授权
npx wrangler pages deploy dist --project-name=hbt-cards
```

### 绑定自定义域名（可选）

Pages 项目 → **Custom domains** → 添加 `cards.你的域名`，按提示在 DNS 加 CNAME 指向 `hbt-cards.pages.dev`。

## 项目结构

```
HBTCards-Web/
├── src/
│   ├── core/cards.ts       数据模型 + 筛选/分组逻辑（纯函数，可复用）
│   ├── App.tsx             三栏布局 + 交互
│   ├── index.css           桌面版同款视觉（Tailwind + 自定义类）
│   └── main.tsx
├── scripts/
│   ├── build-data.mjs      生成精简版卡牌数据
│   └── fetch-images.mjs    下载随从卡图（幂等）
├── public/
│   ├── data/cards.json     卡牌数据
│   ├── img/cards/          随从图（构建时生成）
│   ├── img/minions|tiers|tribes/   边框/盾徽/种族图标
│   └── fonts/Chunkfive.otf 数字字体
└── index.html
```

## 说明

- 数据来源：BattlegroundDB（`Lib/BattlegroundDB.dll` 内嵌数据，v36.2.2+）
- 非官方粉丝工具，与暴雪娱乐无关
