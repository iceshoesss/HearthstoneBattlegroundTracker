# 报名参赛功能 Code Review

> 范围：`MatchSessionClient.cs`、`ApiClient` 队列接口、`MainWindow` 报名/确认 UI
> 初版：2026-06（SSE 版实现）
> **复核：2026-11-10（`feature/league-tool-basic` HEAD）**
> 状态：**P0 已全部修复，P1/P2 已跟进**；传输层已由 SSE 改为 WebSocket
> 编译：Release `0 警告 0 错误`

## 总评

整体结构清晰，与 `MATCHING_DESIGN.md` 的「一场一连 + ticket」模型对齐：

- `MatchSessionClient` — **WebSocket** 事件流（初版是 SSE，现已替换；见文末「迁移说明」）
- `ApiClient` — `queue/join` / `queue/leave` / `confirm-placement`，请求-应答优先走 WS，失败退回 HTTP
- `MainWindow` — 报名、排队、成组、名单、确认名次 UI 状态

主路径可跑通，下列历史问题已逐条关闭。

---

## P0 — 必须修（均已修复）

### 1. ~~`ResolveBattleTag` 缓存垃圾 ID，三元分支写错~~ ✅ 已修复

**原问题**：读 UI 文本 `PlayerName.Text`、三元两分支相同、首次点击把 `等待游戏启动...` 永久缓存。

**现状**（`MainWindow.ResolveBattleTag`）：改为从 `GameMonitorService.PlayerName`（即 `_localPlayerBattleTag`，`Name#Number`）取权威值，不再缓存。

```csharp
private string ResolveBattleTag()
{
    // 权威来源：GameMonitorService.PlayerName（Name#Number），不要用 UI 文本
    var tag = _monitor != null ? _monitor.PlayerName : null;
    return string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();
}
```

### 2. ~~SSE 断开后本地状态不同步~~ ✅ 已修复

**原问题**：只写日志，`_matchTicket` 与主按钮状态不同步。

**现状**（`MainWindow.OnMatchStreamClosed`）：断线自动重连最多 3 次（计数 `_sseReconnects`，报名成功时清零）；仍失败则 `LeaveQueueAsync()` 清空 ticket 并回到「未报名」。WS 连接成功时立即补报一次 `in_room`，避免重连后状态漂移。

> 命名遗留：方法名仍是 `OnMatchStreamClosed` / `MarkSseConnected` 等，`SseDot` 控件名也未改。语义已是 WS，改名可留待后续清理，不影响行为。

### 3. ~~事件用 `Contains` 扫 JSON，状态机易误判~~ ✅ 已修复

**原问题**：`json.Contains("\"grouped\"")` 之类全文子串匹配。

**现状**（`MainWindow.HandleMatchEvent`）：先 `TryParseStrField(json, "type")` / `TryParseStrField(json, "state")` 精确取字段，再按 `type` 分支（`state` / `queue_count` / `roster` / `table_update` / `confirm_placement`）。数组字段用 `TryParseNamedArray` / `ParseBoolArray`。

---

## P1 — 建议修（已跟进）

### 4. ~~主按钮职责过多，await 期间可连点~~ ✅ 已修复

`BtnSidePrimary_Click` / `BtnSideSecondary_Click` 统一在入口 `IsEnabled=false`，`finally` 恢复；`JoinQueueAsync` 内部还有一层保护。确认名次阶段主按钮「确定」、副按钮「有异议」，职责已分离。

### 5. ~~无读超时，静默断线永久挂起~~ ✅ 已修复（随传输层更换）

WebSocket 版设置 `KeepAliveInterval = 30s`，由 `ClientWebSocket` 做 ping/pong 保活；服务端静默断开或保活超时会抛异常进入 `RunAsync` 的重连循环（退避 2→4→6…，上限 30s，连续失败 5 次后触发 `OnClosed`）。初版 SSE 的 `ReadLineAsync` 永久挂起问题不再存在。

### 6. ticket 放在 URL query ⚠️ 部分处理

