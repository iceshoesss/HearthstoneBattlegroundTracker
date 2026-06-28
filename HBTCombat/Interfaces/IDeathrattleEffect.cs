namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 亡语效果接口 - 非召唤类亡语（buff/debuff/伤害等）
    /// </summary>
    public interface IDeathrattleEffect
    {
        /// <summary>
        /// 触发亡语效果
        /// </summary>
        void Trigger(Minion source, CombatState state);
    }
}
