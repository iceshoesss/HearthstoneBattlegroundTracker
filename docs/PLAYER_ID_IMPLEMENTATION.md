# 开发日志 - PLAYER_ID 实现详解

## 概述

PLAYER_ID 是炉石酒馆战棋中每个玩家的唯一标识（tag 2），用于在排行榜悬停时匹配对手阵容缓存。本文档记录了 PLAYER_ID 的获取方式、使用场景、遇到的问题及解决方案。

## 什么是 PLAYER_ID

- **Tag ID**: 2 (`GameTag.PLAYER_ID`)
- **值范围**: 1-8（BG 中 8 个玩家各有一个唯一 ID）
- **存储位置**: 英雄实体的 tags 中
- **特点**: 一局游戏内唯一且稳定，不会因英雄皮肤重复而冲突

## 为什么需要 PLAYER_ID

### 问题：heroCardId 作为缓存键的缺陷

早期实现使用 `heroCardId`（如 `TB_BaconShop_HERO_93`）作为阵容缓存的键。这存在两个问题：

1. **英雄皮肤重复**: 同一英雄有多个皮肤（如 `TB_BaconShop_HERO_93` 和 `TB_BaconShop_HERO_93_SKIN_X`），但基础 cardId 相同
2. **镜像对局**: 两个玩家选同一英雄时，缓存会互相覆盖

### 解决方案：PLAYER_ID

PLAYER_ID 是每个玩家的唯一编号，不受英雄选择影响。用它作为缓存键可以准确区分不同玩家。

## 获取 PLAYER_ID 的三种途径

### 1. Power.log 解析（EntityTracker）✅ 推荐

**原理**: Power.log 中的 `TAG_CHANGE Entity=[...] tag=PLAYER_ID value=X` 事件会被解析器捕获，更新到 EntityTracker 中。

**代码路径**:
```
Parser.ProcessLine() 
  → ReTagChangeWithId / ReTagChangeSimple 
  → entityTracker.UpdateTag(entityId, tagId=2, value)
```

**EntityTracker 中的查询**:
```csharp
// 在 EntityTracker 中查找对手英雄的 PLAYER_ID
foreach (var entity in _entityTracker.Entities.Values)
{
    if (entity.IsHero && entity.CardId == heroCardId 
        && entity.Controller != localController && entity.Controller > 0)
    {
        playerId = entity.GetTag(2); // PLAYER_ID
        break;
    }
}
```

**优点**: 数据最准确，与 HDT 的 `_game.Entities` 一致
**缺点**: 依赖 Power.log 事件，可能有延迟

### 2. BGSpy m_entityMap（游戏内存）❌ 不可用

**原理**: 直接从游戏内存的 `GameState.s_instance.m_entityMap` 读取英雄实体的 tags。

**实测结果**: `m_entityMap` 中的英雄实体 **没有 PLAYER_ID tag**（所有英雄都是 0）。这是游戏内存的限制，不是读取问题。

**诊断输出**:
```
Hero: BG20_HERO_242 (EntityId=118)
    PLAYER_ID=0, CONTROLLER=3, ZONE=1
```

### 3. BGSpy ZonePlay（游戏内存）❌ 不可用

**原理**: 从 `ZoneMgr.s_instance.m_zones` 中 ZonePlay(m_Side=2) 的英雄实体读取 tags。

**实测结果**: ZonePlay 中的英雄实体是 **UI 对象**，tags 不完整，没有 PLAYER_ID。

## 当前实现架构

### 数据流

```
Power.log TAG_CHANGE events
        ↓
    EntityTracker (维护实体字典)
        ↓
    GetTag(2) → PLAYER_ID
        ↓
    _opponentBoardCache[playerId] = boardRecord
        ↓
    HoverWatcherLoop 查找缓存
        ↓
    显示对手阵容
```

### 三个使用 PLAYER_ID 的位置

| 位置 | 方法 | 数据源 |
|------|------|--------|
| 阵容快照 | `HandleBoardSnapshot()` | EntityTracker |
| 战斗开始捕获 | `HandleCombatStart()` | EntityTracker |
| 悬停查找 | `HoverWatcherLoop` | EntityTracker |

### 三个位置的统一逻辑

