using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BattlegroundSpy;
using BattlegroundSpy.Objects;

namespace HBT.Services
{

/// <summary>
/// HearthMirror service wrapper - uses BattlegroundSpy for process memory reading.
/// Pure C# implementation, no native DLL dependencies.
/// </summary>
public class HearthMirrorService : IDisposable
{
    private readonly System.Threading.Timer _reconnectTimer;
    private bool _connected;
    private int _lastHsPid;
    private BattlegroundSpyReader _spy;
    private DateTime _lastPidChangeTime = DateTime.MinValue;
    private int _consecutiveNullCount;  // 连续返回 null 的次数

    public event Action OnConnected;
    public event Action OnDisconnected;

    public bool IsConnected => _connected;

    public HearthMirrorService()
    {
        _reconnectTimer = new System.Threading.Timer(CheckConnection, null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    /// <summary>检查连续 null 次数，超过阈值则销毁缓存的 _spy</summary>
    private void InvalidateSpyIfNeeded()
    {
        _consecutiveNullCount++;
        if (_consecutiveNullCount > 100)  // 连续 100 次返回 null，认为 _spy 已损坏
        {
            Console.WriteLine($"[HM] BGSpy 连续返回 null {_consecutiveNullCount} 次，销毁并重建");
            _spy?.Dispose();
            _spy = null;
            _connected = false;
            _consecutiveNullCount = 0;
        }
    }

    private BattlegroundSpyReader GetSpy()
    {
        if (_spy == null)
        {
            var hsProcesses = Process.GetProcessesByName("Hearthstone");
            if (hsProcesses.Length == 0)
                throw new InvalidOperationException("Hearthstone not running");
            try
            {
                _spy = new BattlegroundSpyReader(hsProcesses[0].Id);
            }
            catch (Exception ex)
            {
                // Mono 未就绪，抛出让调用方重试（不修改 _connected 状态）
                throw new InvalidOperationException($"BGSpy init failed (Mono not ready?): {ex.Message}", ex);
            }
        }
        return _spy;
    }

    private void CheckConnection(object? state)
    {
        try
        {
            var hsProcesses = Process.GetProcessesByName("Hearthstone");
            if (hsProcesses.Length == 0)
            {
                if (_connected)
                {
                    _connected = false;
                    _spy?.Dispose();
                    _spy = null;
                    OnDisconnected?.Invoke();
                    Console.WriteLine("[HM] Hearthstone not running, disconnected");
                }
                return;
            }

            var currentPid = hsProcesses[0].Id;
            if (currentPid != _lastHsPid)
            {
                _lastHsPid = currentPid;
                _spy?.Dispose();
                _spy = null;
                _lastPidChangeTime = DateTime.UtcNow;
                if (_connected)
                {
                    _connected = false;
                    OnDisconnected?.Invoke();
                }
                Console.WriteLine($"[HM] Detected Hearthstone PID={currentPid}");
            }

            // PID 变化后延迟 5 秒再尝试初始化，给 Mono 启动时间
            if (!_connected && (DateTime.UtcNow - _lastPidChangeTime).TotalSeconds < 5)
                return;

            // 主动尝试初始化 BGSpy，只有成功才标记为已连接
            if (!_connected)
            {
                try
                {
                    GetSpy();
                    _connected = true;
                    OnConnected?.Invoke();
                    Console.WriteLine("[HM] Connected (BGSpy initialized)");
                }
                catch (Exception ex)
                {
                    // Mono 未就绪，等下次重试
                    Console.WriteLine($"[HM] BGSpy init failed, retrying: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HM] Connection check error: {ex.Message}");
        }
    }

    /// <summary>Get player BattleTag</summary>
    public BattleTag GetBattleTag()
    {
        try
        {
            var result = GetSpy().GetBattleTag();
            if (result == null) InvalidateSpyIfNeeded();
            else _consecutiveNullCount = 0;  // 成功读取，重置计数
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HM] GetBattleTag failed: {ex.Message}");
            InvalidateSpyIfNeeded();
            return null;
        }
    }

    /// <summary>Get player AccountId</summary>
    public AccountId GetAccountId()
    {
        try { return GetSpy().GetAccountId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetAccountId failed: {ex.Message}"); return null; }
    }

    /// <summary>Get match info</summary>
    public MatchInfo GetMatchInfo()
    {
        try { return GetSpy().GetMatchInfo(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetMatchInfo failed: {ex.Message}"); return null; }
    }

    /// <summary>Get game server address and port</summary>
    public (string address, uint port)? GetServerInfo()
    {
        try { return GetSpy().GetServerInfo(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetServerInfo failed: {ex.Message}"); return null; }
    }

    /// <summary>Get player rankings from leaderboard memory</summary>
    public List<(string heroCardId, int placement, bool isDead, int teamId, int playerId)> GetPlayerRankings()
    {
        try { return GetSpy().GetPlayerRankings(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetPlayerRankings failed: {ex.Message}"); return new List<(string, int, bool, int, int)>(); }
    }

    /// <summary>Get hovered leaderboard player's hero card ID</summary>
    public string GetLeaderboardHoveredHeroCardId()
    {
        try { return GetSpy().GetLeaderboardHoveredHeroCardId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetLeaderboardHoveredHeroCardId failed: {ex.Message}"); return null; }
    }

    /// <summary>Get hovered leaderboard player's EntityId</summary>
    public int GetLeaderboardHoveredEntityId()
    {
        try { return GetSpy().GetLeaderboardHoveredEntityId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetLeaderboardHoveredEntityId failed: {ex.Message}"); return 0; }
    }

    /// <summary>Get hovered leaderboard player's PLAYER_ID (tag 2)</summary>
    public int GetLeaderboardHoveredPlayerId()
    {
        try { return GetSpy().GetLeaderboardHoveredPlayerId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetLeaderboardHoveredPlayerId failed: {ex.Message}"); return 0; }
    }

    /// <summary>Get opponent's PLAYER_ID directly from ZONE_PLAY hero entity</summary>
    public int GetOpponentPlayerIdInPlay()
    {
        try { return GetSpy().GetOpponentPlayerIdInPlay(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetOpponentPlayerIdInPlay failed: {ex.Message}"); return 0; }
    }

    /// <summary>Get opponent hero's EntityId from m_entityMap</summary>
    public int GetOpponentEntityId()
    {
        try { return GetSpy().GetOpponentEntityId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetOpponentEntityId failed: {ex.Message}"); return 0; }
    }

    /// <summary>Get opponent hero's cardId from m_entityMap</summary>
    public string GetOpponentHeroCardId()
    {
        try { return GetSpy().GetOpponentHeroCardId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetOpponentHeroCardId failed: {ex.Message}"); return null; }
    }

    /// <summary>Get BG lobby info (8 players)</summary>
    public BattlegroundsLobbyInfo GetBattlegroundsLobbyInfo()
    {
        try { return GetSpy().GetBattlegroundsLobbyInfo(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetBattlegroundsLobbyInfo failed: {ex.Message}"); return null; }
    }

    /// <summary>Get BG hero pick options</summary>
    public System.Collections.Generic.List<NameCardId> GetBattlegroundsHeroOptions()
    {
        try { return GetSpy().GetBattlegroundsHeroOptions(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetBattlegroundsHeroOptions failed: {ex.Message}"); return null; }
    }

    /// <summary>Get BG rating info</summary>
    public BattlegroundRatingInfo GetBattlegroundRatingInfo()
    {
        try { return GetSpy().GetBattlegroundRatingInfo(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetBattlegroundRatingInfo failed: {ex.Message}"); return null; }
    }

    /// <summary>Get current turn number</summary>
    public int? GetTurnNumber()
    {
        try { return GetSpy().GetTurnNumber(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetTurnNumber failed: {ex.Message}"); return null; }
    }

    /// <summary>Get local player's Controller ID</summary>
    public int? GetLocalControllerIdPublic()
    {
        try { return GetSpy().GetLocalControllerIdPublic(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetLocalControllerIdPublic failed: {ex.Message}"); return null; }
    }

    /// <summary>Get available races</summary>
    public System.Collections.Generic.List<int> GetAvailableRaces()
    {
        try { return GetSpy().GetAvailableBattlegroundsRaces(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetAvailableRaces failed: {ex.Message}"); return null; }
    }

    /// <summary>Get opponent board state</summary>
    public OpponentBoardState GetOpponentBoardState()
    {
        try { return GetSpy().GetOpponentBoardState(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetOpponentBoardState failed: {ex.Message}"); return null; }
    }

    /// <summary>Get next opponent's hero card ID (from NEXT_OPPONENT_PLAYER_ID tag)</summary>
    public string GetNextOpponentHeroCardId()
    {
        try { return GetSpy().GetNextOpponentHeroCardId(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetNextOpponentHeroCardId failed: {ex.Message}"); return null; }
    }

    /// <summary>Get Duos teammate board state</summary>
    public BattlegroundsTeammateBoardState GetTeammateBoardState()
    {
        try { return GetSpy().GetBattlegroundsTeammateBoardState(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetTeammateBoardState failed: {ex.Message}"); return null; }
    }

    /// <summary>Get detailed board minions (UnitySpy addition)</summary>
    public System.Collections.Generic.List<BoardMinion> GetPlayerBoardMinions()
    {
        try { return GetSpy().GetPlayerBoardMinions(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetPlayerBoardMinions failed: {ex.Message}"); return null; }
    }

    /// <summary>Get player trinkets</summary>
    public System.Collections.Generic.List<string> GetPlayerTrinkets()
    {
        try { return GetSpy().GetPlayerTrinkets(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetPlayerTrinkets failed: {ex.Message}"); return null; }
    }

    /// <summary>Get player trinkets with detailed info (debug)</summary>
    public System.Collections.Generic.List<string> GetPlayerTrinketsDetailed()
    {
        try { return GetSpy().GetPlayerTrinketsDetailed(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetPlayerTrinketsDetailed failed: {ex.Message}"); return null; }
    }

    /// <summary>Get local player's hero entity ID from memory</summary>
    public int GetLocalHeroEntityId()
    {
        try { return GetSpy().GetLocalHeroEntityId(); }
        catch { return 0; }
    }

    /// <summary>Get anomaly DBF ID</summary>
    public int GetAnomalyDbfId()
    {
        try { return GetSpy().GetAnomalyDbfId(); }
        catch { return 0; }
    }

    /// <summary>Get BG combat damage cap (tag 2089, enabled by tag 3403)</summary>
    public int GetDamageCap()
    {
        try { return GetSpy().GetDamageCap(); }
        catch { return 0; }
    }

    /// <summary>Get BG rating change data (post-game)</summary>
    public RatingChangeData GetBaconRatingChangeData()
    {
        try { return GetSpy().GetBaconRatingChangeData(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetBaconRatingChangeData failed: {ex.Message}"); return null; }
    }

    /// <summary>Check if game is over</summary>
    public bool GetGameOver()
    {
        try { return GetSpy().IsGameOver(); }
        catch { return false; }
    }

    /// <summary>Get game summary (UnitySpy addition)</summary>
    public GameSummary GetGameSummary()
    {
        try { return GetSpy().GetGameSummary(); }
        catch (Exception ex) { Console.WriteLine($"[HM] GetGameSummary failed: {ex.Message}"); return null; }
    }

    /// <summary>读取 SceneMgr.s_instance.m_mode（场景模式）</summary>
    public int? GetSceneMode()
    {
        try { return GetSpy().GetSceneMode(); }
        catch { return null; }
    }

    /// <summary>检测好友列表是否打开</summary>
    public bool IsFriendsListVisible()
    {
        try { return GetSpy().IsFriendsListVisible(); }
        catch { return false; }
    }

    public void Dispose()
    {
        _reconnectTimer?.Dispose();
        _spy?.Dispose();
    }
}
}
