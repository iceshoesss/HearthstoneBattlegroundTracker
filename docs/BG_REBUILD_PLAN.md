# LeagueTool BG 全功能复刻计划

> 将 LeagueTool 从联赛工具升级为完整 HDT 酒馆战棋功能复刻，使用 Blazor UI。

**目标**: 对齐 HDT 的 BG 模式功能，先实现再优化。
**框架**: .NET 8, x86, WinForms + Blazor Hybrid
**日期**: 2026-05-28

---

## 一、技术决策

### 1.1 框架选择

| 决策 | 选择 | 原因 |
|------|------|------|
| 目标框架 | .NET 8 | Blazor Hybrid 需要 .NET 6+ |
| 平台 | **x86** (强制) | ScryDotNet/BobsBuddy/HearthMirror 全部是 x86 |
| UI 框架 | WinForms + BlazorWebView | 兼容 x86，原生 Blazor 支持 |
| CSS | Tailwind CSS | 快速构建现代化深色主题 |

**x86 限制分析**:

```
炉石 (32-bit Unity IL2CPP)
    ↑ 读取内存
ScryDotNet (untapped-scry-dotnet.dll) → x86 原生库
    ↑ P/Invoke
HearthMirror.dll → x86 managed
    ↑ 引用
LeagueTool.exe → 必须 x86
```

所有关键 DLL 实测均为 x86:
- `untapped-scry-dotnet.dll` — x86 (原生内存读取)
- `BobsBuddy.dll` — x86 (战斗模拟)
- `BobsBuddy.Common.dll` — x86
- `HearthMirror.dll` — x86
- `BattlegroundDB.dll` — x86

### 1.2 架构总览

```
LeagueTool.exe (.NET 8, x86, WinForms)
│
├── MainForm.cs                         # WinForms 宿主 + BlazorWebView
│
├── Services/                           # 后端服务层
│   ├── GameSessionManager.cs           # 当前对局状态管理 (状态机)
│   ├── PowerLogWatcher.cs              # Power.log 实时监控 (复用现有)
│   ├── HearthMirrorService.cs          # HM 读取封装
│   ├── BobsBuddyService.cs             # 战斗模拟封装
│   ├── OpponentTrackerService.cs       # 对手阵容追踪 + 持久化
│   ├── CardDatabaseService.cs          # BattlegroundDB 查询封装
│   └── ServerSyncService.cs            # 数据上传 (联赛 + BG 数据)
│
├── Models/                             # 数据模型
│   ├── Game.cs                         # 复用现有
│   ├── GameEvent.cs                    # 复用现有
│   ├── GameState.cs                    # 复用现有
│   ├── BoardSnapshot.cs                # 场面快照
│   ├── OpponentRecord.cs               # 对手历史记录
│   └── ServerUpload.cs                 # 上传数据模型
│
├── Components/                         # Blazor Razor 组件
│   ├── App.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── Sidebar.razor
│   ├── Pages/
│   │   ├── Dashboard.razor             # 首页: 对局概览
│   │   ├── CombatSim.razor             # 战斗模拟面板
│   │   ├── OpponentTracker.razor       # 对手阵容追踪
│   │   ├── CardBrowser.razor           # 卡牌数据库查询
│   │   └── Settings.razor              # 设置
│   └── Shared/
│       ├── MinionCard.razor            # 随从卡片组件
│       ├── HeroPortrait.razor          # 英雄头像
│       └── WinRateGauge.razor          # 胜率仪表盘
│
├── wwwroot/                            # Blazor 静态资源
│   ├── index.html
│   ├── css/app.css
│   ├── js/interop.js                   # C# ↔ Blazor JS 互操作
│   └── images/                         # 卡牌图片
│
└── Lib/                                # 外部依赖
    ├── BobsBuddy.dll                   # 从 HDT Release 复制
    ├── BobsBuddy.Common.dll
    ├── BattlegroundDB.dll              # 现有
    ├── untapped-scry-dotnet.dll        # ScryDotNet 原生库
    └── HearthMirror/                   # 反编译 HM (项目引用)
```

---

