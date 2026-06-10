using System.Collections.Generic;

namespace HBT
{

/// <summary>
/// 一个英雄实体
/// </summary>
public class GameHero
{
    public int EntityId { get; set; }
    public string HeroName { get; set; } = "";
    public string CardId { get; set; } = "";
    public int PlayerSlot { get; set; }
    public int Placement { get; set; }
    public bool Eliminated { get; set; }
}

/// <summary>
/// 大厅玩家（HearthMirror 读取）
/// </summary>
public class LobbyPlayer
{
    public ulong Lo { get; set; }
    public string HeroCardId { get; set; } = "";
    public string HeroName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

/// <summary>
/// 一局游戏的状态
/// </summary>
public class Game
{
    // 玩家信息
    public int LocalPlayerId { get; set; }  // PlayerID（1 或 2），用于 FULL_ENTITY 的 player 匹配
    public string PlayerTag { get; set; } = "";
    public string PlayerDisplayName { get; set; } = "";
    public ulong AccountIdLo { get; set; }

    // 对局标识
    public long GameSeed { get; set; }
    public string GameUuid { get; set; } = "";   // HearthMirror 提供

    // 英雄信息
    public int HeroEntityId { get; set; }
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
    public int HeroPlacement { get; set; }

    // 构筑模式：对手信息
    public string OpponentPlayerTag { get; set; } = "";
    public string OpponentDisplayName { get; set; } = "";
    public ulong OpponentAccountIdLo { get; set; }
    public string OpponentHeroName { get; set; } = "";
    public string OpponentHeroCardId { get; set; } = "";

    // 所有英雄，key = (cardId, playerSlot)
    public Dictionary<(string, int), GameHero> AllHeroes { get; set; } = new Dictionary<(string, int), GameHero>();

    // 游戏状态
    public bool IsActive { get; set; }
    public bool IsLeagueGame { get; set; }
    public bool Reconnected { get; set; }
    public bool Conceded { get; set; }
    public bool PlacementConfirmed { get; set; }
    public string PlayState { get; set; } = "";  // WON/LOST/CONCEDED/DRAW
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";

    // 断线重连追踪
    public List<string> ReconnectTimes { get; set; } = new List<string>();

    // HearthMirror 对手信息
    public List<LobbyPlayer> LobbyPlayers { get; set; } = new List<LobbyPlayer>();

    // BGSpy 数据
    public int Rating { get; set; }
}
}
