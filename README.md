# HBT - Hearthstone Battleground Tracker

炉石传说酒馆战棋联赛追踪插件，自动检测联赛对局、记录排名、上报 MMR 变动。

**配套联赛网站**：[LeagueWeb](https://github.com/iceshoesss/LeagueWeb)（已拆分为独立仓库）

## 功能

- **联赛对局检测**：自动识别联赛对局，上报排名
- **MMR 追踪**：游戏开始时读取 MMR，游戏结束后读取 MMR 变动
- **阵容快照**：游戏结束时记录己方场面（随从属性 + 关键词）
- **战绩记录**：本地保存对局记录，UI 显示最近战绩和今日统计
- **断线重连**：自动识别并恢复对局状态
- **非联赛对局**：支持非联赛对局的 MMR 变动记录

## 项目结构

```
HDT_BGTracker/
├── HearthstoneBattlegroundTracker/    # WPF 主程序
│   ├── BattlegroundSpy/               # 内存读取（UnitySpy）
│   │   ├── BattlegroundSpyReader.cs   # 核心：MMR、大厅、场面、RatingChange
│   │   └── Objects/                   # 数据模型
│   ├── BattlegroundSpy.Test/          # BGSpy 测试程序
│   ├── Parser/                        # Power.log 解析器
│   ├── Services/                      # 核心服务
│   │   ├── GameMonitorService.cs      # 游戏状态机
│   │   ├── HearthMirrorService.cs     # BGSpy 包装层
│   │   ├── LeagueClient.cs            # 联赛 API 客户端
│   │   └── ApiClient.cs               # HTTP API 客户端
│   ├── Models/                        # 数据模型
│   ├── Windows/                       # UI（MainWindow + Overlay）
│   └── Data/                          # 嵌入资源（bg_heroes.json）
├── LeagueTool/                        # mock 服务器 + 测试工具
│   └── mock_server.py                 # Python mock 服务器
└── API.md                             # API 文档
```

## 编译

### 前置条件

- .NET 8 SDK
- Windows x86 环境

### 步骤

```powershell
cd HearthstoneBattlegroundTracker
dotnet build -c Release
```

编译产物：`bin\Release\net8.0-windows\HearthstoneBattlegroundTracker.exe`

### 运行测试

```powershell
dotnet run --project BattlegroundSpy.Test -c Release
```

## 配置

程序启动后自动检测炉石进程，无需手动配置。

### config.json（可选）

在 exe 同目录创建 `config.json`：

```json
{
  "apiBaseUrl": "http://localhost:5000",
  "region": "CN",
  "mode": "solo"
}
```

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `apiBaseUrl` | 服务端 API 地址 | `http://localhost:5000` |
| `region` | 服务器区域 | `CN` |
| `mode` | 游戏模式 | `solo` |

## API 端点

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/plugin/initialize-player` | POST | 初始化玩家信息，获取验证码，上报 MMR |
| `/api/plugin/check-league` | POST | 检查是否为联赛对局 |
| `/api/plugin/update-placement` | POST | 上报排名（仅联赛对局） |
| `/api/plugin/report-game-stats` | POST | 上报游戏统计（MMR + 阵容 + RatingChange） |

详细字段说明见 [API.md](API.md)。

## 调试（mock 服务器）

```bash
python LeagueTool/mock_server.py
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
