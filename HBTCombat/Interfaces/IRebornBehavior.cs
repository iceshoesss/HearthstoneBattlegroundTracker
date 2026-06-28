namespace HBTCombat.Interfaces
{
    /// <summary>
    /// 自定义复生行为接口（默认复生为 1HP，某些随从可自定义）
    /// </summary>
    public interface IRebornBehavior
    {
        /// <summary>
        /// 执行复生，返回复生后的随从（返回 null 表示不复生）
        /// </summary>
        Minion OnReborn(Minion self);
    }
}
