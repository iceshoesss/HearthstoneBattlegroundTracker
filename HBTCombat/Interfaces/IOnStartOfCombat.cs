namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 战斗开始触发接口
    /// </summary>
    public interface IOnStartOfCombat
    {
        /// <summary>
        /// 战斗开始时触发
        /// </summary>
        void OnStartOfCombat(Minion self, CombatState state);
    }
}
