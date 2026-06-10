using System.Collections.Generic;

namespace HBT
{

/// <summary>
/// Parser 输出的强类型事件基类
/// </summary>
public abstract class GameEvent { }

/// <summary>新对局开始（CREATE_GAME 无 TURN）</summary>
public class GameStartEvent : GameEvent
{
    public long GameSeed { get; set; }
    public ulong AccountIdLo { get; set; }
}

/// <summary>断线重连（CREATE_GAME 有 TURN）</summary>
public class ReconnectEvent : GameEvent { }

/// <summary>非酒馆对局，跳过</summary>
public class NotBgEvent : GameEvent { }

/// <summary>玩家信息（PlayerName）</summary>
public class PlayerInfoEvent : GameEvent
{
    public string PlayerTag { get; set; } = "";
    public string PlayerDisplayName { get; set; } = "";
}

/// <summary>英雄实体匹配</summary>
public class HeroEntityEvent : GameEvent
{
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
}

/// <summary>STEP 13 到达，主大厅信息就绪，可以 check-league</summary>
public class CheckLeagueEvent : GameEvent
{
    public string PlayerTag { get; set; } = "";
    public ulong AccountIdLo { get; set; }
    /// <summary>由 MainForm 在收到事件后填充（需要调 HearthMirror）</summary>
    public List<LobbyPlayer> LobbyPlayers { get; set; }
}

/// <summary>游戏结束（STATE=COMPLETE）</summary>
public class GameEndEvent : GameEvent
{
    public int Placement { get; set; }
    /// <summary>构筑模式：WON / LOST / CONCEDED</summary>
    public string PlayState { get; set; } = "";
    /// <summary>true=构筑模式</summary>
    public bool IsConstructed { get; set; }
}

/// <summary>投降</summary>
public class ConcedeEvent : GameEvent
{
    public int Placement { get; set; }
}

/// <summary>BG 战斗阶段开始（GameTag 2022 从 1 变为 0）</summary>
public class CombatStartEvent : GameEvent { }

/// <summary>BG 阵容快照时机（GameTag 3533 从 1 变为 0，战斗开始前）</summary>
public class BoardSnapshotEvent : GameEvent { }

/// <summary>构筑模式对局开始（GameType 确认后发出）</summary>
public class ConstructedGameStartEvent : GameEvent
{
    public long GameSeed { get; set; }
    public ulong AccountIdLo { get; set; }
}

/// <summary>构筑模式：双方数据就绪，可以 check-league</summary>
public class ConstructedCheckLeagueEvent : GameEvent
{
    public string PlayerTag { get; set; } = "";
    public ulong AccountIdLo { get; set; }
    public ulong OpponentAccountIdLo { get; set; }
    public string OpponentPlayerTag { get; set; } = "";
    public string OpponentDisplayName { get; set; } = "";
}

}