## 二、HearthMirror 升级方案

### 2.1 现状

反编译版位于 `D:\coding\HearthMirror_Decompiled`，已编译为 net9.0。

核心机制:
- `ScryDotNet` (原生 DLL) → 连接炉石进程 → 读取 Unity 堆内存
- `Reflection.cs` (3846 行) → 动态遍历对象图
- `IReflection` 接口 → 160+ 个读取方法

### 2.2 升级步骤

1. **Fork 反编译版** 到 `LeagueTool/libs/HearthMirror/`
2. **改 TargetFramework** 为 `net8.0`
3. **作为项目引用** 加入 LeagueTool
4. **保留依赖**: `ScryDotNet.dll` (原生) + `Newtonsoft.Json` (NuGet)

### 2.3 LeagueTool 调用改造

```csharp
// 现在 (net472, 通过 HDT 的 HM DLL)
var tag = HearthMirrorClient.FetchBattleTag();
var players = HearthMirrorClient.FetchLobbyPlayers(playerTag);

// 改后 (net8, 直接引用反编译 HM)
var mirror = new Reflection();
var tag = mirror.GetBattleTag();              // BattleTag 对象
var accountId = mirror.GetAccountId();        // AccountId (hi, lo)
var lobby = mirror.GetBattlegroundsLobbyInfo(); // 大厅信息
var board = mirror.GetOpponentBoardState();   // 对手场面
var match = mirror.GetMatchInfo();            // 比赛信息
```

### 2.4 关键 IReflection API (BG 相关)

| 方法 | 返回类型 | 用途 |
|------|----------|------|
| `GetBattleTag()` | `BattleTag` | 玩家 BattleTag |
| `GetAccountId()` | `AccountId` | 玩家 AccountId (Lo) |
| `GetMatchInfo()` | `MatchInfo` | 比赛信息 (对手、模式) |
| `GetBattlegroundsLobbyInfo()` | `BattlegroundsLobbyInfo` | 大厅 8 人信息 |
| `GetBattlegroundsHeroOptions()` | `List<NameCardId>` | 英雄选择选项 |
| `GetOpponentBoardState()` | `OpponentBoardState` | 对手场面状态 |
| `GetBattlegroundRatingInfo()` | `BattlegroundRatingInfo` | BG 段位信息 |
| `GetAvailableBattlegroundsRaces()` | `List<int>` | 当前池种族 |
| `GetBattlegroundsTeammateBoardState()` | `BattlegroundsTeammateBoardState` | Duos 队友场面 |
| `GetSelectedBattlegroundsGameMode()` | `SelectedBattlegroundsGameMode` | BG 游戏模式 |

---

## 三、BobsBuddy 集成方案

### 3.1 API 概览

BobsBuddy 是纯 .NET 库 (x86)，核心流程:

```csharp
using BobsBuddy.Simulation;

var simulator = new Simulator();
var input = new Input();

// 构建玩家状态
input.Player.Health = 40;
input.Player.Tier = 5;
input.Player.Side.Add(minion1);  // 场上随从
input.Player.Side.Add(minion2);
input.Player.AddHeroPower(cardId, friendly, activated, data, data2, data3, null, entityId);

// 构建对手状态
input.Opponent.Health = 35;
input.Opponent.Tier = 4;
input.Opponent.Side.Add(opponentMinion1);

// 设置回合
input.SetTurn(10);

// 运行模拟
var output = await new SimulationRunner()
    .SimulateMultiThreaded(input, 10000, threadCount, maxTimeMs);
```

### 3.2 Output 结构

```csharp
public class Output {
    public float winRate;           // 胜率 (0-1)
    public float tieRate;           // 平局率
    public float lossRate;          // 败率
    public float theirDeathRate;    // 对手死亡率
    public float myDeathRate;       // 我方死亡率
    public List<int> damageResults; // 各模拟的伤害值
    public int simulationCount;     // 实际完成的模拟次数
    public ExitConditions myExitCondition; // 退出原因
}
```

### 3.3 Minion 构建

