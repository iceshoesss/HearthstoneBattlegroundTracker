using System;
using System.Collections.Generic;
using BattlegroundDB;

namespace HBT
{

/// <summary>
/// 通过 BattlegroundDB 将 heroCardId 解析为中文英雄名
/// </summary>
public static class HeroNameResolver
{
    private static bool _initialized;
    private static Dictionary<string, string> _map = new Dictionary<string, string>();

    /// <summary>
    /// 解析 heroCardId → 中文英雄名，失败返回原 cardId
    /// </summary>
    public static string Resolve(string heroCardId)
    {
        if (string.IsNullOrEmpty(heroCardId)) return "";

        if (!_initialized)
        {
            _initialized = true;
            LoadFromBattlegroundDB();
            LoadConstructedHeroes();
        }

        if (_map.TryGetValue(heroCardId, out var name) && !string.IsNullOrEmpty(name))
            return name;

        // 尝试剥离后缀后匹配（HERO_06a → HERO_06，BG20_HERO_100_SKIN_A → BG20_HERO_100）
        var baseId = GetBaseCardId(heroCardId);
        if (baseId != heroCardId && _map.TryGetValue(baseId, out var baseName) && !string.IsNullOrEmpty(baseName))
            return baseName;

        return heroCardId;
    }

    /// <summary>提取基础卡牌 ID</summary>
    public static string GetBaseCardId(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return cardId;

        // HERO_XX 格式：前 7 个字符
        if (cardId.Length >= 7)
        {
            var prefix = cardId.Substring(0, 7);
            if (prefix.StartsWith("HERO_") && char.IsDigit(prefix[5]) && char.IsDigit(prefix[6]))
                return prefix;
        }

        // BG20_HERO_100_SKIN_A → BG20_HERO_100（剥离 _SKIN_ 后缀）
        var skinIndex = cardId.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
        if (skinIndex > 0)
            return cardId.Substring(0, skinIndex);

        return cardId;
    }

    /// <summary>从 BattlegroundDB 加载英雄名</summary>
    private static void LoadFromBattlegroundDB()
    {
        try
        {
            Cards.Load();
            foreach (var hero in Cards.Heroes)
            {
                if (!string.IsNullOrEmpty(hero.CardId) && !string.IsNullOrEmpty(hero.NameZh))
                {
                    _map[hero.CardId] = hero.NameZh;
                }
                else if (!string.IsNullOrEmpty(hero.CardId) && !string.IsNullOrEmpty(hero.Name))
                {
                    _map[hero.CardId] = hero.Name;
                }
            }
            Console.WriteLine($"[HeroNameResolver] ✅ 已加载 {_map.Count} 个英雄名（BattlegroundDB）");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[HeroNameResolver] ⚠️ 加载失败: {e.Message}");
        }
    }

    private static void LoadConstructedHeroes()
    {
        var constructed = new Dictionary<string, string>
        {
            {"HERO_01", "战士"}, {"HERO_02", "萨满"}, {"HERO_03", "盗贼"},
            {"HERO_04", "圣骑士"}, {"HERO_05", "猎人"}, {"HERO_06", "德鲁伊"},
            {"HERO_07", "术士"}, {"HERO_08", "法师"}, {"HERO_09", "牧师"},
            {"HERO_10", "恶魔猎手"}, {"HERO_11", "死亡骑士"},
        };
        foreach (var kv in constructed)
            if (!_map.ContainsKey(kv.Key)) _map[kv.Key] = kv.Value;
    }
}
}
