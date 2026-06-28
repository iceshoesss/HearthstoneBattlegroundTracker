using System.Collections.Generic;
using HBTCombat.Interfaces;

namespace HBTCombat
{
    /// <summary>
    /// 通用亡语实现 - 召唤衍生物
    /// </summary>
    public class GenericDeathrattle : IDeathrattle
    {
        private readonly bool _golden;
        private readonly List<string> _summonCardIds;
        private readonly int _count;
        private readonly int _goldenCount;

        /// <summary>
        /// 创建通用亡语
        /// </summary>
        /// <param name="golden">是否为金色版本</param>
        /// <param name="summonCardIds">召唤的衍生物 CardId 列表</param>
        /// <param name="count">普通版本召唤数量（每个 CardId）</param>
        /// <param name="goldenCount">金色版本召唤数量（每个 CardId）</param>
        public GenericDeathrattle(bool golden, List<string> summonCardIds, int count = 1, int goldenCount = 2)
        {
            _golden = golden;
            _summonCardIds = summonCardIds;
            _count = count;
            _goldenCount = goldenCount;
        }

        public List<Minion> TriggerDeathrattle(Minion source, bool golden)
        {
            int summonCount = (golden || _golden) ? _goldenCount : _count;
            var result = new List<Minion>();

            foreach (var cardId in _summonCardIds)
            {
                for (int i = 0; i < summonCount; i++)
                {
                    var token = MinionFactoryCache.GetFactory().CreateFromCardId(cardId, source.ControlledByPlayer);
                    token.Golden = golden || _golden;
                    result.Add(token);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// MinionFactory 缓存（用于在亡语等回调中创建衍生物）
    /// </summary>
    public static class MinionFactoryCache
    {
        private static MinionFactory _factory;

        public static void SetFactory(MinionFactory factory)
        {
            _factory = factory;
        }

        public static MinionFactory GetFactory()
        {
            if (_factory == null)
                _factory = new MinionFactory();
            return _factory;
        }
    }
}
