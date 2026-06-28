namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 被动攻击加成接口
    /// </summary>
    public interface IPassiveAttackBonus
    {
        /// <summary>
        /// 获取被动攻击加成值
        /// </summary>
        int GetPassiveAttackBonus(Minion self, CombatState state);
    }
}