```csharp
// 从 HM Entity 构建 BobsBuddy Minion
Minion CreateMinion(Simulator sim, Entity entity) {
    var cardId = entity.Info.LatestCardId;
    var minion = sim.MinionFactory.CreateFromCardId(cardId, friendly);
    
    minion.baseAttack = entity.GetTag(GameTag.ATK);
    minion.baseHealth = entity.GetTag(GameTag.HEALTH) - entity.GetTag(GameTag.DAMAGE);
    minion.taunt = entity.HasTag(GameTag.TAUNT);
    minion.div = entity.HasTag(GameTag.DIVINE_SHIELD) ? 1 : 0;
    minion.poisonous = entity.HasTag(GameTag.POISONOUS);
    minion.venomous = entity.HasTag(GameTag.VENOMOUS);
    minion.windfury = entity.HasTag(GameTag.WINDFURY);
    minion.reborn = entity.HasTag(GameTag.REBORN);
    minion.golden = entity.HasTag(GameTag.PREMIUM);
    minion.tier = entity.GetTag(GameTag.TECH_LEVEL);
    minion.PrimaryRace = (Race)entity.GetTag(GameTag.CARDRACE);
    minion.ScriptDataNum1 = entity.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_1);
    minion.ScriptDataNum2 = entity.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_2);
    
    return minion;
}
```

### 3.4 模拟参数

```csharp
const int Iterations = 10_000;
const int DefaultMaxTime = 1_500;        // 默认 1.5 秒
const int ComplexBoardMaxTime = 3_000;   // 复杂场面 3 秒
const int LeapfroggerMaxTime = 5_000;    // 跳蛙 5 秒
int ThreadCount = Environment.ProcessorCount / 2;
```

---

## 四、对手追踪系统

### 4.1 数据模型

```csharp
public class OpponentRecord
{
    public ulong AccountIdLo { get; set; }
    public string BattleTag { get; set; }
    public string DisplayName { get; set; }
    public string HeroCardId { get; set; }
    public List<EncounterRecord> Encounters { get; set; } = new();
}

public class EncounterRecord
{
    public DateTime Timestamp { get; set; }
    public int Turn { get; set; }
    public int PlayerPlacement { get; set; }
    public int OpponentPlacement { get; set; }
    public string PlayerHero { get; set; }
    public string OpponentHero { get; set; }
    public BoardSnapshot OpponentBoard { get; set; }  // 最后看到的阵容
    public string GameUuid { get; set; }
}

public class BoardSnapshot
{
    public int TavernTier { get; set; }
    public int HeroHealth { get; set; }
    public int HeroArmor { get; set; }
    public string HeroPowerCardId { get; set; }
    public bool HeroPowerActivated { get; set; }
    public List<MinionSnapshot> Minions { get; set; } = new();
    public List<TrinketSnapshot> Trinkets { get; set; } = new();
}

public class MinionSnapshot
{
    public string CardId { get; set; }
    public string Name { get; set; }
    public int Attack { get; set; }
    public int Health { get; set; }
    public bool Golden { get; set; }
    public bool Taunt { get; set; }
    public bool DivineShield { get; set; }
    public bool Poisonous { get; set; }
    public bool Reborn { get; set; }
    public bool Windfury { get; set; }
    public string Race { get; set; }
    public int Tier { get; set; }
}

public class TrinketSnapshot
{
    public string CardId { get; set; }
    public string Name { get; set; }
    public bool IsGreater { get; set; }
}
```

### 4.2 持久化

- 本地存储: `opponent_history.json` (JSONL 格式)
- 每个对手最多保留 20 条遭遇记录
- 启动时加载，运行时追加，退出时保存

### 4.3 数据来源

| 数据 | 来源 | 时机 |
|------|------|------|
| 对手 BattleTag | `IReflection.GetMatchInfo()` | 对局开始 |
| 对手 Hero | Power.log `FULL_ENTITY` | 英雄选择后 |
| 对手场面 | `IReflection.GetOpponentBoardState()` | 战斗阶段 |
| 对手排名 | Power.log `TAG_CHANGE Entity=... tag=PLAYER_LEADERBOARD_PLACE` | 结算时 |

