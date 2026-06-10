# HBT 项目方法速查手册

> 记录项目中所有可复用的方法、服务、工具类及其用法。避免重复造轮子。

---

## 目录

1. [API 客户端 (ApiClient)](#1-apiclient)
2. [联赛逻辑 (LeagueClient)](#2-leagueclient)
3. [游戏存储 (GameStore)](#3-gamestore)
4. [阵容存储 (BoardStateStore)](#4-boardstatestore)
5. [日志读取 (LogWatcher)](#5-logwatcher)
6. [日志路径查找 (LogPathFinder)](#6-logpathfinder)
7. [英雄名解析 (HeroNameResolver)](#7-heronameresolver)
8. [卡牌数据库 (CardDatabaseService)](#8-carddatabaseservice)
9. [图片缓存 (ImageCacheService)](#9-imagecacheservice)
10. [配置加载 (Config)](#10-config)
11. [Parser 解析器](#11-parser-解析器)
12. [BattlegroundSpy](#12-battlegroundspy)
13. [事件模型 (GameEvent)](#13-gameevent)
14. [插件系统](#14-插件系统)

---

## 1. ApiClient

**文件**: `Services/ApiClient.cs`
**用途**: 对 LeagueWeb 服务端的所有 HTTP 请求。

### 初始化

```csharp
ApiClient.Init("https://league.example.com");
```

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `InitializePlayerAsync(playerId, accountIdHi, accountIdLo, rating)` | `Task<bool>` | 启动时初始化玩家，获取验证码 |
| `CheckLeagueAsync(playerId, accountIdLo, lobbyPlayers, region, mode, startedAt)` | `Task<bool?>` | 检查是否为联赛对局。`true`=联赛, `false`=非联赛, `null`=重试 |
| `CheckLeagueAsync(playerId, accountIdLo, opponentLo, localDisplay, oppDisplay, localBT, oppBT, localHero, oppHero, region, mode, startedAt)` | `Task<bool?>` | 构筑模式重载 |
| `UpdatePlacementAsync(gameUuid, playerId, accountIdLo, placement, reconnectTimes, otherPlacements)` | `Task<bool>` | 上报排名（含 3 次重试） |
| `UpdatePlacementAsync(gameUuid, localTag, localLo, localPl, oppTag, oppLo, oppPl, mode)` | `Task<bool>` | 构筑模式重载 |
| `ReportGameStatsAsync(gameUuid, playerId, accountIdLo, rating, boardState, ratingChange, placement, trinkets, anomalyDbfId)` | `Task<bool>` | 上报游戏统计（阵容 + MMR 变动） |
| `PingAsync()` | `Task<bool>` | 测试服务器连通性 |

### 静态属性

| 属性 | 说明 |
|------|------|
| `VerificationCode` | 最近一次获取的验证码 |
| `ServerGameUuid` | 服务端返回的 gameUuid |
| `LastLeagueResult` | 最近 check-league 是否为联赛 |
| `LastError` | 最近一次错误信息 |

### 工具方法

| 方法 | 说明 |
|------|------|
| `GenerateDeterministicUuid(lobbyPlayers)` | 从 8 人 AccountId.Lo 集合生成确定性 UUID |
| `GetRegionFromAccountIdHi(accountIdHi)` | 从 accountIdHi 计算 region 码 |
| `GetRegionString(region)` | region 码 → 区域字符串 (`1=US, 2=EU, 3=ASIA, 5=CN`) |

---

## 2. LeagueClient

**文件**: `Services/LeagueClient.cs`
**用途**: 联赛逻辑编排。独立于 UI，负责 check-league / update-placement 调用 + 重试 + 过期保护。

### 初始化

```csharp
var league = new LeagueClient(config);
league.OnStateChanged = () => { /* UI 更新 */ };
```

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `OnGameStart()` | `int` | 新局开始调用，返回 generation 用于过期判断 |
| `OnCheckLeague(playerTag, accountIdLo, lobbyPlayers, region, mode, shouldRetry)` | `void` | BG 对局 check-league（自动重试 + 15 秒周期重试） |
| `OnCheckLeagueConstructed(playerTag, accountIdLo, opponentAccountIdLo, ...)` | `void` | 构筑模式 check-league |
| `OnGameEnd(gameUuid, playerTag, accountIdLo, placement, reconnectTimes, otherPlacements)` | `Task<bool>` | 上报排名 |
| `Reset()` | `void` | PID 变化时重置 |
| `StopRetry()` | `void` | 停止重试定时器 |

### 属性

| 属性 | 说明 |
|------|------|
| `IsLeagueGame` | 当前对局是否为联赛 |
| `GameUuid` | 当前对局 UUID |
| `VerificationCode` | 验证码 |

---

## 3. GameStore

**文件**: `Services/GameStore.cs`
**用途**: 对局记录持久化（JSONL 格式，`games.json`）。零 JSON 库依赖。

### 初始化

```csharp
GameStore.Init(); // 使用 exe 目录
GameStore.Init("C:\\custom\\path"); // 自定义目录
```

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `Save(record)` | `void` | 追加一条记录（JSONL，线程安全） |
| `GetToday(battleTag)` | `List<GameRecord>` | 获取今日记录 |
| `GetRecent(count, battleTag)` | `List<GameRecord>` | 获取最近 N 条记录 |
| `Load()` | `List<GameRecord>` | 加载全部记录（带缓存） |
| `SaveTodayStartMmr(mmr, battleTag)` | `void` | 保存今日起始 MMR |
| `GetTodayStartMmr(battleTag)` | `int` | 读取今日起始 MMR |

### 自定义 JSONL 存储模板

项目中多处自建了 JSONL 存储（GameStore、BoardStateStore）。核心模式：

```csharp
// 序列化
var sb = new StringBuilder();
sb.Append("{\"key\":\"").Append(Esc(value)).Append('"');
sb.Append(",\"number\":").Append(num).Append('}');
File.AppendAllText(path, sb.ToString() + "\n", Encoding.UTF8);

// 解析
private static string Extract(string json, string key)
{
    var search = $"\"{key}\":\"";
    var idx = json.IndexOf(search);
    if (idx < 0) return "";
    idx += search.Length;
    var end = json.IndexOf('"', idx);
    return end < 0 ? "" : json.Substring(idx, end - idx);
}

private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
```

---

## 4. BoardStateStore

**文件**: `Services/BoardStateStore.cs`
**用途**: 最终阵容持久化（JSONL 格式，`boardstate.json`）。最多保留 20 条。

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `Init(dir)` | `void` | 初始化路径 |
| `Save(gameUuid, battleTag, boardState)` | `void` | 保存阵容（自动 trim） |
| `GetRecent(count, battleTag)` | `List<BoardStateRecord>` | 获取最近 N 条 |
| `GetByGameUuid(gameUuid)` | `BoardStateRecord` | 按 UUID 查询 |

---

## 5. LogWatcher

**文件**: `Services/LogWatcher.cs`
**用途**: 增量读取 Power.log，自动跳过旧内容。

### 使用

```csharp
var watcher = new LogWatcher("C:\\path\\to\\Power.log");

// 可选：跳到文件末尾（只读新写入的）
watcher.SetPosition(new FileInfo("Power.log").Length);

// 读取新行
var lines = watcher.TryReadLines(); // string[]?  null=文件出错, []=无新行

// 切换到新日志文件（炉石重启时）
watcher.SwitchTo(newPath);
```

### 特性

- 首次只读最后 5MB
- 自动检测日志文件轮转（大小回卷）
- `OnFileNotFound` 事件

---

## 6. LogPathFinder

**文件**: `Services/LogPathFinder.cs`
**用途**: 自动查找 Power.log 路径。

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `Find(customPath)` | `string?` | 查找最新 Power.log（注册表 + 进程 + 常见路径 + 全盘扫描） |
| `CheckNewLogFile(currentPath)` | `string?` | 检查是否有更新的日志文件 |
| `ResetProcessDirCache()` | `void` | 炉石重启后重置缓存 |

### 查找顺序

1. 自定义路径
2. 注册表 `Hearthstone\InstallPath`
3. 运行中炉石进程 exe 目录
4. 常见安装路径（含国服中文路径）
5. 全盘扫描 `*Hearthstone*` / `*炉石*` 目录

---

## 7. HeroNameResolver

**文件**: `Services/HeroNameResolver.cs`
**用途**: CardId → 中文名映射。零外部依赖。

### 使用

```csharp
var name = HeroNameResolver.Resolve("BG34_630"); // "弗拉达姆"
var name2 = HeroNameResolver.Resolve("HERO_01"); // "战士"
```

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `Resolve(cardId)` | `string` | CardId → 中文名（失败返回原 cardId） |
| `GetBaseCardId(cardId)` | `string` | 剥离后缀 (`BG20_HERO_100_SKIN_A` → `BG20_HERO_100`) |

### 数据源

- 嵌入资源 `bg_heroes.json`（BG 英雄）
- 硬编码 `HERO_01~11`（构筑职业英雄）

---

## 8. CardDatabaseService

**文件**: `Services/CardDatabaseService.cs`
**用途**: 酒馆卡牌数据库查询。基于 BattlegroundDB.dll 嵌入资源。

### 使用

```csharp
var cardDb = new CardDatabaseService();
var minion = cardDb.GetMinion("BG34_630");
var minions = cardDb.GetMinionsByRace("MURLOC");
var minions = cardDb.GetMinionsByTier(5);
var results = cardDb.SearchMinions("弗拉达姆", "MURLOC", 5); // 组合查询
var keywords = CardDatabaseService.GetKeywords(minion); // ["亡语", "复生"]
```

### 方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `GetMinion(cardId)` | `Minion` | 按 CardId 查询 |
| `GetMinion(dbfId)` | `Minion` | 按 DBF ID 查询 |
| `GetHero(cardId)` | `Hero` | 按 CardId 查询英雄 |
| `GetMinionsByTier(tier)` | `List<Minion>` | 按等级查询 |
| `GetMinionsByRace(race)` | `List<Minion>` | 按种族查询 |
| `SearchMinions(query, race, tier)` | `List<Minion>` | 组合搜索（名称/种族/等级） |
| `SearchMinions(query)` | `List<Minion>` | 按名称搜索 |
| `GetTrinket(cardId)` | `Trinket` | 饰品查询 |
| `GetAnomaly(cardId)` | `Anomaly` | 畸变查询 |

### 静态工具

| 方法 | 说明 |
|------|------|
| `TagRacesToCodes(List<int>)` | TAG_RACE 整数 → 种族代码列表 |
| `GetRaceChinese(race)` | 种族代码 → 中文名 |
| `GetKeywords(Minion)` | 获取关键词标签列表 |

### 种族 TAG_RACE 映射

| 整数值 | 代码 | 中文 |
|--------|------|------|
| 11 | UNDEAD | 亡灵 |
| 14 | MURLOC | 鱼人 |
| 15 | DEMON | 恶魔 |
| 17 | MECHANICAL | 机械 |
| 18 | ELEMENTAL | 元素 |
| 20 | PET | 野兽 |
| 23 | PIRATE | 海盗 |
| 24 | DRAGON | 龙 |
| 43 | QUILBOAR | 野猪人 |
| 92 | NAGA | 纳迦 |

---

## 9. ImageCacheService

**文件**: `Services/ImageCacheService.cs`
**用途**: 卡牌图片缓存（内存 LRU + 本地磁盘 + ETag 验证）。

### 使用

```csharp
var cache = new ImageCacheService();

// 同步：缓存中有则返回，否则返回占位符
var img = cache.GetTileOrPlaceholder("BG34_630");

// 异步：ETag 验证 + 下载 + 缓存
var img = await cache.GetTileAsync("BG34_630");
```

### 特性

- 来源: `https://art.hearthstonejson.com/v1/tiles/{cardId}.png`
- 内存 LRU 上限: 300 张
- 本地缓存: `%APPDATA%\HearthstoneBattlegroundTracker\Images\tiles\`
- ETag 验证避免重复下载

---

## 10. Config

**文件**: `Services/Config.cs`
**用途**: 配置加载（自定义 JSON 解析，零依赖）。

### 使用

```csharp
var config = Config.Load();
// config.ApiBaseUrl, config.Region, config.Mode, config.TestMode
```

### 查找顺序

1. 环境变量 `BGTRACKER_CONFIG` 指定路径
2. 从 exe 目录向上 5 级查找 `shared_config.json`
3. exe 同目录 `config.json`
4. exe 同目录 `config.json.example`

### 配置格式

```json
{
  "apiBaseUrl": "https://league.example.com",
  "region": "CN",
  "mode": "solo",
  "testMode": false
}
```

---

## 11. Parser 解析器

**文件**: `Parser/Parser.cs`
**用途**: 逐行解析 Power.log，产出强类型事件。纯函数，无内部状态。

### 使用

```csharp
var game = new Game { IsActive = true };
var state = GameState.CreateInitial();
var games = new List<Game>();

var (evt, newState, newGame) = Parser.ProcessLine(logLine, game, state, games);
state = newState;
game = newGame;
```

### 静态方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `ProcessLine(line, game, state, games)` | `(GameEvent, GameState, Game)` | 核心：处理一行日志 |
| `IsHeroCard(cardId)` | `bool` | 判断是否英雄卡牌 |
| `IsConstructedGameType(gameType)` | `bool` | 判断是否为构筑模式 |
| `GameTypeToMode(gameType, formatType)` | `string?` | GameType → mode 映射 |
| `NewGame()` | `Game` | 创建新 Game 对象 |
| `EndGame(game, games)` | `void` | 标记游戏结束 |

### 正则表达式参考

| 事件 | 正则 | 示例 |
|------|------|------|
| CREATE_GAME | `GameState\.DebugPrintPower\(\) - CREATE_GAME$` | 游戏开始 |
| GameType | `GameType=(\w+)` | GT_BATTLEGROUNDS |
| FormatType | `FormatType=(\w+)` | FT_WILD |
| PlayerName | `PlayerID=(\d+),\s*PlayerName=(.+?)$` | 玩家信息 |
| AccountId | `GameAccountId=\[hi=\d+ lo=(\d+)\]` | 账号 ID |
| HERO_ENTITY | `TAG_CHANGE Entity=(.+?) tag=HERO_ENTITY value=(\d+)` | 英雄匹配 |
| FULL_ENTITY | `FULL_ENTITY - ... cardId=(\w+)` | 实体创建/更新 |
| LEADERBOARD | `tag=PLAYER_LEADERBOARD_PLACE value=(\d+)` | 排名变化 |
| GRAVEYARD | `tag=ZONE value=GRAVEYARD` | 英雄淘汰 |
| STEP | `tag=STEP value=(\w+)` | 回合阶段 |
| STATE COMPLETE | `tag=STATE value=COMPLETE` | 游戏结束 |
| Concede | `tag=(3479|4356) value=1` | 投降信号 |

---

## 12. BattlegroundSpy

**文件**: `BattlegroundSpy/BattlegroundSpyReader.cs`
**用途**: 从炉石进程读取实时数据。基于 UnitySpy。

### 初始化

```csharp
var reader = new BattlegroundSpyReader(); // 自动查找炉石进程
// 或
var reader = new BattlegroundSpyReader(processId); // 指定 PID
```

### 内存读取方法

| 方法 | 返回值 | 内存路径 |
|------|--------|----------|
| `GetBattleTag()` | `BattleTag?` | `BnetPresenceMgr.s_instance.m_myPlayer.m_account.m_battleTag` |
| `GetAccountId()` | `AccountId?` | `BnetPresenceMgr.s_instance.m_myGameAccountId` |
| `GetSceneMode()` | `int?` | `SceneMgr.s_instance.m_mode` |
| `GetBattlegroundsLobbyInfo()` | `BattlegroundsLobbyInfo?` | `GameState.s_instance.m_playerInfoMap.valueSlots` |
| `GetBattlegroundRatingInfo()` | `BattlegroundRatingInfo?` | `NetCache → NetCacheBaconRatingInfo` |
| `GetBaconRatingChangeData()` | `RatingChangeData?` | `GameState.m_gameEntity.<RatingChangeData>` |
| `GetPlayerBoardMinions()` | `List<BoardMinion>?` | `ZoneMgr → ZonePlay(side=1)` |
| `GetOpponentBoardState()` | `OpponentBoardState?` | `ZoneMgr → ZonePlay(side=2)` |
| `GetPlayerTrinkets()` | `List<string>?` | `m_entityMap` 过滤 BACON_TRINKET |
| `GetTurnNumber()` | `int?` | `GameEntity.tags[TURN=20]` |
| `GetAvailableBattlegroundsRaces()` | `List<int>?` | `GameState.m_availableRacesInBattlegroundsExcludingAmalgam` |
| `GetLocalHeroEntityId()` | `int` | `m_entityMap` 中找本地玩家英雄 |
| `GetAnomalyDbfId()` | `int` | `GameEntity.tags[2897]` |
| `IsGameOver()` | `bool` | `GameState.m_gameOver` |

### 辅助方法

| 方法 | 返回值 | 用途 |
|------|--------|------|
| `GetService(name)` | `dynamic?` | 通过 ServiceLocator 获取服务 |
| `TryGetField(obj, name)` | `dynamic?` | 安全字段读取 |
| `GetTagValue(tags, key)` | `int` | 从 tag 字典取指定值 |
| `ReadTagDict(tagValues)` | `Dictionary<int,int>` | 读取 tag 字典 |
| `GetCollectionSize(collection)` | `int` | 兼容多种集合 size 访问 |
| `FindZonePlay(side)` | `dynamic?` | 查找指定侧的 ZonePlay |
| `GetLocalControllerId()` | `int?` | 获取本地玩家 CONTROLLER ID |
| `DiagnoseServiceLocator()` | `string` | 诊断 ServiceLocator 路径 |
| `DiagnoseZoneMgr()` | `string` | 诊断 ZoneMgr 路径 |
| `DiagnoseRatingChangeData()` | `string` | 诊断 RatingChangeData 字段 |

### 常用 TAG 常量

| 值 | 含义 |
|----|------|
| 47 | ATK |
| 45 | HEALTH |
| 49 | ZONE |
| 50 | CONTROLLER |
| 53 | ENTITY_ID |
| 190 | TAUNT |
| 191 | STEALTH |
| 194 | DIVINE_SHIELD |
| 202 | CARDTYPE |
| 217 | DEATHRATTLE |
| 363 | POISONOUS |
| 364 | PREMIUM(GOLDEN) |
| 1085 | REBORN |
| 1440 | TECH_LEVEL |
| 189 | WINDFURY |
| 263 | ZONE_POSITION |
| 2853 | VENOMOUS |
| 2897 | BACON_GLOBAL_ANOMALY_DBID |
| 3407 | BACON_TRINKET |

---

## 13. GameEvent

**文件**: `Models/GameEvent.cs`
**用途**: Parser 产出的强类型事件，用于解耦解析器和业务逻辑。

### 事件类型

| 事件 | 含义 |
|------|------|
| `GameStartEvent` | 新对局开始 |
| `ReconnectEvent` | 断线重连 |
| `NotBgEvent` | 非酒馆对局 |
| `PlayerInfoEvent` | 玩家信息 |
| `HeroEntityEvent` | 英雄实体匹配 |
| `CheckLeagueEvent` | STEP 13 到达，可查联赛 |
| `GameEndEvent` | 游戏结束（含 placement / 构筑结果） |
| `ConcedeEvent` | 投降 |
| `ConstructedGameStartEvent` | 构筑模式开始 |
| `ConstructedCheckLeagueEvent` | 构筑模式可查联赛 |

---

## 14. 插件系统

**文件**: `Plugins/`

### IHbtPlugin 接口

```csharp
public class MyPlugin : IHbtPlugin
{
    public string Name => "MyPlugin";
    public string Description => "My plugin";
    public string Author => "Me";
    public Version Version => new Version(1, 0, 0);

    public void OnLoad() { }
    public void OnUnload() { }
    public void OnUpdate() { }        // ~100ms 一次
    public void OnGameStart() { }     // 游戏开始
    public void OnGameEnd(int placement, int ratingChange) { }
}
```

### PluginManager

```csharp
var pm = new PluginManager(Path.Combine(baseDir, "Plugins"));
pm.LoadAll();   // 扫描 *.dll，自动识别 IHbtPlugin 和 HDT 兼容插件
pm.Update();    // 定时调用
pm.OnGameStart();
pm.OnGameEnd(1, +5);
```

### HDT 兼容

自动识别 `Hearthstone_Deck_Tracker.Plugins.IPlugin` 接口，通过 `HdtPluginAdapter` 适配。

---

## 附录：关键设计决策

1. **所有 JSON 序列化均自建** — 避免 Newtonsoft.Json 依赖，使用 `StringBuilder` 手动构造
2. **日志读取只读 5MB** — `LogWatcher` 首次只读文件尾部 5MB，跳过历史
3. **幂等 UUID 生成** — `ApiClient.GenerateDeterministicUuid()` 用 SHA256 从 8 人 Lo 生成确定性 UUID
4. **异步超时 + 重试** — `HttpClient` 超时 15 秒，`UpdatePlacement` 重试 3 次，`check-league` 15 秒周期重试
5. **Generation 过期保护** — `LeagueClient` 用递增 generation 数避免过期回调覆盖新数据
6. **try-catch 静默失败** — BGSpy 所有内存读取方法 catch 所有异常返回 null，由调用方判断
7. **动态反射访问 Unity 对象** — 全部使用 `_image["ClassName"]["fieldName"]` 动态索引，无强类型映射
8. **JSONL 追加写入** — `GameStore` / `BoardStateStore` 都采用 JSONL 格式，避免全量重写锁文件
