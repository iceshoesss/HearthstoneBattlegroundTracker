namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 被动生命加成接口
    /// </summary>
    public interface IPassiveHealthBonus
    {
        /// <summary>
        /// 获取被动生命加成值
        /// </summary>
        int GetPassiveHealthBonus(Minion self, CombatState state);
    }
}
