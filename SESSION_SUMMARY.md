# HBT 开发 Session 总结

## 项目目标
复刻 HDT 战棋模式所有功能，用 WPF 重做 UI，做一个可替代 HDT BG 模式的插件。

## 当前分支
`feat/HBT`

## 已完成的工作

### 1. BattlegroundSpy 内存读取库
- **位置**: `HBT/BattlegroundSpy/`
- **技术**: 使用 UnitySpy（MIT 开源）读取炉石进程内存
- **依赖**: UnitySpy 源码链接编译，无需外部 DLL
- **已实现 API**:
  - `GetBattleTag()` — 玩家 BattleTag
  - `GetAccountId()` — 账号 ID (Hi + Lo)
  - `GetSceneMode()` — 场景模式 (3=主菜单, 4=对局, 15=大厅)
  - `GetBattlegroundsLobbyInfo()` — 大厅 8 位玩家信息
- **关键内存路径**:
  - `GameState.s_instance.m_playerInfoMap.valueSlots[i]` — 大厅玩家
  - `BnetPresenceMgr.s_instance.m_myPlayer.m_account.m_battleTag` — BattleTag
  - `SceneMgr.s_instance.m_mode` — 场景模式

### 2. GameMonitorService 游戏监控
- **位置**: `HBT/Services/GameMonitorService.cs`
- **架构**: 双线程
  - **SceneWatcher** (16ms): 内存轮询场景变化，用于 UI 显示
  - **LogWatcher** (100ms): Power.log 解析，游戏状态主驱动
- **状态机**: Idle → PreLobby → Lobby → Active → PostGame
- **3 个核心 API**: upload-rating, check-league, update-placement

### 3. 从 LeagueTool 移植的模块
- `HBT/Parser/Parser.cs` — Power.log 解析（纯函数式）
- `HBT/Models/` — Game, GameState, GameEvent, GameRecord
- `HBT/Services/ApiClient.cs` — HTTP 客户端
- `HBT/Services/LeagueClient.cs` — API 编排 + 重试
- `HBT/Services/GameStore.cs` — 战绩持久化 (JSONL)
- `HBT/Services/LogWatcher.cs` — 文件尾部读取
- `HBT/Services/LogPathFinder.cs` — 日志路径发现
- `HBT/Services/HeroNameResolver.cs` — 英雄名解析
- `HBT/Services/Config.cs` — 配置加载

### 4. WPF UI
- `HBT/Windows/MainWindow.xaml` — 主窗口
- `HBT/Windows/OverlayWindow.xaml` — 游戏内覆盖层

## 关键技术决策

| 决策 | 选择 | 原因 |
|------|------|------|
| 内存读取 | UnitySpy (MIT) | 原 ScryDotNet 是专有库 |
| 游戏检测 | Power.log 为主 + SceneWatcher 辅助 | HDT 方案，更可靠 |
| 日志扫描 | 从末尾读 2MB | 避免读取整个文件（170万行） |
| 大厅信息 | GameState.m_playerInfoMap | 反编译 HearthMirror 正确路径 |

## 已知问题

1. **排名检测**: `HeroPlacement` 有时为 0，已添加诊断日志
2. **BobsBuddy**: .NET 8 无法加载，需要重写
3. **BattlegroundSpy 未实现**: GetBattlegroundsHeroOptions, GetBattlegroundRatingInfo 等

## 重要文件

| 文件 | 用途 |
|------|------|
| `HBT/BattlegroundSpy/BattlegroundSpyReader.cs` | 内存读取 API |
| `HBT/Services/GameMonitorService.cs` | 游戏监控主逻辑 |
| `HBT/Parser/Parser.cs` | Power.log 解析 |
| `HBT/Windows/MainWindow.xaml` | 主窗口 UI |
| `HBT/BattlegroundSpy/README.md` | BattlegroundSpy 维护文档 |

## 参考资源

- HearthMirror 反编译: `d:/coding/HearthMirror_Decompiled/`
- UnitySpy 源码: `d:/coding/HDT_Reverse/unity-spy/`
- HDT 源码: `d:/coding/Hearthstone-Deck-Tracker/`

## 构建

```powershell
cd HBT
dotnet build -c Release
```

## 测试

```powershell
cd HBT/BattlegroundSpy.Test
dotnet build -c Release
cd bin/Release/net8.0-windows
./BattlegroundSpy.Test.exe
```
