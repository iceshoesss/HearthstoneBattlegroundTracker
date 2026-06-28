namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 友方随从召唤触发接口
    /// </summary>
    public interface IOnFriendlyMinionSummoned
    {
        /// <summary>
        /// 当友方随从被召唤时触发
        /// </summary>
        void OnFriendlyMinionSummoned(Minion self, Minion summoned, CombatState state);
    }
}
