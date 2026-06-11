namespace HBTCombat
{

/// <summary>
/// 模拟结果
/// </summary>
public class SimulationResult
{
    public float WinRate;
    public float TieRate;
    public float LossRate;
    public int SimulationCount;

    // 获胜时造成的伤害范围
    public int PlayerDamageMin;
    public int PlayerDamageMax;
    public float PlayerDamageAvg;

    // 失败时受到的伤害范围
    public int OpponentDamageMin;
    public int OpponentDamageMax;
    public float OpponentDamageAvg;
}

}
