namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 复仇接口 - 友方随从死亡后计数触发
    /// </summary>
    public interface IAvenge
    {
        /// <summary>
        /// 复仇需要的死亡计数
        /// </summary>
        int AvengeCount { get; }

        /// <summary>
        /// 复仇触发时执行
        /// </summary>
        void OnAvenge(Minion self, CombatState state);
    }
}