```csharp
// 从 EntityTracker 获取对手英雄的 PLAYER_ID
int playerId = 0;
var localController = _hm.GetLocalControllerIdPublic();
if (localController != null)
{
    foreach (var entity in _entityTracker.Entities.Values)
    {
        if (entity.IsHero && entity.CardId == heroCardId 
            && entity.Controller != localController.Value 
            && entity.Controller > 0)
        {
            playerId = entity.GetTag(2); // PLAYER_ID
            break;
        }
    }
}
```

## 遇到的问题及解决方案

### 问题 1：m_entityMap 没有 PLAYER_ID

**现象**: 所有英雄的 `playerId=0`
**原因**: 游戏内存的 `m_entityMap` 在 BG 模式下不设置 PLAYER_ID tag
**解决**: 改用 EntityTracker（Power.log 数据）

### 问题 2：Hover 时找不到英雄实体

**现象**: 战斗时能找到（playerId=5），购物阶段找不到（playerId=0）
**原因**: `ClearStaleEntities()` 删除了非 ZONE=PLAY 的实体，英雄在 zone=2(DECK) 被清除
**解决**: `ClearStaleEntities` 跳过 `IsHero` 的实体

### 问题 3：BGSpy ZonePlay 的 hero entity 没有完整 tags

**现象**: `GetOpponentBoardState()` 返回 `PlayerId=0`
**原因**: ZonePlay 中的 hero entity 是 UI 对象，不是游戏逻辑实体
**解决**: 不从 ZonePlay 读 PLAYER_ID，改用 EntityTracker

### 问题 4：m_playerId 字段不可靠

**现象**: 尝试从 `tile["m_playerId"]` 读取，返回 0
**原因**: 排行榜 tile 对象的 `m_playerId` 字段可能不存在或未设置
**解决**: 不依赖此字段，统一用 EntityTracker

## 缓存键选择

### 最终方案：PLAYER_ID (int)

```csharp
private readonly Dictionary<int, OpponentBoardRecord> _opponentBoardCache = new();
```

### 对比

| 方案 | 优点 | 缺点 |
|------|------|------|
| heroCardId (string) | 简单直接 | 同英雄冲突、皮肤重复 |
| PLAYER_ID (int) | 唯一、稳定 | 需要从 EntityTracker 获取 |
| EntityId (int) | 精确 | 每局变化，不适合跨回合 |

## 相关 Commit

| Commit | 说明 |
|--------|------|
| `a78c2e9` | 缓存键从 heroCardId 改为 PLAYER_ID |
| `8e5d12b` | GetLeaderboardHoveredPlayerId 优先从 tile.m_playerId 读取 |
| `a379ee0` | GetLeaderboardHoveredPlayerId 通过 m_entityMap 查找 |
| `e91b6b2` | GetOpponentBoardState 通过 m_entityMap 查找 PLAYER_ID |
| `22377fe` | GetLeaderboardHoveredPlayerId 用 heroCardId 查找 |
| `bb09acd` | 从 EntityTracker 获取 PLAYER_ID（替代 m_entityMap） |
| `abf268d` | HandleCombatStart 也从 EntityTracker 获取 PLAYER_ID |
| `3e1842f` | ClearStaleEntities 保留英雄实体 |

## 调试方法

### 诊断 EntityTracker 中的英雄实体

在 `HandleBoardSnapshot()` 中添加临时日志：
```csharp
Log("[Diag] EntityTracker 英雄实体:");
foreach (var entity in _entityTracker.Entities.Values)
{
    if (entity.IsHero)
    {
        Log($"[Diag]   {entity.CardId} entityId={entity.EntityId} " +
            $"playerId={entity.GetTag(2)} controller={entity.Controller} zone={entity.Zone}");
    }
}
```

### 诊断 m_entityMap 中的英雄实体

运行 `BattlegroundSpy.Test` 程序，输出包含：
```
=== m_entityMap 英雄实体 ===
[Dump] m_entityMap: 557 entries
  Hero: BG20_HERO_242 (EntityId=118)
    PLAYER_ID=0, CONTROLLER=3, ZONE=1
```

## 与 HDT 的对比

| | HDT | 我们的实现 |
|---|---|---|
| 数据源 | `_game.Entities` (Power.log) | `EntityTracker` (Power.log) |
| 获取方式 | `entity.GetTag(GameTag.PLAYER_ID)` | `entity.GetTag(2)` |
| 缓存键 | `Dictionary<int, BoardSnapshot>` | `Dictionary<int, OpponentBoardRecord>` |
| 清理策略 | 不清理（内存由 GC 管理） | `ClearStaleEntities` 保留英雄 |
