# bg_tool 重构计划

> 分支：`refactor/bg-tool`
> 前一个版本 tag：`v0.6.0-pre-refactor`
> 基于 `claw_version`（v0.6.0）

---

## 为什么重构

| 问题 | 数据 |
|------|------|
| `MainForm.cs` 过胖 | 1264 行，同时管 UI + PID + 文件监控 + API 编排 + 重试定时器 |
| 空 `catch {}` | 15 个，吞掉异常无法调试 |
| Parser 不是纯函数 | 内部可变状态与 MainForm 耦合，靠魔术字符串通信 |
| HearthMirror 耦合深 | 调用散布在 3 个文件里，没有集中接口 |
| 构筑模式合入准备 | 当前 Parser 只认 BG，双模式需要新架构 |

---

## 架构（重构目标）

```
┌───────────────┐     ┌─────────────────┐     ┌──────────────┐
│   LogWatcher  │────▶│    MainForm     │────▶│ LeagueClient │
│  (文件监控)    │     │  (UI 编排)       │     │  (联赛逻辑)   │
│               │     │                 │     │              │
│  事件: OnLines │     │ 订阅 LogWatcher │     │ GameStart →  │
│               │     │ parser.Process  │     │   check-league│
│               │     │ LeagueClient.*  │     │ GameEnd   →  │
│               │     │ UpdateUI()      │     │   placement  │
└───────────────┘     └─────────────────┘     └──────────────┘
                            │
                     ┌──────┴──────┐
                     │   Parser    │
                     │  (纯函数)    │
                     │ input: line │
                     │ output:     │
                     │  GameEvent  │
                     └─────────────┘
```

### 核心原则

1. **LogWatcher**：不知道 Parser、Game、API、UI。只管读文件。
2. **Parser**：纯函数，无副作用。输入一行 + GameState → 输出 GameEvent。
3. **LeagueClient**：不知道 WinForms。只管编排 API 调用。
4. **MainForm**：只管 UI + 事件分发。

---

## 阶段

### 阶段 0：准备 ✅（已完成）

- [x] 从 `claw_version` 开 `refactor/bg-tool` 分支
- [x] 标记当前版本：`git tag v0.6.0-pre-refactor`
- [x] 本文档

### 阶段 1：提取 LogWatcher（1 天）

**拆分路径**：`MainForm.LogMonitorLoop()` → `LogWatcher` 类。

```
LogWatcher
  .ctor(string logPath)
  .Start()                   → 启动后台线程
  .Stop()                    → 停止线程
  event EventHandler<LinesReadEventArgs> OnLinesRead
    LinesReadEventArgs { Lines: string[], Position: long }

处理范围：
  - Power.log 文件 follow（每 100ms 读新行）
  - 文件长度变化 / 截断检测
  - FileNotFoundException → 搜索新日志（调 LogPathFinder）
  - PID 进程检测（仍留在 MainForm，因为涉及状态重置）
  - 文件行前缀过滤（跳过不含 GameState./PowerTaskList 的行）

保留在 MainForm：
  - PID 进程检测 + 重启逻辑（涉及 _phase 状态机）
  - 扫描旧日志（TryScanAndSwitch）
  - 所有 UI 代码
  - 所有 API 编排
```

**可验证**：启动后日志读取行为完全一致。

### 阶段 2：Parser 纯函数化（1 天）

**拆分路径**：Parser 去掉内部可变状态，输出强类型事件对象。

**当前**：
```csharp
// Parser 有内部状态
string? ProcessLine(string line)
// MainForm 用 switch(evt) 处理魔术字符串
```

**改后**：
```csharp
// GameState 是纯数据类，由外部持有
class GameState {
    bool InCreateBlock;
    bool CreateHasTurn;
    Game CurrentGame;
    Game PendingNewGame;
    bool LoFetched;
    bool ReachedStep13;
    bool ConcedePending;
    string ConcedeTag;
    Dictionary<string, int> EntityNameToPlayerId;
}

// GameEvent 是类型联合
abstract class GameEvent {}
class GameStartEvent : GameEvent { GameType, GameSeed, AccountIdLo, Guid }
class CheckLeagueEvent : GameEvent { LobbyPlayers }
class GameEndEvent : GameEvent { Placement, PlayState }
// 等

// Parser 纯函数
static GameEvent? ProcessLine(string line, GameState state)
```

**注意**：`HearthMirrorClient.FetchLobbyPlayers()` 目前从 Parser 内部调用。纯函数化后，Parser 只负责"检测到 STEP 13 → 输出 CheckLeagueEvent"，MainForm/LeagueClient 负责调 fetch。

**可验证**：用同一段 Power.log 逐行跑，新旧 Parser 输出等价。

### 阶段 3：提取 LeagueClient（1.5 天）

**拆分路径**：把事件处理中的 API 编排逻辑提取到 LeagueClient。

```
LeagueClient
  .ctor(ApiClient, GameStore, Config)
  .OnGameStart(GameStartEvent)
  .OnCheckLeague(CheckLeagueEvent)
  .OnGameEnd(GameEndEvent)
  .OnPidReset()
  .OnConfigChanged()
  .State { IsLeagueGame, GameUuid, VerificationCode, … }
```

**职责**：
- 调用 check-league API（包括重试定时器）
- 调用 update-placement API
- 持久化 GameStore
- 管理 _gameGeneration 代际
- 管理验证码

**MainForm 保留**：
- `UpdateUI()` 从 `LeagueClient.State` 读取数据
- PID 变化时重置状态
- 扫描旧日志

**可验证**：同样的事件触发同样的 API 请求。

### 阶段 4：HearthMirror 接口化（搁置）

> 暂时不做。需要服务端配合。见 DEV_NOTES.md。

### 阶段 5：错误处理扫尾（0.5 天）

逐个审查所有 `catch`：

| 模式 | 处理 |
|------|------|
| `catch { }` | 至少 `catch (Exception ex) { Console.WriteLine($"[E] {ex.Message}"); }` |
| `catch (Exception ex) { Log(ex); }` | 保留 |
| Parser 内的 catch | 改为记录违规行号和内容 |

### 阶段 6：构筑模式合入（1 天）

把 `std_tool` 的构筑 Parser 逻辑合入 bg_tool：

- Parser 增加 `GameType` 分支：BG 走现有路径，构筑走新路径
- `GameEndEvent` 统一接口：BG 用 `Placement`，构筑用 `PlayState`
- `LeagueClient.OnGameEnd()` 根据 `mode` 分发
- `check-league` payload 根据 playercount 2/8 调整
- `update-placement` 支持双方同时上报

---

## 验证策略

| 阶段 | 验证方式 |
|------|---------|
| 1 | 启动 bg_tool，日志读取正常 |
| 2 | 单元测试跑旧日志，新旧 Parser 输出一致 |
| 3 | 同一局天梯，API 请求一致 |
| 5 | 故意触发异常，确认有日志 |
| 6 | 标准对局 + bg_tool 同时启动，收到 check-league |
