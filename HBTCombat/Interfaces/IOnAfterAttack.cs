namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 攻击后触发接口
    /// </summary>
    public interface IOnAfterAttack
    {
        /// <summary>
        /// 攻击后触发（攻击者和目标都可实现）
        /// </summary>
        void OnAfterAttack(Minion self, Minion attacker, Minion target, CombatState state);
    }
}
