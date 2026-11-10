# HBT - Hearthstone Battleground Tracker

炉石传说酒馆战棋联赛追踪插件，自动检测联赛对局、记录排名、上报 MMR 变动。

**配套联赛网站**：[LeagueWeb](https://github.com/iceshoesss/LeagueWeb)（已拆分为独立仓库）

## 功能

- **联赛对局检测**：自动识别联赛对局，上报排名
- **报名参赛**：插件内直接报名匹配（`queue/join`），排队进度 / 成组 / 对局中 / 名次确认全流程
- **同桌名单**：成组后显示 8 人名单，可逐条复制，已进房的名字标绿
- **名次确认**：「确定」或「有异议」，避免名次误记
- **MMR 追踪**：游戏开始时读取 MMR，游戏结束后读取 MMR 变动
- **阵容快照**：游戏结束时记录己方场面（随从属性 + 关键词）
- **战绩记录**：本地保存对局记录，UI 显示最近战绩和今日统计
- **断线重连**：自动识别并恢复对局状态，每局拔线次数随对局上报（`disconnects`）
- **非联赛对局**：支持非联赛对局的 MMR 变动记录

## 项目结构

```
HBT/                                   # 基础联赛工具
├── BattlegroundSpy/                   # 内存读取（UnitySpy）
│   ├── BattlegroundSpyReader.cs       # 核心：MMR、大厅、排名
│   └── Objects/                       # 数据模型
├── BattlegroundSpy.Test/              # BGSpy 测试程序
├── Parser/                            # Power.log 解析器
├── Services/                          # 核心服务
│   ├── GameMonitorService.cs          # 游戏状态机
│   ├── HearthMirrorService.cs         # BGSpy 包装层
│   ├── MatchSessionClient.cs          # 报名匹配 WebSocket（一场一连）
│   ├── LeagueClient.cs                # 联赛 API 客户端
│   └── ApiClient.cs                   # HTTP API 客户端
├── Models/                            # 数据模型
├── Windows/                           # UI（MainWindow）
├── Lib/BattlegroundDB.dll             # 卡牌数据库
├── mock_server.py                     # Python mock 服务器（调试用）
└── API.md                             # API 文档
```

## 编译

### 前置条件

- .NET SDK（含 .NET Framework 4.7.2 目标包）
- Windows（WPF）

### 步骤

```powershell
dotnet build -c Release
```

编译产物：`bin\Release\net472\HearthstoneBattlegroundTracker.exe`

### 运行测试

```powershell
dotnet run --project BattlegroundSpy.Test -c Release
```

## 配置

程序启动后自动检测炉石进程，玩家信息、MMR、大厅数据全部从游戏内存读取，**无需手动配置**。

### 服务端地址

| 构建 | 地址来源 |
|------|----------|
| **Release**（发给选手） | 编译时内置 `https://www.dazy.asia`，**不读任何外部配置文件**（防改包/防误配） |
| **Debug**（自己联调） | 支持外部配置，见下 |

Debug 的查找顺序（找到即停）：

1. 环境变量 `BGTRACKER_CONFIG` 指定的 json 文件
2. 从 exe 目录向上最多 5 层查找 `shared_config.json`（已 gitignore）
3. exe 同目录的 `config.json`

```json
{
  "apiBaseUrl": "http://localhost:5000",
  "testRemap": false
}
```

| 字段 | 说明 | 默认值（Debug） |
|------|------|-----------------|
| `apiBaseUrl` | 服务端 API 地址 | `http://localhost:5000` |
| `testRemap` | 仅测试用：check-league 前把 Lo 重映射为真实路人 | `false` |

`region` / `mode` 目前写死为 `CN` / `solo`（见 `LeagueClient`），不在配置文件里。

## API 端点

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/plugin/initialize-player` | POST | 初始化玩家信息，获取验证码，上报 MMR |
| `/api/plugin/check-league` | POST | 检查是否为联赛对局 |
| `/api/plugin/update-placement` | POST | 上报排名（仅联赛对局） |
| `/api/plugin/report-game-stats` | POST | 上报游戏统计（MMR + 阵容 + RatingChange） |
| `/api/plugin/queue/join` | POST | 报名匹配，返回 ticket |
| `/api/plugin/queue/leave` | POST | 退出排队 |
| `/api/plugin/confirm-placement` | POST | 名次确认 / 异议 |
| `/api/plugin/match/ws` | WebSocket | 一场一连事件流（排队人数、成组、同桌名单、名次确认） |

`check-league` / `update-placement` 在有 WS 连接时优先走 WS 请求-应答，失败退回 HTTP。详细字段说明见 [API.md](API.md)。

## 安全说明

插件的 `Authorization: Bearer` Key 是**编译进 exe 的共享凭证**，不是秘密：它存在于 git 历史和发布产物中，可被直接提取。因此：

- 服务端必须把它当低信任凭证 —— 只授予 `/api/plugin/*` 上报权限，并叠加 `accountIdLo` 身份校验 + 频控；
- 不要在这把 Key 之下挂任何读管理数据 / 改配置的接口；
- 轮换时服务端与插件同时换，旧 Key 保留一个版本过渡期；
- 需要真正保密的凭证时，改为服务端下发短期 token，不要继续往客户端里塞常量。

## 调试（mock 服务器）

```bash
python mock_server.py
```

mock 服务器监听 `localhost:5000`，打印所有请求数据，用于调试。

## 版本号

当前版本：`v0.2.0`

修改版本号的位置：

| 文件 | 字段 |
|------|------|
| `HearthstoneBattlegroundTracker.csproj` | `<Version>0.2.0</Version>` |
| `BattlegroundSpy/BattlegroundSpy.csproj` | `<Version>0.2.0</Version>` |

## 更新日志

### v0.2.0 (2026-05-31)

**BattlegroundSpy 新增功能：**
- MMR 读取（通过 ServiceLocator → NetCache → NetCacheBaconRatingInfo）
- RatingChange 读取（游戏结束后轮询 `m_gameEntity.RatingChangeData`）
- 己方/对手场面读取（通过 ZoneMgr → ZonePlay）
- TryGetField 安全字段访问（避免 UnitySpy 抛异常）
- GetCollectionSize 兼容 UnitySpy size() 和 .NET Array.Length

**HBT 功能：**
- MMR 显示：用户名下方显示当前 MMR
- MMR 上报：initialize-player 时一起上报 MMR
- RatingChange 上报：游戏结束后轮询分数变化，通过 report-game-stats 上报
- 阵容快照：游戏结束时记录己方场面（攻击/血量/科技等级/关键词）
- 非联赛对局支持：跳过 update-placement，保留 report-game-stats 和本地记录
- 结算 UI 修复：显示排名而非"结算中..."
- 最近战绩：统一显示 MMR 变动（+绿色/-红色/±0灰色）
- 今日统计：只统计 MMR 变动
- 扫描优化：上一局已结束时跳到日志末尾，避免误触发旧事件
- 关闭优化：移除 Application.Current.Shutdown，Environment.Exit 直接退出

**Bug 修复：**
- 修复炉石重启后 BGSpy 初始化过早（Mono 未就绪）：PID 变化后延迟 5 秒
- 修复炉石重启后玩家信息未重置：ResetToIdle 时清空 BattleTag/AccountId
- 修复 UI 英雄名显示错误：BGSpy 大厅数据始终覆盖 Parser 匹配结果
- 修复扫描完成后 check-league 被跳过：补发 HandleCheckLeague
- 修复非联赛对局 UI 卡在"联赛检查中"：isLeague=false 时设为 Active 阶段

### v0.1.0 (2026-05-26)

- 初始版本：WPF 主程序 + Power.log 解析 + 联赛对局检测
