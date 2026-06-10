using System;
using System.Collections.Generic;

namespace HBT
{

/// <summary>
/// 持久化的对局记录（写入 games.json）
/// </summary>
public class GameRecord
{
    public string BattleTag { get; set; } = "";
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
    public int Placement { get; set; }
    public int Points { get; set; }
    public int Rating { get; set; }
    public int RatingAfter { get; set; }
    public int RatingChange { get; set; }
    public string GameUuid { get; set; } = "";
    public string Mode { get; set; } = "";       // "solo"/"duo"/"standard"/"wild"
    public string Timestamp { get; set; } = "";  // ISO 8601 UTC
}

}
