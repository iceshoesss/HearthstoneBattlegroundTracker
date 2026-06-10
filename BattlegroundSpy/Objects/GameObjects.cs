using System.Collections.Generic;

namespace BattlegroundSpy.Objects
{

/// <summary>
/// Player's BattleTag (name + number).
/// </summary>
public class BattleTag
{
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    
    public override string ToString() => $"{Name}#{Number}";
}

/// <summary>
/// Player's account identifier.
/// </summary>
public class AccountId
{
    public ulong Hi { get; set; }
    public ulong Lo { get; set; }
}

/// <summary>
/// BG lobby information (8 players before game starts).
/// </summary>
public class BattlegroundsLobbyInfo
{
    public string GameUuid { get; set; } = "";
    public List<BattlegroundsLobbyPlayer> Players { get; set; } = new List<BattlegroundsLobbyPlayer>();
    /// <summary>playerId → AccountId.Lo 映射（从 m_playerMap 构建）</summary>
    public Dictionary<int, ulong> PlayerIdToLo { get; set; } = new Dictionary<int, ulong>();
}

/// <summary>
/// A single player in the BG lobby.
/// </summary>
public class BattlegroundsLobbyPlayer
{
    public AccountId AccountId { get; set; } = new AccountId();
    public string HeroCardId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Match information.
/// </summary>
public class MatchInfo
{
    public PlayerInfo LocalPlayer { get; set; }
    public PlayerInfo OpposingPlayer { get; set; }
    public int GameType { get; set; }
    public int FormatType { get; set; }
    public bool Spectator { get; set; }

    public class PlayerInfo
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public AccountId AccountId { get; set; }
        public BattleTag BattleTag { get; set; }
        public int StandardRank { get; set; }
        public int WildRank { get; set; }
    }
}

/// <summary>
/// BG rating information.
/// </summary>
public class BattlegroundRatingInfo
{
    public int Rating { get; set; }
    public int DuosRating { get; set; }
}

/// <summary>
/// Rating change data.
/// </summary>
public class RatingChangeData
{
    public int OldRating { get; set; }
    public int NewRating { get; set; }
    public int Change => NewRating - OldRating;
}

/// <summary>
/// BG hero option for hero picking.
/// </summary>
public class NameCardId
{
    public string Name { get; set; } = "";
    public string CardId { get; set; } = "";
}

/// <summary>
/// Selected BG game mode.
/// </summary>
public enum SelectedBattlegroundsGameMode
{
    Standard = 0,
    Duos = 1,
}

/// <summary>
/// BG player on the leaderboard (in-game).
/// </summary>
public class BattlegroundsPlayer
{
    public int Id { get; set; }
    public string CardId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Health { get; set; }
    public int Armor { get; set; }
    public int Damage { get; set; }
    public int LeaderboardPosition { get; set; }
    public int TechLevel { get; set; }
    public int TriplesCount { get; set; }
    public int WinStreak { get; set; }
    public bool IsDead { get; set; }
    public List<BattlegroundsBattle> Battles { get; set; }
}

/// <summary>
/// A battle in the combat history.
/// </summary>
public class BattlegroundsBattle
{
    public int PlayerId { get; set; }
    public int OpponentId { get; set; }
    public int Damage { get; set; }
    public bool IsDefeated { get; set; }
}

/// <summary>
/// Board card (opponent's board state).
/// </summary>
public class BoardCard
{
    public string CardId { get; set; } = "";
    public int? EntityId { get; set; }
    public int ZonePosition { get; set; }
    public bool Hovered { get; set; }
    public int Attack { get; set; }
    public int Health { get; set; }
    public bool Golden { get; set; }
    public bool Taunt { get; set; }
    public bool DivineShield { get; set; }
    public bool Poisonous { get; set; }
    public bool Venomous { get; set; }
    public bool Windfury { get; set; }
    public bool Reborn { get; set; }
    public bool Stealth { get; set; }
    public bool Deathrattle { get; set; }
    public int TechLevel { get; set; }
}

/// <summary>
/// Opponent board state.
/// </summary>
public class OpponentBoardState
{
    public int MousedOverSlot { get; set; }
    public string HeroCardId { get; set; }  // 当前对手的英雄卡牌ID
    public int PlayerId { get; set; }       // 对手的 PLAYER_ID (tag 2)，唯一标识
    public int ControllerId { get; set; }   // 对手的 CONTROLLER ID (tag 50)
    public List<BoardCard> BoardCards { get; set; } = new List<BoardCard>();
}

/// <summary>
/// Duos teammate board state.
/// </summary>
public class BattlegroundsTeammateBoardState
{
    public bool ViewingTeammate { get; set; }
    public List<string> MulliganHeroes { get; set; }
    public List<BattlegroundsTeammateBoardStateEntity> Entities { get; set; } = new List<BattlegroundsTeammateBoardStateEntity>();
}

/// <summary>
/// An entity on the teammate's board.
/// </summary>
public class BattlegroundsTeammateBoardStateEntity
{
    public string CardId { get; set; } = "";
    public Dictionary<int, int> Tags { get; set; } = new Dictionary<int, int>();
}

// ═══════════════════════════════════════
//  UnitySpy 补充的数据模型
// ═══════════════════════════════════════

/// <summary>
/// Detailed minion on board (UnitySpy addition).
/// </summary>
public class BoardMinion
{
    public string CardId { get; set; } = "";
    public int Attack { get; set; }
    public int Health { get; set; }
    public int MaxAttack { get; set; }
    public int MaxHealth { get; set; }
    public int ZonePosition { get; set; }
    public bool Golden { get; set; }
    public bool Taunt { get; set; }
    public bool DivineShield { get; set; }
    public bool Poisonous { get; set; }
    public bool Venomous { get; set; }
    public bool Windfury { get; set; }
    public bool Reborn { get; set; }
    public bool Stealth { get; set; }
    public bool Deathrattle { get; set; }
    public int TechLevel { get; set; }
    public int EntityId { get; set; }
    public Dictionary<int, int> Tags { get; set; } = new Dictionary<int, int>();
}

/// <summary>
/// Hero power state (UnitySpy addition).
/// </summary>
public class HeroPowerState
{
    public string CardId { get; set; } = "";
    public bool Activated { get; set; }
    public int Data1 { get; set; }
    public int Data2 { get; set; }
}

/// <summary>
/// Current game state summary (UnitySpy addition).
/// </summary>
public class GameSummary
{
    public int CurrentTurn { get; set; }
    public int PlayerHealth { get; set; }
    public int PlayerArmor { get; set; }
    public int PlayerTavernTier { get; set; }
    public int PlayerGold { get; set; }
    public int DamageCap { get; set; }
    public List<BoardMinion> PlayerBoard { get; set; } = new List<BoardMinion>();
    public List<BoardMinion> PlayerHand { get; set; } = new List<BoardMinion>();
    public HeroPowerState HeroPower { get; set; }
}
}
