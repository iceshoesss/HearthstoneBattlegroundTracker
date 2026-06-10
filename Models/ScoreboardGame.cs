namespace HBT
{

/// <summary>
/// 记分板单局游戏数据
/// </summary>
public class ScoreboardGame
{
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
    public int Placement { get; set; }
    public int MMRBefore { get; set; }
    public int MMRAfter { get; set; }
}

}
