using System.Collections.Generic;

namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 亡语接口 - 随从死亡时触发
    /// </summary>
    public interface IDeathrattle
    {
        /// <summary>
        /// 触发亡语，返回召唤的随从列表
        /// </summary>
        List<Minion> TriggerDeathrattle(Minion source, bool golden);
    }
}