---

## 五、服务端上传数据模型 (预留)

> 不修改 LeagueWeb 代码，仅定义数据结构，将来由用户确认后实现。

### 5.1 现有联赛数据 (已实现)

```json
{
  "gameUuid": "...",
  "playerTag": "玩家名#1234",
  "accountIdLo": 1708070391,
  "placement": 3,
  "mode": "BG",
  "region": "CN"
}
```

### 5.2 新增 BG 数据

```csharp
// POST /api/v1/upload-bg-data (新增端点，预留)
public class BgGameDataUpload
{
    // === 现有联赛字段 (保持兼容) ===
    public string GameUuid { get; set; }
    public string PlayerTag { get; set; }
    public ulong AccountIdLo { get; set; }
    public int Placement { get; set; }
    public string Mode { get; set; }
    public string Region { get; set; }
    public List<OtherPlacement> OtherPlacements { get; set; }
    
    // === 新增 BG 数据 ===
    public string HeroCardId { get; set; }
    public string HeroName { get; set; }
    public int TavernTier { get; set; }
    public int TurnCount { get; set; }
    public int FinalHealth { get; set; }
    
    // 最终场面
    public List<MinionSnapshot> FinalBoard { get; set; }
    
    // 遭遇记录 (每回合对手场面)
    public List<EncounterData> Encounters { get; set; }
    
    // 战斗模拟统计 (可选)
    public SimulationSummary? SimulationSummary { get; set; }
}

public class EncounterData
{
    public int Turn { get; set; }
    public ulong OpponentAccountIdLo { get; set; }
    public string OpponentHero { get; set; }
    public int OpponentHealth { get; set; }
    public int OpponentTavernTier { get; set; }
    public List<MinionSnapshot> OpponentBoard { get; set; }
}

public class SimulationSummary
{
    public int TotalSimulations { get; set; }
    public float AverageWinRate { get; set; }
    public float AverageLossRate { get; set; }
    public float AverageDamageTaken { get; set; }
}
```

---

## 六、HDT 功能对照表

| HDT 功能 | 实现方案 | 优先级 | Phase |
|----------|----------|--------|-------|
| **BobsBuddy 战斗模拟** | BobsBuddyService + CombatSim.razor | P0 | 2 |
| **对手阵容追踪** | OpponentTrackerService + OpponentTracker.razor | P0 | 2-3 |
| **卡牌数据库查询** | CardDatabaseService + CardBrowser.razor | P1 | 3 |
| **实时场面追踪** | HM GetOpponentBoardState + Power.log | P0 | 2 |
| **酒馆等级/铸币显示** | Power.log 解析 + Dashboard | P1 | 2 |
| **伤害上限显示** | Power.log `DAMAGE_CAP` tag | P2 | 3 |
| **英雄选择辅助** | HM GetBattlegroundsHeroOptions + 卡牌数据 | P1 | 3 |
| **战斗倒计时** | Power.log 时间戳差值 | P2 | 3 |
| **Duos 模式支持** | HM GetBattlegroundsTeammateBoardState | P3 | 4 |
| **统计数据面板** | 本地 JSON 聚合 | P1 | 3 |

### 6.1 HDT 核心流程 (参考)

```
PowerHandler (日志解析)
    │
    ├── TurnStart → BobsBuddyInvoker.StartShoppingAsync()
    │                 → 显示上回合模拟结果
    │
    ├── PlayerEndOfTurn → BobsBuddyInvoker.StartCombat()
    │                      → SnapshotBoardState(turn)
    │                      → new SimulationRunner().SimulateMultiThreaded()
    │                      → 显示胜率/伤害
    │
    ├── TagChange (GAME_ENTITY_ATTACK) → 更新对手场面
    │
    └── GameEnd → BobsBuddyInvoker.StartShoppingAsync(isGameOver: true)
                  → 显示最终结果
```

---

## 七、实施计划

### Wave 1: 基础架构 (预计 3 天)

