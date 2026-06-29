# HBTCombat 开发维护指南

## 目录

- [架构概述](#架构概述)
- [添加新随从行为](#添加新随从行为)
- [添加新的行为接口](#添加新的行为接口)
- [更新卡牌数据](#更新卡牌数据)
- [版本更新流程](#版本更新流程)
- [调试技巧](#调试技巧)
- [常见问题](#常见问题)

---

## 架构概述

### 核心组件

```
HBTCombat/
├── Interfaces/           # 行为接口定义
│   ├── IDeathrattle.cs   # 亡语召唤
│   ├── IDeathrattleEffect.cs  # 亡语效果（非召唤）
│   ├── IOnStartOfCombat.cs    # 战斗开始
│   ├── IOnAfterAttack.cs      # 攻击后
│   ├── IOnFriendlyMinionDied.cs   # 友方死亡
│   ├── IOnFriendlyMinionSummoned.cs  # 友方召唤
│   ├── IAvenge.cs              # 复仇
│   ├── IPassiveAttackBonus.cs  # 被动攻击
│   ├── IPassiveHealthBonus.cs  # 被动生命
│   └── IRebornBehavior.cs      # 自定义复生
├── Minions/
│   ├── Minion.cs           # 随从基类
│   ├── MinionFactory.cs    # 随从工厂 + 行为注册表
│   └── MinionBehaviors.cs  # 所有随从行为注册（主要维护文件）
├── Data/
│   ├── DataDrivenBehaviors.cs  # 数据驱动行为推断
│   └── GenericDeathrattle.cs   # 通用亡语实现
└── Simulation/
    ├── CombatSimulator.cs  # 核心战斗循环
    ├── CombatState.cs      # 战斗上下文
    ├── SimulationRunner.cs # 多线程模拟器
    ├── SimulationInput.cs  # 输入
    └── SimulationOutput.cs # 输出
```

### 数据流

```
HBT 主程序
    ↓ (BoardMinion / TrackedEntity)
MinionFactory.CreateFromCardId()
    ↓ (查询 BattlegroundDB + 附加行为)
Minion 对象
    ↓ (传入 SimulationInput)
CombatSimulator.SimulateFight()
    ↓ (触发行为接口)
SimulationOutput (胜率/伤害)
```

### 关键设计决策

1. **Minion 是 class 不是 struct**: 因为需要继承和行为接口附加
2. **行为通过注册表附加**: MinionFactory 维护 CardId → 行为的映射
3. **共享行为引用**: Clone() 时行为列表是共享的（不可变），减少内存
4. **毒/烈毒在伤害结算时生效**: 不依赖攻击者存活，符合炉石机制

---

## 添加新随从行为

### 步骤 1: 确定行为类型

| 行为类型 | 接口 | 示例随从 |
|---------|------|---------|
| 亡语召唤衍生物 | `IDeathrattle` | Rat Pack, Infested Wolf |
| 亡语效果（buff/伤害） | `IDeathrattleEffect` | Goldrinn, Unstable Ghoul |
| 战斗开始触发 | `IOnStartOfCombat` | Red Whelp |
| 攻击后触发 | `IOnAfterAttack` | Monstrous Macaw |
| 友方随从死亡 | `IOnFriendlyMinionDied` | Scavenging Hyena |
| 友方随从召唤 | `IOnFriendlyMinionSummoned` | Mama Bear |
| 复仇 | `IAvenge` | (待实现) |
| 被动攻击加成 | `IPassiveAttackBonus` | Mal'Ganis |
| 被动生命加成 | `IPassiveHealthBonus` | Mal'Ganis |
| 自定义复生 | `IRebornBehavior` | (特殊随从) |

### 步骤 2: 实现行为类

#### 示例: 亡语召唤类 (IDeathrattle)

```csharp
/// <summary>
/// Rat Pack 亡语：召唤等同于攻击力数量的 1/1 Rat
/// </summary>
public class RatPackDeathrattle : IDeathrattle
{
    public List<Minion> TriggerDeathrattle(Minion source, bool golden)
    {
        int count = source.BaseAttack;
        if (golden) count *= 2;
        
        var result = new List<Minion>();
        var factory = MinionFactoryCache.GetFactory();
        
        for (int i = 0; i < count; i++)
        {
            var rat = factory.CreateFromCardId("BG21_002t", source.ControlledByPlayer);
            rat.Golden = golden;
            result.Add(rat);
        }
        
        return result;
    }
}
```

#### 示例: 亡语效果类 (IDeathrattleEffect)

```csharp
/// <summary>
/// Goldrinn 亡语：给所有友方野兽 +4/+4
/// </summary>
public class GoldrinnDeathrattleEffect : IDeathrattleEffect
{
    public void Trigger(Minion source, CombatState state)
    {
        int atkBonus = source.Golden ? 8 : 4;
        int hpBonus = source.Golden ? 8 : 4;
        
        var friendlyBoard = state.GetFriendlyBoard(source);
        foreach (var m in friendlyBoard)
        {
            if (m.PrimaryRace == "Beast")
            {
                m.BaseAttack += atkBonus;
                m.MaxHealth += hpBonus;
                m.CurrentHealth += hpBonus;
            }
        }
    }
}
```

#### 示例: 友方死亡触发 (IOnFriendlyMinionDied)

```csharp
/// <summary>
/// Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
/// </summary>
public class ScavengingHyenaTrigger : IOnFriendlyMinionDied
{
    public void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state)
    {
        if (dead.PrimaryRace == "Beast")
        {
            int atkBonus = self.Golden ? 4 : 2;
            int hpBonus = self.Golden ? 2 : 1;
            
            self.BaseAttack += atkBonus;
            self.MaxHealth += hpBonus;
            self.CurrentHealth += hpBonus;
        }
    }
}
```

#### 示例: 战斗开始触发 (IOnStartOfCombat)

```csharp
/// <summary>
/// Red Whelp: 战斗开始时每有一条龙造成 1 伤害
/// </summary>
public class RedWhelpTrigger : IOnStartOfCombat
{
    private static readonly Random Rng = new Random();
    
    public void OnStartOfCombat(Minion self, CombatState state)
    {
        var friendlyBoard = state.GetFriendlyBoard(self);
        int dragonCount = 0;
        
        foreach (var m in friendlyBoard)
        {
            if (m.PrimaryRace == "Dragon")
                dragonCount++;
        }
        
        int damage = self.Golden ? dragonCount * 2 : dragonCount;
        if (damage <= 0) return;
        
        var enemyBoard = state.GetEnemyBoard(self);
        if (enemyBoard.Count == 0) return;
        
        // 对随机敌方随从造成伤害
        for (int i = 0; i < damage; i++)
        {
            if (enemyBoard.Count == 0) break;
            var target = enemyBoard[Rng.Next(enemyBoard.Count)];
            target.TakeDamage(1);
        }
    }
}
```

### 步骤 3: 注册行为

在 `MinionBehaviors.cs` 的 `RegisterAll()` 方法中注册：

```csharp
public static void RegisterAll()
{
    // ... 已有注册 ...
    
    // === 新增随从 ===
    
    // Rat Pack: 亡语召唤等同于攻击力数量的 1/1 Rat
    MinionFactory.RegisterDeathrattle("BG21_002", (m, p) => new List<IDeathrattle>
    {
        new RatPackDeathrattle()
    });
    
    // Goldrinn: 亡语给所有友方野兽 +4/+4
    MinionFactory.RegisterDeathrattleEffect("BG21_007", (m, p) => new List<IDeathrattleEffect>
    {
        new GoldrinnDeathrattleEffect()
    });
    
    // Scavenging Hyena: 当友方野兽死亡时获得 +2/+1
    MinionFactory.RegisterFriendlyMinionDied("BG21_044", (m, p) => new List<IOnFriendlyMinionDied>
    {
        new ScavengingHyenaTrigger()
    });
}
```

### 注册方法对照表

| 行为类型 | 注册方法 |
|---------|---------|
| 亡语召唤 | `MinionFactory.RegisterDeathrattle(cardId, factory)` |
| 亡语效果 | `MinionFactory.RegisterDeathrattleEffect(cardId, factory)` |
| 战斗开始 | `MinionFactory.RegisterStartOfCombat(cardId, factory)` |
| 攻击后 | `MinionFactory.RegisterAfterAttack(cardId, factory)` |
| 友方死亡 | `MinionFactory.RegisterFriendlyMinionDied(cardId, factory)` |
| 友方召唤 | `MinionFactory.RegisterFriendlyMinionSummoned(cardId, factory)` |
| 复仇 | `MinionFactory.RegisterAvenge(cardId, factory)` |
| 被动攻击 | `MinionFactory.RegisterPassiveAttackBonus(cardId, factory)` |
| 被动生命 | `MinionFactory.RegisterPassiveHealthBonus(cardId, factory)` |
| 自定义复生 | `MinionFactory.RegisterRebornBehavior(cardId, factory)` |

---

## 添加新的行为接口

如果现有接口不能满足需求，需要添加新接口：

### 步骤 1: 定义接口

在 `Interfaces/` 目录创建新接口：

```csharp
namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 新行为接口说明
    /// </summary>
    public interface INewBehavior
    {
        void OnNewEvent(Minion self, CombatState state);
    }
}
```

### 步骤 2: 更新 Minion 基类

在 `Minion.cs` 中添加：

```csharp
// 添加行为列表
public List<INewBehavior> NewBehaviors { get; set; } = new List<INewBehavior>();

// 更新 Clone() 方法
clone.NewBehaviors = NewBehaviors;

// 更新 HasBehavior<T>() 方法
if (typeof(T) == typeof(INewBehavior) && NewBehaviors.Count > 0) return true;
```

### 步骤 3: 更新 MinionFactory

在 `MinionFactory.cs` 中添加：

```csharp
// 添加注册表
private static readonly Dictionary<string, Func<Minion, bool, List<INewBehavior>>> NewBehaviorRegistry
    = new Dictionary<string, Func<Minion, bool, List<INewBehavior>>>();

// 添加注册方法
public static void RegisterNewBehavior(string cardId, Func<Minion, bool, List<INewBehavior>> factory)
{
    NewBehaviorRegistry[cardId] = factory;
}

// 更新 AttachBehaviors() 方法
if (NewBehaviorRegistry.TryGetValue(cardId, out var nbFactory))
    minion.NewBehaviors = nbFactory(minion, controlledByPlayer);
```

### 步骤 4: 更新 CombatSimulator

在 `CombatSimulator.cs` 的适当位置触发新行为：

```csharp
// 例如在攻击后触发
foreach (var trigger in attacker.NewBehaviors)
    trigger.OnNewEvent(attacker, state);
```

---

## 更新卡牌数据

### BattlegroundDB 更新

卡牌数据由 BattlegroundDB 提供，更新流程：

1. 运行 `HsbgFetcher` 获取最新卡牌数据
2. 重新编译 `BattlegroundDB.dll`
3. 替换 `Lib/BattlegroundDB.dll`
4. 重新编译 `HBTCombat.dll`（因为它引用 BattlegroundDB）

### 新随从上线时

1. 确认随从的 CardId（在 BattlegroundDB 的 `bg_cards.json` 中查找）
2. 确认随从的行为类型
3. 按照 [添加新随从行为](#添加新随从行为) 流程实现
4. 运行测试验证

### 随从下线时

1. 在 `MinionBehaviors.cs` 中注释或删除对应注册
2. 保留行为类代码（可能随从会回归）

---

## 版本更新流程

### 小版本更新（新随从/平衡调整）

1. 更新 BattlegroundDB（卡牌数据）
2. 在 `MinionBehaviors.cs` 中添加/修改随从行为
3. 运行测试: `dotnet test HBTCombat.Tests`
4. 编译: `dotnet build HBTCombat -c Release`
5. 复制 DLL: `cp HBTCombat/bin/Release/net472/HBTCombat.dll Lib/`
6. 提交代码

### 大版本更新（新机制/接口变更）

1. 评估是否需要新接口
2. 如需要，按 [添加新的行为接口](#添加新的行为接口) 流程
3. 更新现有随从行为
4. 更新测试
5. 编译并测试
6. 更新版本号（在 csproj 中）
7. 提交代码

### DLL 自动更新

HBTCombat.dll 需要支持自动更新（类似 BattlegroundDB）：

1. 在 Cloudflare Worker 添加 HBTCombat.dll 的下载端点
2. 在 HBT 主程序中添加版本检查逻辑
3. 下载新 DLL 到 `Lib/HBTCombat.dll.new`
4. 创建批处理脚本在退出时替换

---

## 调试技巧

### 查看模拟日志

在 HBT 主程序中，模拟日志输出到 `GameMonitorService.Log()`：

```
[模拟] 开始模拟: 己方7个 vs 对方7个, DamageCap=15
[模拟] 完成: 胜65% 平5% 负30% 我方5~15 对方3~12
```

### 单步调试

1. 在 Visual Studio 中设置断点
2. 运行 HBT 主程序
3. 进入战斗阶段时会触发模拟
4. 断点会命中 `CombatSimulator.SimulateFight()`

### 测试特定随从

创建单元测试验证特定随从行为：

```csharp
[TestMethod]
public void TestNewMinion()
{
    var input = CreateInput(
        playerBoard: new[] { ("NEW_CARD_ID", 5, 5, 3) },
        opponentBoard: new[] { ("TEST_002", 5, 5, 3) }
    );
    
    // 设置特殊状态
    input.PlayerBoard[0].SomeProperty = true;
    
    var result = _simulator.SimulateFight(input);
    
    // 验证结果
    Assert.IsTrue(result > 0, "Expected win");
}
```

### 性能分析

模拟性能关键点：

- `CloneBoard()`: 每次模拟都会克隆面板
- `ResolveAllDeaths()`: 死亡处理可能递归
- `TriggerDeathrattle()`: 亡语可能召唤多个衍生物

如果模拟太慢：
1. 减少迭代次数（默认 10000）
2. 减少超时时间（默认 1500ms）
3. 优化 Minion.Clone()（考虑使用对象池）

---

## 常见问题

### Q: 新随从没有效果？

**A:** 检查以下：
1. CardId 是否正确？（在 BattlegroundDB 中查找）
2. 是否在 `MinionBehaviors.RegisterAll()` 中注册？
3. 行为类是否正确实现接口？
4. 接口方法是否被正确触发？（在 CombatSimulator 中检查）

### Q: 亡语没有触发？

**A:** 检查以下：
1. 随从是否有 `Deathrattle` 关键词？
2. 是否注册了 `IDeathrattle` 或 `IDeathrattleEffect`？
3. `CombatSimulator.TriggerDeathrattle()` 是否被调用？
4. 面板是否已满（最多 7 个随从）？

### Q: 模拟结果不准确？

**A:** 可能原因：
1. 缺少某些随从的特殊行为
2. 行为实现有 bug
3. 随从属性读取错误（攻击/生命/关键词）
4. 需要更多迭代次数

### Q: 如何添加新的关键词支持？

**A:** 
1. 在 `Minion.cs` 添加属性（如 `public bool NewKeyword { get; set; }`）
2. 在 `Minion.Clone()` 中复制
3. 在 `MinionFactory.CreateFromCardId()` 中从 BattlegroundDB 读取
4. 在 `CombatSimulator` 中处理效果

### Q: 如何处理 ScriptDataNum？

**A:** 某些随从用 ScriptDataNum 存储状态（如计数器）：

```csharp
// 在 Minion 中已有属性
public int ScriptDataNum1 { get; set; }
public int ScriptDataNum2 { get; set; }

// 创建时从游戏状态读取
var minion = factory.CreateFromCardId(cardId, player,
    attack, health, maxHealth,
    taunt, divineShield, poisonous, venomous,
    windfury, megaWindfury, stealth, reborn,
    golden, tier,
    scriptDataNum1,  // 从游戏状态读取
    scriptDataNum2
);

// 在行为中使用
public void OnSomeEvent(Minion self, CombatState state)
{
    int counter = self.ScriptDataNum1;
    // ...
}
```

---

## 代码规范

### 命名约定

- 接口: `I` 前缀，如 `IDeathrattle`
- 行为类: 描述性名称，如 `RatPackDeathrattle`, `ScavengingHyenaTrigger`
- 注册方法: `Register` + 行为类型，如 `RegisterDeathrattle`

### 注释要求

- 接口和公共方法必须有 XML 注释
- 复杂逻辑需要行内注释
- 随从行为需要说明触发条件和效果

### 测试要求

- 新随从行为必须有对应测试
- 测试覆盖正常情况和边界情况
- 测试名称清晰描述测试内容

---

## 参考资源

- [BobsBuddy 分析](../../HDT_Reverse/BobsBuddy_analysis.txt) - BobsBuddy 架构参考
- [BattlegroundDB 源码](../../BattlegroundDB/) - 卡牌数据模型
- [HDT BobsBuddyUtils](../../Hearthstone-Deck-Tracker/Hearthstone%20Deck%20Tracker/BobsBuddy/BobsBuddyUtils.cs) - HDT 到 BobsBuddy 的桥接参考

---

## 当前覆盖情况 (2024 年版本)

### 手动注册的随从 (~20 个)

**亡语效果:**
- Goldrinn (T6): 给所有友方野兽 +8/+8
- Scarlet Skull (T2): +1/+2 to a friendly Undead
- Tunnel Blaster (T4): 对所有随从造成 3 伤害
- Silent Enforcer (T4): 对所有非恶魔随从造成 2 伤害
- Baneling (T2): 对随机敌方造成等同于攻击力的伤害
- Plaguerunner (T4): +1/+1 for each friendly minion that died
- Elementium Squirrel Bomb (T4): 4 damage per friendly mech that died
- Ingenious Inventor (T5): +1/+1 for each friendly minion that died

**友方死亡触发:**
- Scavenging Hyena: 友方野兽死亡 +2/+1
- Junkbot: 友方机械死亡 +2/+2
- Flesheating Ghoul: 任意随从死亡 +1 攻击
- Imp Gang Boss: 友方恶魔死亡召唤 Imp

**战斗开始:**
- Red Whelp: 每条龙造成 1 伤害
- Spirit of Air: 给随机友方 Windfury+DivineShield+Taunt

**攻击后:**
- Monstrous Macaw: 触发友方亡语

**召唤触发:**
- Mama Bear: 召唤野兽 +4/+4
- Pack Leader: 召唤野兽 +3 攻击

**特殊亡语:**
- Sly Raptor: 召唤 6/6 野兽
- Twilight Hatchling: 召唤 3/3 并立即攻击
- Handless Forsaken: 召唤 2/1 并具有复生

### 数据驱动 (47 个)

有 ChildIds 的亡语随从会自动通过 DataDrivenBehaviors 创建 GenericDeathrattle。这些随从不需要手动实现。

### 关键词系统

所有具有以下关键词的随由 CombatSimulator 自动处理:
- Taunt (39 个)
- Divine Shield (28 个)
- Windfury (6 个)
- Reborn (11 个)
- Venomous (7 个)

### 待实现

- Leeroy the Reckless: 摧毁击杀者
- Kangor's Apprentice: 复制前 2 个死亡的机械
- Baron Rivendare: 亡语触发两次
- Khadgar: 召唤随从时召唤 2 个副本
- Rylak Metalhead: 触发相邻随从的战吼
- Timewarped Warghoul: 触发相邻随从的亡语
- 更多 Timewarped 随从的特殊行为
