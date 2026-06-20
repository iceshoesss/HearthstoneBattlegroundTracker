using System;
using System.Collections.Generic;
using System.Linq;
using BattlegroundDB;

namespace HBT.Services
{

/// <summary>
/// 卡牌数据库查询服务 - 基于 BattlegroundDB v2 (BgdbCard)
/// </summary>
public class CardDatabaseService
{
    private bool _loaded;

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            Cards.Load();
            System.Diagnostics.Debug.WriteLine(
                $"CardDB loaded: {Cards.Minions.Count} minions, {Cards.Heroes.Count} heroes, " +
                $"{Cards.Trinkets.Count} trinkets, {Cards.Anomalies.Count} anomalies");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CardDB load error: {ex}");
        }
    }

    // === 查询 API ===

    public BgdbCard GetMinion(string cardId)
    {
        EnsureLoaded();
        return Cards.GetByCardId(cardId);
    }

    public BgdbCard GetMinion(int dbfId)
    {
        EnsureLoaded();
        return Cards.GetById(dbfId);
    }

    public BgdbCard GetHero(string cardId)
    {
        EnsureLoaded();
        return Cards.GetHero(cardId);
    }

    public List<BgdbCard> GetMinionsByTier(int tier)
    {
        EnsureLoaded();
        return Cards.GetMinionsByTier(tier);
    }

    public List<BgdbCard> GetMinionsByRace(string race)
    {
        EnsureLoaded();
        return Cards.GetMinionsByMinionType(race);
    }

    /// <summary>新格式种族名（Title Case）→ 中文名</summary>
    private static readonly Dictionary<string, string> RaceChinese = new()
    {
        ["Mech"] = "机械",
        ["Demon"] = "恶魔",
        ["Dragon"] = "龙",
        ["Elemental"] = "元素",
        ["Murloc"] = "鱼人",
        ["Beast"] = "野兽",
        ["Pirate"] = "海盗",
        ["Quilboar"] = "野猪人",
        ["Undead"] = "亡灵",
        ["Naga"] = "纳迦",
        ["All"] = "全部",
    };

    /// <summary>TAG_RACE 整数值 → 种族代码（与新格式 minionType 一致）</summary>
    private static readonly Dictionary<int, string> TagRaceToCode = new()
    {
        [11] = "Undead",
        [14] = "Murloc",
        [15] = "Demon",
        [17] = "Mech",
        [18] = "Elemental",
        [20] = "Beast",      // HearthDb Race.BEAST = 20
        [23] = "Pirate",
        [24] = "Dragon",
        [43] = "Quilboar",
        [92] = "Naga",
    };

    /// <summary>获取种族中文名</summary>
    public static string GetRaceChinese(string race)
    {
        if (string.IsNullOrEmpty(race)) return "";
        return RaceChinese.TryGetValue(race, out var cn) ? cn : race;
    }

    /// <summary>TAG_RACE 整数列表 → 种族代码列表</summary>
    public static List<string> TagRacesToCodes(List<int> tagRaces)
    {
        return tagRaces
            .Where(t => TagRaceToCode.ContainsKey(t))
            .Select(t => TagRaceToCode[t])
            .ToList();
    }

    public HashSet<string> GetRaces()
    {
        EnsureLoaded();
        return Cards.GetMinionTypes();
    }

    public List<BgdbCard> GetAllMinions()
    {
        EnsureLoaded();
        return Cards.Minions.Where(m => !m.IsDuosOnly).ToList();
    }

    public List<BgdbCard> GetAllHeroes()
    {
        EnsureLoaded();
        return Cards.Heroes.Where(h => !h.IsDuosOnly).ToList();
    }

    public BgdbCard GetTrinket(string cardId)
    {
        EnsureLoaded();
        return Cards.GetTrinket(cardId);
    }

    public BgdbCard GetAnomaly(string cardId)
    {
        EnsureLoaded();
        return Cards.GetAnomaly(cardId);
    }

    public List<BgdbCard> SearchMinions(string query)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(query))
            return Cards.Minions.Where(m => !m.IsDuosOnly && !m.IsToken && !m.IsBuddy && !m.IsTimewarped).ToList();

        return Cards.Search(query)
            .Where(m => m.IsMinion && !m.IsDuosOnly && !m.IsToken && !m.IsBuddy && !m.IsTimewarped)
            .ToList();
    }

    public List<BgdbCard> SearchMinions(string? query, string? race, int? tier)
    {
        EnsureLoaded();
        IEnumerable<BgdbCard> result = Cards.Minions.Where(m => !m.IsDuosOnly && !m.IsToken && !m.IsBuddy && !m.IsTimewarped);

        if (!string.IsNullOrWhiteSpace(query))
            result = result.Where(m =>
                (m.Name != null && m.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
             || (m.NameZh != null && m.NameZh.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
             || (m.CardId != null && m.CardId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
        if (!string.IsNullOrWhiteSpace(race) && race != "全部")
        {
            if (race == "NEUTRAL")
                result = result.Where(m => string.IsNullOrEmpty(m.MinionType) || m.MinionType == "All");
            else
                result = result.Where(m => m.MinionType == race);
        }
        if (tier is > 0 and <= 7)
            result = result.Where(m => m.Tier == tier);

        return result.OrderBy(m => m.Tier).ThenBy(m => m.Name ?? "").ToList();
    }

    /// <summary>获取卡牌关键词标签（中文）</summary>
    public static List<string> GetKeywords(BgdbCard m)
    {
        if (m.Keywords == null || m.Keywords.Count == 0)
            return new List<string>();

        var tags = new List<string>();
        foreach (var kw in m.Keywords)
        {
            switch (kw)
            {
                case "Battlecry": tags.Add("战吼"); break;
                case "Deathrattle": tags.Add("亡语"); break;
                case "Reborn": tags.Add("复生"); break;
                case "Divine Shield": tags.Add("圣盾"); break;
                case "Start of Turn": tags.Add("回合开始"); break;
                case "Venomous": tags.Add("烈毒"); break;
                case "Windfury":
                case "Mega-Windfury": tags.Add("风怒"); break;
                case "Taunt": tags.Add("嘲讽"); break;
                case "Aura": tags.Add("光环"); break;
                case "End of Turn": tags.Add("回合结束"); break;
            }
        }
        return tags;
    }
}
}