| Task | 描述 | Category | 依赖 | 文件变更 |
|------|------|----------|------|----------|
| **T1** | 项目骨架搭建 | quick | 无 | 新建 .csproj, Program.cs, MainForm.cs |
| **T2** | Blazor 布局 + 导航 | visual-engineering | T1 | wwwroot/*, Components/Layout/* |
| **T3** | HearthMirror 升级 | deep | T1 | Services/HearthMirrorService.cs |
| **T4** | BobsBuddy 封装 | deep | T1 | Services/BobsBuddyService.cs |
| **T5** | CardDatabase 封装 | quick | T1 | Services/CardDatabaseService.cs |

**T1 项目骨架**:
```xml
<!-- LeagueTool.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PlatformTarget>x86</PlatformTarget>
    <RuntimeIdentifier>win-x86</RuntimeIdentifier>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.*" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.WindowsForms" Version="8.0.*" />
    <ProjectReference Include="libs/HearthMirror/HearthMirror.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="Lib\BobsBuddy.dll" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Lib\BobsBuddy.Common.dll" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Lib\untapped-scry-dotnet.dll" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Lib\BattlegroundDB.dll" CopyToOutputDirectory="PreserveNewest" />
    <EmbeddedResource Include="Data\bg_heroes.json" />
    <EmbeddedResource Include="Data\bg_minions.json" />
  </ItemGroup>
</Project>
```

### Wave 2: 核心功能 (预计 4 天)

| Task | 描述 | Category | 依赖 | 文件变更 |
|------|------|----------|------|----------|
| **T6** | GameSessionManager | unspecified-high | T3 | Services/GameSessionManager.cs |
| **T7** | OpponentTracker 服务 | unspecified-high | T3 | Services/OpponentTrackerService.cs |
| **T8** | ServerSync 服务 | unspecified-high | 无 | Services/ServerSyncService.cs |
| **T9** | Dashboard 页面 | visual-engineering | T2, T6 | Components/Pages/Dashboard.razor |
| **T10** | CombatSim 页面 | visual-engineering | T2, T4 | Components/Pages/CombatSim.razor |

**T6 GameSessionManager**:
- 状态机: Idle → PreLobby → Lobby → Active → PostGame
- 整合 PowerLogWatcher + HearthMirrorService
- 管理当前对局所有状态
- 通过事件通知 Blazor UI 更新

**T7 OpponentTrackerService**:
- 每次战斗记录对手场面
- 按 AccountIdLo 索引
- 持久化到 `opponent_history.json`
- 提供查询接口: GetLastEncounter(accountIdLo), GetRecentOpponents()

### Wave 3: 完整功能 (预计 3 天)

| Task | 描述 | Category | 依赖 | 文件变更 |
|------|------|----------|------|----------|
| **T11** | OpponentTracker 页面 | visual-engineering | T7, T2 | Components/Pages/OpponentTracker.razor |
| **T12** | CardBrowser 页面 | visual-engineering | T5, T2 | Components/Pages/CardBrowser.razor |
| **T13** | Settings 页面 | visual-engineering | T2 | Components/Pages/Settings.razor |
| **T14** | Shared 组件库 | visual-engineering | T2 | Components/Shared/* |

### Wave 4: 集成测试 (预计 2 天)

| Task | 描述 | Category | 依赖 |
|------|------|----------|------|
| **T15** | 端到端集成 | deep | 全部 |
| **T16** | 手动 QA | unspecified-high | T15 |

---

## 八、数据流

```
┌─────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  Power.log  │───▶│  PowerLogWatcher │───▶│ GameSessionManager│
└─────────────┘    └──────────────────┘    └────────┬────────┘
                                                    │
┌─────────────┐    ┌──────────────────┐             │
│   炉石进程   │───▶│  HearthMirror    │─────────────┤
│  (内存读取)  │    │  Service         │             │
└─────────────┘    └──────────────────┘             │
                                                    ▼
                                    ┌──────────────────────────┐
                                    │     EventBus / State      │
                                    │  (C# → Blazor 推送)       │
                                    └──────────┬───────────────┘
                                               │
                            ┌──────────────────┼──────────────────┐
                            ▼                  ▼                  ▼
                    ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
                    │  Dashboard   │  │  CombatSim   │  │  Opponent    │
                    │  (Blazor)    │  │  (Blazor)    │  │  Tracker     │
                    └──────────────┘  └──────────────┘  └──────────────┘
                                               │
                                               ▼
                                    ┌──────────────────────┐
                                    │  BobsBuddyService    │
                                    │  (战斗模拟)           │
                                    └──────────────────────┘
```

---

## 九、风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| BobsBuddy.dll 依赖链不兼容 .NET 8 | 中 | 高 | 提前验证; 备选: ILSpy 重编译 |
| ScryDotNet 原生库与 .NET 8 不兼容 | 低 | 高 | 已有 HDT 在新框架使用; 测试 P/Invoke |
| HearthMirror 反编译代码编译错误 | 中 | 中 | 已验证 net9.0 可编译; 降级 net8 小修 |
| Blazor 首次加载慢 | 中 | 低 | 预加载 + 本地虚拟主机映射 |
| 对手历史数据量过大 | 低 | 低 | 限制每对手 20 条记录 |
| WebView2 Runtime 缺失 | 低 | 低 | Win10/11 自带; 提示安装 |

---

## 十、验证策略

### 10.1 每 Task 验证

1. `dotnet build` 成功 (exit code 0)
2. LSP 诊断无 error
3. 功能验证:
   - HM: 能读取 BattleTag + LobbyPlayers
   - BobsBuddy: 能运行模拟并返回胜率
   - UI: Blazor 组件正确渲染

### 10.2 集成验证

```
启动 LeagueTool
  → 检测炉石进程
  → 读取 BattleTag (HM)
  → 检测 Power.log
  → 等待对局开始
  → 读取英雄信息
  → 战斗阶段: 运行 BobsBuddy 模拟 → 显示胜率
  → 记录对手场面
  → 结算: 显示排名
  → 查询对手历史: 显示上次遇到的阵容
```

---

## 附录 A: 现有代码复用清单

| 文件 | 复用方式 | 改动 |
|------|----------|------|
| `Parser/Parser.cs` | 直接复用 | 改 namespace |
| `Models/Game.cs` | 直接复用 | 改 namespace |
| `Models/GameEvent.cs` | 直接复用 | 改 namespace |
| `Models/GameState.cs` | 直接复用 | 改 namespace |
| `Services/HeroNameResolver.cs` | 直接复用 | 改 namespace |
| `Services/LogPathFinder.cs` | 直接复用 | 改 namespace |
| `Services/LogWatcher.cs` | 直接复用 | 改 namespace |
| `Services/HearthMirrorClient.cs` | 重写为 HM Service | 使用反编译 HM API |
| `Services/LeagueClient.cs` | 保留 | 适配新框架 |
| `Services/ApiClient.cs` | 保留 | 适配新框架 |
| `bg_heroes.json` | 嵌入资源 | 不变 |
| `bg_minions.json` | 嵌入资源 | 不变 |
| `BattlegroundDB.dll` | 直接引用 | 不变 |

## 附录 B: 外部依赖来源

| 依赖 | 来源 | 复制到 |
|------|------|--------|
| `BobsBuddy.dll` | `C:\Users\cc\Downloads\HDT-V2.4.2\HDT\` | `Lib/` |
| `BobsBuddy.Common.dll` | 同上 | `Lib/` |
| `untapped-scry-dotnet.dll` | 同上 | `Lib/` |
| `HearthMirror` 代码 | `D:\coding\HearthMirror_Decompiled\` | `libs/HearthMirror/` |
| `BattlegroundDB.dll` | `D:\coding\HDT_BGTracker\LeagueTool\` | `Lib/` |

## 附录 C: NuGet 包

| 包 | 版本 | 用途 |
|----|------|------|
| `Microsoft.Web.WebView2` | 1.0.* | WebView2 控件 |
| `Microsoft.AspNetCore.Components.WebView.WindowsForms` | 8.0.* | Blazor Hybrid |
| `Newtonsoft.Json` | 13.0.* | JSON 序列化 (HM 依赖) |
