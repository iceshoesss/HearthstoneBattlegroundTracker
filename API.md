# API 文档

完整 API 文档已迁移至 **[LeagueWeb](https://github.com/iceshoesss/LeagueWeb)** 仓库。

→ [LeagueWeb/API.md](https://github.com/iceshoesss/LeagueWeb/blob/main/API.md)

---

## 插件相关端点速查

以下端点供 C# 插件调用（全部由 `Services/ApiClient.cs` 发出）：

| 端点 | 方法 | 时机 | 说明 |
|------|------|------|------|
| `/api/plugin/initialize-player` | POST | 启动 / 刷新验证码 | 初始化玩家信息 + 获取验证码，顺带上报 MMR |
| `/api/plugin/check-league` | POST | STEP 13（大厅就绪） | 检查是否联赛对局 |
| `/api/plugin/update-placement` | POST | 对局结束 | 上报名次（含 `disconnects` / `otherPlacements` / 兜底 `timeout`） |
| `/api/plugin/report-game-stats` | POST | 对局结束 | 上报 MMR 变动 + 阵容快照 |
| `/api/plugin/queue/join` | POST | 点「参赛」 | 报名匹配，返回 `ticket` + 初始状态 |
| `/api/plugin/queue/leave` | POST | 退出排队 / 关闭 HBT | 退出匹配队列 |
| `/api/plugin/confirm-placement` | POST | 名次确认面板 | 「确定」/「有异议」 |
| `/api/plugin/test/remap-los` | POST | 仅 Debug 联调 | Lo 重映射测试 |
| `/api/plugin/match/ws` | **WebSocket** | 报名成功后 | 一场一连事件流（`state` / `queue_count` / `roster` / `confirm_placement` / `*_result`） |

**请求头**

| Header | 值 | 说明 |
|--------|-----|------|
| `X-HDT-Plugin` | 程序集版本（如 `0.7.0`） | 服务端做版本兼容判断 |
| `Authorization` | `Bearer <pluginKey>` | Release 编译时内置，Debug 为空 |
| `X-Match-Ticket` | `queue/join` 返回的 ticket | 仅 WS 连接，同时也会带在 `?ticket=` |

**调用策略**：`check-league` / `update-placement` / `remap-los` 优先走 WS 请求-应答（注入 `reqId`，等 `<type>_result`），WS 不可用或超时（8–12s，按接口不同）自动退回上面的 HTTP 端点。

`upload-rating` 为旧版端点，当前代码已不再调用。

详细请求/响应格式见 [LeagueWeb/API.md](https://github.com/iceshoesss/LeagueWeb/blob/main/API.md#插件专用-api)。

## 数据库

- 数据库名：`hearthstone`
- 关键集合：`player_records`（玩家记录+验证码）、`league_matches`（联赛对局）
- 完整集合说明见 LeagueWeb API 文档
