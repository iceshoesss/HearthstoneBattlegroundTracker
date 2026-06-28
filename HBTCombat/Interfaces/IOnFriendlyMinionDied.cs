namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 友方随从死亡触发接口
    /// </summary>
    public interface IOnFriendlyMinionDied
    {
        /// <summary>
        /// 当友方随从死亡时触发
        /// </summary>
        void OnFriendlyMinionDied(Minion self, Minion dead, CombatState state);
    }
}
