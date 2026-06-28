using System.Collections.Generic;
using System.Linq;
using BattlegroundDB;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 数据驱动行为 - 从 BattlegroundDB 自动推断随从行为
    /// 对于有 Deathrattle 关键词但没有注册特殊行为的随从，
    /// 尝试通过 ChildIds 推断亡语生成的衍生物
    /// </summary>
    public static class DataDrivenBehaviors
    {
        // === 已知的 ChildIds → 亡语生成映射 ===
        // 某些随从的 ChildIds 不是衍生物，需要排除
        private static readonly HashSet<string> ExcludedFromChildIdDeathrattle = new HashSet<string>
        {
            // 金色版本的 ChildIds 指向金色衍生物，不是亡语
        };

        /// <summary>
        /// 为有 Deathrattle 关键词的随从创建数据驱动的亡语
        /// </summary>
        public static IDeathrattle CreateDeathrattle(string cardId, bool controlledByPlayer)
        {
            var card = Cards.GetByCardId(cardId);
            if (card == null || !card.HasKeyword("Deathrattle"))
                return null;

            // 如果有 ChildIds，可能是亡语生成的衍生物
            if (card.ChildIds != null && card.ChildIds.Count > 0)
            {
                // 查找 ChildIds 中的随从卡牌
                var childCards = card.ChildIds
                    .Select(id => Cards.GetById(id))
                    .Where(c => c != null && c.IsMinion && c.CardId != cardId)
                    .ToList();

                if (childCards.Count > 0)
                {
                    // 每个 ChildId 生成一个衍生物
                    var summonCardIds = childCards.Select(c => c.CardId).ToList();
                    int count = 1; // 默认生成 1 个
                    int goldenCount = 2; // 金色默认生成 2 个

                    return new GenericDeathrattle(
                        golden: false, // 金色状态由调用方设置
                        summonCardIds: summonCardIds,
                        count: count,
                        goldenCount: goldenCount
                    );
                }
            }

            // 没有 ChildIds 或 ChildIds 不是衍生物的，返回 null
            // 这些随从需要手动注册特殊行为
            return null;
        }
    }
}