`BuildWsUri()` 仍用 `?ticket=…`（兼容现有服务端），但连接时**同时**发送 `X-Match-Ticket` 头：

```csharp
try { ws.Options.SetRequestHeader("X-Match-Ticket", _ticket); } catch { }
```

服务端改为读 Header 后即可下线 query 形式，避免进反向代理 access log。

### 7. ~~事件回调 `Dispatcher.Invoke` 改为 `BeginInvoke`~~ ✅ 已修复

报名/事件流回调全部 `BeginInvoke`；`GameMonitorService` 的 `OnPhaseChanged` / `OnSceneChanged` / `OnVerifyCodeChanged` / `OnLogMessage` / `OnPlayerNameChanged` / `OnMmrChanged` 也一并改为 `BeginInvoke`（后台线程只投递、不等待 UI 线程，避免关窗时与 `Closing` 互锁）。

### 8. ~~`Stop()` 不等待循环退出~~ ✅ 已修复

`Stop()` 取消 CTS 并 `loop.Wait(500)`；同时有 `_generation` 代际号，`RunAsync` 每次读写前后校验，过期连接的收发结果直接丢弃（与 `LeagueClient._gameGeneration` 同一模式）。

---

## P2 — 小问题（已处理）

| # | 原问题 | 现状 |
|---|--------|------|
| 9 | `BtnSidePrimary` 用 `FontWeight="Bold"`，中文小字号发虚 | ✅ XAML 已去掉该按钮的加粗 |
| 10 | `TryParseIntField` 跳过 `n/u/l` 兼容 null，过于隐晦 | ✅ 只跳空白，无数字返回 -1 |
| 11 | `ticket.Substring(0, 8)` 越界 | ✅ 已用 `Math.Min(8, ticket.Length)` |
| 12 | 缺 `ready` 分支 | ✅ 已实现（`state == "ready"` → 「准备就绪」） |
| 13 | 每条事件 `AppendLog` 刷屏 | ✅ 原始事件 dump 降到 `#if DEBUG`；正式包只保留状态分支的日志 |

---

## 仍然存在的风险 / 待办

1. **协议文档缺失**：`MATCHING_DESIGN.md` 在服务端仓库（`LeagueWeb/docs/`），插件侧只有本文件与 `API.md`；WS 事件字段（`roster` 的 `names/tags/inRooms`）建议同步到 `API.md`。
2. **`Sse*` 命名遗留**：`SseDot`、`MarkSseConnected/Disconnected`、`_sseReconnects`、`OnMatchStreamClosed` 均为 SSE 时期命名，实际已是 WS。
3. **无自动化测试**：报名/成组/确认状态机只能靠 mock 服务器手工回归，见 `TEST_PLAN.md`。

---

## 迁移说明（2026-10 改动）

| 项 | 初版（SSE） | 现在（WebSocket） |
|---|---|---|
| 端点 | `GET /api/plugin/match/events?ticket=` | `GET /api/plugin/match/ws?ticket=`（另发 `X-Match-Ticket` 头） |
| 实现 | `HttpClient` + `ReadLineAsync` | `ClientWebSocket` + `ReceiveAsync` |
| 请求-应答 | 只能走 HTTP | `RequestAsync(type, payload)` 注入 `reqId`，等 `*_result`；超时退回 HTTP |
| 保活 | 无 | `KeepAliveInterval = 30s`，ping/pong 过滤 |
| 断线 | 永久挂起 | 退避重连（上限 30s，5 次后 `OnClosed`）+ UI 侧再重连 3 次 |

## 关联文件

| 文件 | 说明 |
|------|------|
| `Services/MatchSessionClient.cs` | WebSocket 一场一连 + 请求-应答 |
| `Services/ApiClient.cs` | queue/join、queue/leave、confirm-placement（WS 优先，HTTP 兜底） |
| `Windows/MainWindow.xaml` | 侧栏匹配面板 |
| `Windows/MainWindow.xaml.cs` | 报名状态机 + 事件处理 + 进房轮询 |
| `LeagueWeb/docs/MATCHING_DESIGN.md` | 服务端匹配设计（权威，独立仓库） |
