using System.Collections.Generic;

namespace HBT
{
/// <summary>
/// 游戏实体（通过 Power.log 追踪）
/// 类似 HDT 的 Entity 类
/// </summary>
public class TrackedEntity
{
    public int EntityId { get; set; }
    public string CardId { get; set; } = "";
    public Dictionary<int, int> Tags { get; set; } = new Dictionary<int, int>();

    // 便捷属性
    public int GetTag(int tagId) => Tags.TryGetValue(tagId, out var val) ? val : 0;
    public bool HasTag(int tagId) => Tags.ContainsKey(tagId) && Tags[tagId] > 0;
    public void SetTag(int tagId, int value) => Tags[tagId] = value;

    // 常用标签快捷方式
    public int CardType => GetTag(202);      // CARDTYPE
    public int Zone => GetTag(49);           // ZONE
    public int Controller => GetTag(50);     // CONTROLLER
    public int Attack => GetTag(47);         // ATK
    public int Health => GetTag(45);         // HEALTH
    public int Damage => GetTag(197);        // DAMAGE
    public int MaxHealth => Health - Damage;
    public bool Golden => HasTag(364);       // PREMIUM
    public bool Taunt => HasTag(190);        // TAUNT
    public bool DivineShield => HasTag(194); // DIVINE_SHIELD
    public bool Poisonous => HasTag(363);    // POISONOUS
    public bool Venomous => HasTag(2853);    // VENOMOUS
    public bool Windfury => HasTag(189);     // WINDFURY
    public bool Reborn => HasTag(1085);      // REBORN
    public bool Stealth => HasTag(191);      // STEALTH
    public bool Deathrattle => HasTag(217);  // DEATHRATTLE
    public int TechLevel => GetTag(1440);    // TECH_LEVEL
    public int ZonePosition => GetTag(263);  // ZONE_POSITION

    // 酒馆战棋特有
    public bool IsMinion => CardType == 4;
    public bool IsHero => CardType == 3;
    public bool IsInPlay => Zone == 1;       // ZONE_PLAY = 1
}

/// <summary>
/// 实体追踪器 — 通过 Power.log 解析维护实体字典
/// 类似 HDT 的 GameV2.Entities
/// </summary>
public class EntityTracker
{
    private readonly Dictionary<int, TrackedEntity> _entities = new Dictionary<int, TrackedEntity>();

    public IReadOnlyDictionary<int, TrackedEntity> Entities => _entities;

    /// <summary>获取或创建实体</summary>
    public TrackedEntity GetOrCreate(int entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity))
        {
            entity = new TrackedEntity { EntityId = entityId };
            _entities[entityId] = entity;
        }
        return entity;
    }

    /// <summary>更新实体标签</summary>
    public void UpdateTag(int entityId, int tagId, int value)
    {
        var entity = GetOrCreate(entityId);
        entity.SetTag(tagId, value);
    }

    /// <summary>设置实体卡牌 ID</summary>
    public void SetCardId(int entityId, string cardId)
    {
        var entity = GetOrCreate(entityId);
        entity.CardId = cardId;
    }

    /// <summary>获取对手的随从列表（ZONE=PLAY, CARDTYPE=MINION, CONTROLLER≠本地玩家）</summary>
    public List<TrackedEntity> GetOpponentMinions(int localControllerId)
    {
        var result = new List<TrackedEntity>();
        foreach (var entity in _entities.Values)
        {
            if (entity.IsMinion && entity.IsInPlay && entity.Controller != localControllerId && entity.Controller > 0)
            {
                result.Add(entity);
            }
        }
        result.Sort((a, b) => a.ZonePosition.CompareTo(b.ZonePosition));
        return result;
    }

    /// <summary>清除残留实体：非 PLAY 区域 + 对手的 PLAY 随从
    /// 保留英雄实体（用于 PLAYER_ID 查找）</summary>
    public void ClearStaleEntities(int localControllerId = 0)
    {
        var toRemove = new List<int>();
        foreach (var kvp in _entities)
        {
            var entity = kvp.Value;
            // 保留英雄实体（PLAYER_ID 查找需要）
            if (entity.IsHero) continue;

            if (entity.Zone != 1) // 非 PLAY 区域直接清除
            {
                toRemove.Add(kvp.Key);
            }
            else if (localControllerId > 0 && entity.IsMinion && entity.Controller != localControllerId)
            {
                // PLAY 区域的对手随从也清除（防止跨战斗累积）
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
        {
            _entities.Remove(id);
        }
    }

    /// <summary>获取对手英雄（CARDTYPE=HERO, CONTROLLER≠本地玩家, 非GRAVEYARD）</summary>
    public TrackedEntity GetOpponentHero(int localControllerId)
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.IsHero && entity.Controller != localControllerId && entity.Controller > 0)
            {
                // 跳过鲍勃
                if (entity.CardId == "TB_BaconShopBob") continue;
                // 跳过已淘汰的英雄（GRAVEYARD=4）
                if (entity.Zone == 4) continue;
                // 跳过占位英雄
                if (entity.CardId == "TB_BaconShop_HERO_PH") continue;
                return entity;
            }
        }
        return null;
    }

    /// <summary>清除所有实体</summary>
    public void Clear()
    {
        _entities.Clear();
    }
}
}
