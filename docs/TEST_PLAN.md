# 数据自动更新链路 — 测试方案

覆盖 BattlegroundDB 发布 → 网页版自动重建部署的完整链路，分四层测试。

## 链路总览

```
[BattlegroundDB 仓库]
① 定时/手动触发 build.yml
② HsbgFetcher 拉数据 → 构建 DLL
③ 生成 web-cards.json
④ 版本检查（无新版则跳过发布）
⑤ 上传 DLL 到 CF KV + 部署 Worker
⑥ GitHub Release 发布（附 cards.json）
⑦ repository_dispatch → HBT 仓库
        ↓
[HearthstoneBattlegroundTracker 仓库]
⑧ bgdb-update workflow 触发
⑨ fetch-data 拉最新 cards.json（失败回退本地）
⑩ fetch-images 补齐新卡图片（幂等）
⑪ vite build → wrangler pages deploy
        ↓
[线上]
⑫ https://hbt-cards.pages.dev 更新
```

---

## 第一层：BattlegroundDB CI（①~⑥）

| 用例 | 步骤 | 预期 |
|---|---|---|
| T1-1 定时构建 | 等 cron 触发，或手动 Workflow dispatch | 全部步骤绿色 |
| T1-2 数据新鲜 | 检查 `BattlegroundDB/Data/bg_cards.json` 的 meta.version | 与 hsbg.cards 当前版本一致 |
| T1-3 web-cards.json 生成 | 在「Get version」步骤前，检查 `web-cards.json` 是否存在、可解析 | JSON 合法；version 字段与源数据一致 |
| T1-4 字段完整性 | 抽查 `web-cards.json`：首条卡含 cardId/goldenCardId/nameZh/tier/minionType/attack/health/keywords/isBuddy/isTimewarped | 字段齐全；isToken/isDuosOnly 卡已剔除 |
| T1-5 Release 资产 | 到 GitHub Releases 看最新版 | 含 `BattlegroundDB.dll` 和 `cards.json` 两个资产 |
| T1-6 幂等/跳过 | 版本未变时手动触发一次 | NEW_VERSION=false，跳过发布/触发，不产生重复 Release |
| T1-7 强制更新 | 手动触发勾选 force=true | 即使版本相同也重新发布（覆盖旧 Release）|

**验证方式**：GitHub Actions → build.yml → 查看各 step 日志；Releases 页面核对资产。

---

## 第二层：跨仓库触发（⑦）

| 用例 | 步骤 | 预期 |
|---|---|---|
| T2-1 dispatch 触发 | Release 创建成功后，去 HBT 仓库 Actions 页 | 出现 `Bgdb Update → Rebuild Cards Web` 运行记录，event 来源 repository_dispatch |
| T2-2 版本载荷 | 查看 workflow 日志 / payload | client_payload.version 与 Release 版本一致 |
| T2-3 失败情况 | 若 `REPO_DISPATCH_TOKEN` 未配置或权限不足 | build.yml 该 step 报错（不影响前面 DLL/KV/Release 成功）；HBT 侧无新构建 |

**前置配置**（一次性）：
- BattlegroundDB 仓库 Secrets 添加 `REPO_DISPATCH_TOKEN`：PAT，权限勾选 HBT 仓库的 **repo**（contents+actions 写）——注意**必须**是能写目标仓库的 PAT，默认 GITHUB_TOKEN 跨仓库无效

---

## 第三层：网页版构建（⑧~⑪）

| 用例 | 步骤 | 预期 |
|---|---|---|
| T3-1 数据拉取 | 查看 `fetch-data` step 日志 | 显示 `数据更新: v旧 → v新` |
| T3-2 图片补齐 | 查看 `fetch-images` step 日志 | `下载 N, 跳过 M`；N=0 或少量（增量） |
| T3-3 构建产物 | 确认 `vite build` 成功 | dist/ 生成 |
| T3-4 本地回退 | 断网/`GITHUB_TOKEN` 无效时本地跑 `npm run build` | 构建成功，数据保持本地版本（日志显示回退警告） |

**前置配置**（一次性）：
- HBT 仓库 Secrets 添加 `CLOUDFLARE_API_TOKEN`、`CLOUDFLARE_ACCOUNT_ID`（与 BattlegroundDB 用的同一 token 即可，需有 Pages:Edit 权限）

---

## 第四层：线上验证（⑫）

| 用例 | 步骤 | 预期 |
|---|---|---|
| T4-1 版本号更新 | 打开 hbt-cards.pages.dev，看牌匾下方版本号 | 显示新版本号 |
| T4-2 新卡可见 | 搜索新版本新增的卡 | 出现且能筛出 |
| T4-3 金色渲染 | 点击新卡，详情弹窗金色图 | 正常显示（无则隐藏金色位）|
| T4-4 图片加载 | DevTools Network 过滤 img | 全来自本地 `/img/cards/`，无外域请求 |

---

## 端到端演练（全链路一次性验证）

> 建议选在一个真实补丁更新日（hsbg.cards 有新版本）执行。

1. **手动触发** BattlegroundDB build.yml（不用等 cron）
2. 观察 Actions 全绿 → Release 创建（含 cards.json）
3. 观察 HBT 仓库 Actions：bgdb-update workflow 自动启动
4. 等构建部署完成（约 2~4 分钟）
5. 打开 hbt-cards.pages.dev 核对 T4-1~T4-4
6. **通过标准**：四个层级全部符合预期 = 链路正常

**干跑替代**（无真实补丁时）：在 build.yml 手动触发勾选 `force=true`——强制重发同版本 Release，同样会触发全链路。

---

## 回滚方案

| 故障 | 处理 |
|---|---|
| 网页版数据异常 | HBT 仓库手动触发 bgdb-update 不行（仍拉坏数据）→ 在 HBT 仓库执行 `git push` 到 feature/card-browser 触发 CF git 构建（fetch-data 失败会用**本地已提交**的 cards.json → 即上一个好版本）；或临时注释 fetch-data 步骤 |
| 网页版部署失败 | CF Dashboard → Deployments → 选择上一次成功部署 → **Rollback** |
| Worker/KV 故障（桌面版受影响） | BattlegroundDB 上次成功的 Release 里重新下载 DLL 手动放回 |

---

## 未覆盖/已知边界

- `repository_dispatch` 依赖 PAT 有效性与目标仓库存在；PAT 过期需手动更新
- GitHub API 未认证限流 60 次/时——fetch-data 每次构建消耗 1 次，日常足够；但若一个小时内多次构建可能触发限流（已自动回退本地）
- 网页端**无搜索框**（当前用筛选）——不在本链路测试范围
