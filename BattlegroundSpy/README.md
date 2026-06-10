# BattlegroundSpy

炉石传说酒馆战棋内存读取库，用于从游戏进程中读取实时数据。

## 架构

```
BattlegroundSpy（本项目）
  ├── BattlegroundSpyReader.cs    — 对外 API
  ├── Objects/GameObjects.cs      — 数据模型
  └── UnitySpy（源码链接，MIT）   — Mono 内存读取引擎
       ├── UnitySpy/              — 核心库
       └── UnitySpy.HearthstoneLib/ — 炉石数据读取
```

## 已实现的 API

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `GetBattleTag()` | `BattleTag?` | 玩家 BattleTag（如 `玩家名#1234`） |
| `GetAccountId()` | `AccountId?` | 账号 ID（Hi + Lo） |
| `GetSceneMode()` | `int?` | 当前场景模式（3=主菜单, 4=对局, 15=大厅） |
| `GetBattlegroundsLobbyInfo()` | `BattlegroundsLobbyInfo?` | 大厅 8 位玩家信息 |
| `GetMatchInfo()` | `MatchInfo?` | 对局信息 |
| `GetBattlegroundsHeroOptions()` | `List<NameCardId>?` | 英雄选择（TODO） |
| `GetBattlegroundRatingInfo()` | `BattlegroundRatingInfo?` | 评分信息（TODO） |
| `GetAvailableBattlegroundsRaces()` | `List<int>?` | 可用种族（TODO） |
| `IsGameOver()` | `bool` | 游戏是否结束（TODO） |
| `IsMulligan()` | `bool` | 是否在英雄选择（TODO） |

## 构建

### 前置条件

- .NET 8.0 SDK
- Windows x86 环境

### 编译

```powershell
cd HearthstoneBattlegroundTracker/BattlegroundSpy
dotnet build -c Release
```

输出：`bin/Release/net8.0-windows/BattlegroundSpy.dll`

### 运行测试

```powershell
cd HearthstoneBattlegroundTracker/BattlegroundSpy.Test
dotnet build -c Release
cd bin/Release/net8.0-windows
./BattlegroundSpy.Test.exe
```

测试程序需要炉石传说正在运行。

## 内存读取原理

炉石传说使用 Unity 引擎，游戏逻辑运行在 Mono 虚拟机上。BattlegroundSpy 通过以下步骤读取内存：

1. **找到 mono-2.0-bdwgc.dll** — Mono 运行时模块
2. **调用 mono_get_root_domain** — 获取根域
3. **遍历 Assembly 列表** — 找到 Assembly-CSharp
4. **读取类的 s_instance** — 获取单例对象
5. **遍历字段** — 读取具体数据

### 关键内存路径

```
BattleTag:
  BnetPresenceMgr.s_instance.m_myPlayer.m_account.m_battleTag

AccountId:
  BnetPresenceMgr.s_instance.m_myGameAccountId.low_/high_

SceneMode:
  SceneMgr.s_instance.m_mode

大厅玩家:
  GameState.s_instance.m_playerInfoMap.valueSlots[i]
    .m_name          — 玩家名
    .m_gameAccountId.low_  — AccountId.Lo
    .m_playerHero.m_cardIdInternal — 英雄卡牌 ID
```

## 维护指南

### 何时需要维护

| 情况 | 症状 | 处理方式 |
|------|------|---------|
| 炉石版本更新 | API 返回 null | 检查字段名是否变化 |
| Unity 版本升级 | 初始化失败 | 更新 UnitySpy 偏移量 |
| 新功能需求 | 需要读取新数据 | 添加新方法 |

### 诊断步骤

1. 运行 `BattlegroundSpy.Test.exe`，观察输出
2. 如果返回 null，用调试代码探索实际字段名
3. 参考 `d:/coding/HearthMirror_Decompiled` 中的 HearthMirror 实现

### 调试示例

当 `GetBattlegroundsLobbyInfo()` 返回空时，添加调试代码探索字段：

```csharp
var image = typeof(BattlegroundSpyReader)
    .GetField("_image", BindingFlags.NonPublic | BindingFlags.Instance)
    ?.GetValue(reader) as IAssemblyImage;

var gameState = image["GameState"]?["s_instance"];
var playerInfoMap = gameState?["m_playerInfoMap"];
var valueSlots = playerInfoMap?["valueSlots"];

// 遍历查看实际字段
for (int i = 0; i < 16; i++)
{
    var item = valueSlots[i];
    if (item == null) break;
    Console.WriteLine($"[{i}] {item["m_name"]}");
}
```

### 字段变化历史

| 日期 | 炉石版本 | 变化 |
|------|---------|------|
| 2026-05-29 | 当前 | `BaconLobbyMgr.s_instance` 不再可用，改用 `GameState.s_instance.m_playerInfoMap.valueSlots` |

## 参考资源

- **HearthMirror 反编译**: `d:/coding/HearthMirror_Decompiled/`
- **UnitySpy 源码**: `d:/coding/HDT_Reverse/unity-spy/`
- **UnitySpy 偏移量**: `UnitySpy/Offsets/MonoLibraryOffsets.cs`

## 依赖

| 依赖 | 许可证 | 引入方式 |
|------|--------|---------|
| UnitySpy | MIT | 源码链接（编译进 BattlegroundSpy.dll） |
| Newtonsoft.Json | MIT | NuGet |
| JetBrains.Annotations | MIT | NuGet |

## 许可证

本项目代码遵循项目根目录的 LICENSE。
UnitySpy 部分遵循 MIT 许可证。
