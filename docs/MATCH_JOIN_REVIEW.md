# 报名参赛功能 Code Review

> 范围：`MatchSessionClient.cs`、`ApiClient` 队列接口、`MainWindow` 报名/确认 UI  
> 状态：待修复  
> 编译：通过（0 错误）

## 总评

整体结构清晰，与 `MATCHING_DESIGN.md` 的「一场一连 + ticket」模型对齐：

- `MatchSessionClient` — SSE 事件流
- `ApiClient` — `queue/join` / `queue/leave` / `confirm-placement`
- `MainWindow` — 报名、排队、成组、确认名次 UI 状态

主路径可跑通。下列问题按优先级排列，**P0 建议修完再联调服务端**。

---

## P0 — 必须修

### 1. `ResolveBattleTag` 缓存垃圾 ID，三元分支写错

**位置**：`Windows/MainWindow.xaml.cs` · `ResolveBattleTag()`

```csharp
var name = PlayerName.Text ?? "";
_battleTag = name.Contains("#") ? name : name;  // 两个分支相同
```

**问题**：

1. 三元表达式两个分支完全一样，逻辑无效。
2. 读取的是 **UI 文本** `PlayerName.Text`，不是服务端/内存权威字段。
3. 启动时 UI 文本为 `等待游戏启动...`，第一次点「参赛」会被永久缓存到 `_battleTag`。
4. 后续 `IsNullOrWhiteSpace(tag) || tag == "待接入"` 拦不住该值，会把垃圾 `battleTag` 提交到 `queue/join`。

**修复建议**：

```csharp
private string ResolveBattleTag()
{
    // GameMonitorService.PlayerName 即 _localPlayerBattleTag（Name#Number）
    var tag = _monitor?.PlayerName ?? "";
    return string.IsNullOrEmpty(tag) ? "" : tag;
}
```

不要用 UI 文本做数据源，也不要缓存校验失败前的值。

---

### 2. SSE 断开后本地状态不同步

**位置**：`Windows/MainWindow.xaml.cs` · `StartMatchSession()`

```csharp
OnClosed = () => Dispatcher.Invoke(() => AppendLog("匹配事件流已断开，可重新报名")),
```

**问题**：只写日志。`_matchTicket` 仍保留，主按钮仍是「退出排队」，但服务端可能已将玩家移出队列或会话已失效。

**修复建议**（二选一）：

- **A. 重连**：按设计文档「断线凭 ticket 续上」，自动重建 SSE；
- **B. 降级**：清空 `_matchTicket`、UI 回「未报名」，提示用户重新报名。

---

### 3. 事件用 `Contains` 扫 JSON，状态机易误判

**位置**：`Windows/MainWindow.xaml.cs` · `HandleMatchEvent()`

```csharp
json.Contains("\"type\":\"state\"")
json.Contains("\"grouped\"")
json.Contains("\"confirm\"")
```

**问题**：全文子串匹配，字段/玩家名里出现同名片段就会误切状态。`confirm` 目前不会命中 `confirm_placement`（后面是 `_` 而非 `"`），但极脆。

**修复建议**：做最小字段解析，按 `type` / `state` 精确匹配：

```csharp
var type = TryParseStrField(json, "type");
var state = TryParseStrField(json, "state");
```

---

## P1 — 建议修

### 4. 主按钮职责过多，await 期间可连点

**位置**：`BtnSidePrimary_Click` / `BtnSideSecondary_Click`

同一主按钮承担：报名 / 退出排队 / 名次无误 / 返回。`JoinQueueAsync` 有禁用按钮，`LeaveQueueAsync`、`ConfirmPlacementAsync` 没有，双击可能重复请求。

**建议**：

- 所有 async 点击统一入口禁用/恢复按钮；
- confirm 阶段主按钮只负责确认，「返回」拆开。

---

### 5. SSE 无读超时，静默断线会永久挂起

**位置**：`Services/MatchSessionClient.cs`

```csharp
http.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
var line = await reader.ReadLineAsync();
```

**问题**：中间设备静默断开时 `ReadLineAsync` 可能永远不返回。

**建议**：2–3 分钟无任何字节视为断线并触发 `OnClosed`；服务端 ping 可作保活信号。

---

### 6. ticket 放在 URL query

**位置**：`MatchSessionClient.RunAsync()`

```csharp
var url = _baseUrl + "/api/plugin/match/events?ticket=" + Uri.EscapeDataString(_ticket);
```

**问题**：易进反向代理 / 访问日志。

**建议**：优先改 Header（如 `X-Match-Ticket`）；若协议已定为 query，在服务端文档注明勿记录 access log。

---

### 7. 事件回调用 `Dispatcher.Invoke` 改为 `BeginInvoke`

**位置**：`StartMatchSession()` 的 `OnEventJson` / `OnClosed`

**问题**：窗口关闭时 `Invoke` 可能与关闭流程互卡。

**建议**：`Dispatcher.BeginInvoke(...)`。

---

### 8. `MatchSessionClient.Stop()` 不等待循环退出

**位置**：`Services/MatchSessionClient.cs`

`Start()` 先 `Stop()` 再开新连接时，旧 `RunAsync` 可能仍在读流，存在短暂并发。

**建议**：`Dispose`/`Stop` 里等待 `_loop` 短暂结束（如 500ms），或用 generation 丢弃过期事件（参考现有 `LeagueClient._gameGeneration` 模式）。

---

## P2 — 小问题

| # | 位置 | 问题 | 建议 |
|---|------|------|------|
| 9 | `BtnSidePrimary` XAML | 再次使用 `FontWeight="Bold"`，中文小字号易发虚（此前已反馈过） | 与「复制/刷新」一致，去掉加粗 |
| 10 | `TryParseIntField` | 跳过字符 `n/u/l` 来兼容 `null`，过于隐晦 | 只跳过空白；无数字则返回 -1 |
| 11 | `JoinQueueAsync` 日志 | `ticket.Substring(0, 8)`，ticket 不足 8 位会抛异常 | `Math.Min(8, ticket.Length)` |
| 12 | 事件处理 | 缺少设计中的 `READY` / ready 确认分支 | Phase 2 补全 |
| 13 | `HandleMatchEvent` | 每条事件 `AppendLog` 会刷屏 | 仅 `#if DEBUG` 或限流 |

---

## 做得好的地方

- 一场一连生命周期与设计文档一致，ticket 模型正确
- `ApiClient` 三个接口干净，复用 `PostAsync`
- UI 状态卡片分区清晰；「名次无误 / 有异议」交互合理
- 异步路径有 try/catch，避免静默崩溃

---

## 建议落地顺序

1. **P0-1 battleTag**（防止报名数据被污染）
2. **P0-2 断线状态 + P0-3 事件解析**（状态机正确性）
3. P1 / P2 按联调需要补齐

---

## 关联文件

| 文件 | 说明 |
|------|------|
| `Services/MatchSessionClient.cs` | 新增，SSE 一场一连 |
| `Services/ApiClient.cs` | 新增 queue/join、queue/leave、confirm-placement |
| `Windows/MainWindow.xaml` | 侧栏匹配面板 |
| `Windows/MainWindow.xaml.cs` | 报名状态机 + 事件处理 |
| `D:\coding\LeagueWeb\docs\MATCHING_DESIGN.md` | 服务端匹配设计（权威） |
