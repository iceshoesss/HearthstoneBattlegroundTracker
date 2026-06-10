using System.Collections.Generic;

namespace HBT.Services.Combat
{

/// <summary>
/// 战斗模拟输入
/// </summary>
public class SimulationInput
{
    public List<SimMinion> PlayerBoard { get; set; } = new List<SimMinion>();
    public List<SimMinion> OpponentBoard { get; set; } = new List<SimMinion>();
    public int PlayerHealth { get; set; }
    public int OpponentHealth { get; set; }
    public int PlayerTier { get; set; }
    public int OpponentTier { get; set; }
    public int Turn { get; set; }
    public int DamageCap { get; set; }  // 0 = 无上限
}

}
