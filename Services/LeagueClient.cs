using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HBT
{

/// <summary>
/// 联赛逻辑编排。
/// 负责 check-league / update-placement 的 API 调用 + 重试。
/// 不知道 WinForms UI，不知道 GamePhase 状态机。
/// </summary>
public class LeagueClient
{
    readonly Config _config;

    public bool IsLeagueGame { get; set; }
    public string GameUuid { get; set; } = "";
    public string VerificationCode { get; private set; } = "";

    public Action OnStateChanged { get; set; }

    // 局数代际标记：新局递增，过期异步回调自动丢弃
    volatile int _gameGeneration;
    int GameGeneration => _gameGeneration;

    Timer _retryTimer;
    string? _pendingPlayerTag;
    ulong? _pendingAccountIdLo;
    List<LobbyPlayer> _pendingLobbyPlayers;
    string? _pendingRegion;
    string? _pendingMode;

    public LeagueClient(Config config)
    {
        _config = config;
    }

    /// <summary>新对局开始时调用</summary>
    public int OnGameStart()
    {
        StopRetry();
        IsLeagueGame = false;
        GameUuid = "";
        return ++_gameGeneration;
    }

    /// <summary>STEP 13 时调用，发起 check-league</summary>
    public void OnCheckLeague(string playerTag, ulong accountIdLo,
        List<LobbyPlayer> lobbyPlayers, string region, string mode,
        Func<bool> shouldRetry)
    {
        int generation = _gameGeneration;
        Task.Run(async () =>
        {
            for (int retry = 0; retry < 3 && shouldRetry(); retry++)
            {
                var ok = await ApiClient.CheckLeagueAsync(playerTag, accountIdLo, lobbyPlayers, region, mode, DateTime.UtcNow.ToString("o"));
                if (_gameGeneration != generation) return;
                if (ok == true)
                {
                    HandleCheckLeagueResult();
                    return;
                }
                else if (ok == false)
                {
                    if (!string.IsNullOrEmpty(ApiClient.ServerGameUuid))
                        GameUuid = ApiClient.ServerGameUuid;
                    if (!string.IsNullOrEmpty(ApiClient.VerificationCode))
                        VerificationCode = ApiClient.VerificationCode;
                    OnStateChanged?.Invoke();
                    return;
                }
                if (retry < 2) await Task.Delay(1000 * (retry + 1));
            }

            if (!shouldRetry()) return;

            Console.WriteLine("[API] ⚠️ check-league 初始重试失败，启动 15 秒周期重试...");
            _pendingPlayerTag = playerTag;
            _pendingAccountIdLo = accountIdLo;
            _pendingLobbyPlayers = lobbyPlayers;
            _pendingRegion = region;
            _pendingMode = mode;

            _retryTimer?.Dispose();
            _retryTimer = new Timer(async _ =>
            {
                try
                {
                    if (!shouldRetry() || _gameGeneration != generation)
                    {
                        StopRetry();
                        return;
                    }

                    var retryOk = await ApiClient.CheckLeagueAsync(
                        _pendingPlayerTag!, _pendingAccountIdLo!.Value, _pendingLobbyPlayers!,
                        _pendingRegion!, _pendingMode!, DateTime.UtcNow.ToString("o"));

                    if (_gameGeneration != generation)
                    {
                        Console.WriteLine("[API] ⏭️ 定时器回调已过期，丢弃结果");
                        StopRetry();
                        return;
                    }

                    if (retryOk == true)
                    {
                        HandleCheckLeagueResult();
                        StopRetry();
                        Console.WriteLine("[API] ✅ check-league 周期重试成功");
                    }
                    else if (retryOk == false)
                    {
                        if (!string.IsNullOrEmpty(ApiClient.VerificationCode))
                            VerificationCode = ApiClient.VerificationCode;
                        StopRetry();
                        OnStateChanged?.Invoke();
                    }
                    else
                    {
                        Console.WriteLine("[API] ⏳ check-league 重试失败，15 秒后继续...");
                    }
                }
                catch (ObjectDisposedException) { StopRetry(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API] ❌ check-league 定时重试异常: {ex.Message}");
                    StopRetry();
                }
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        });
    }

    /// <summary>构筑模式：双方数据就绪时调用，发起 check-league（不依赖 HearthMirror）</summary>
    public void OnCheckLeagueConstructed(
        string playerTag, ulong accountIdLo,
        ulong opponentAccountIdLo, string opponentPlayerTag,
        string opponentDisplayName,
        string localHeroCardId, string opponentHeroCardId,
        string region, string mode,
        Func<bool> shouldRetry)
    {
        int generation = _gameGeneration;
        var localDisplayName = playerTag.Contains("#")
            ? playerTag.Substring(0, playerTag.IndexOf("#")) : playerTag;

        Task.Run(async () =>
        {
            for (int retry = 0; retry < 3 && shouldRetry(); retry++)
            {
                var ok = await ApiClient.CheckLeagueAsync(
                    playerTag, accountIdLo,
                    opponentAccountIdLo,
                    localDisplayName, opponentDisplayName,
                    playerTag, opponentPlayerTag,
                    localHeroCardId, opponentHeroCardId,
                    region, mode, DateTime.UtcNow.ToString("o"));
                if (_gameGeneration != generation) return;
                if (ok == true)
                {
                    HandleCheckLeagueResult();
                    return;
                }
                else if (ok == false)
                {
                    if (!string.IsNullOrEmpty(ApiClient.ServerGameUuid))
                        GameUuid = ApiClient.ServerGameUuid;
                    if (!string.IsNullOrEmpty(ApiClient.VerificationCode))
                        VerificationCode = ApiClient.VerificationCode;
                    OnStateChanged?.Invoke();
                    return;
                }
                if (retry < 2) await Task.Delay(1000 * (retry + 1));
            }

            if (!shouldRetry()) return;

            Console.WriteLine("[API] ⚠️ check-league 初始重试失败，启动 15 秒周期重试...");
            _pendingPlayerTag = playerTag;
            _pendingAccountIdLo = accountIdLo;

            _retryTimer?.Dispose();
            _retryTimer = new Timer(async _ =>
            {
                try
                {
                    if (!shouldRetry() || _gameGeneration != generation)
                    {
                        StopRetry();
                        return;
                    }

                    var retryOk = await ApiClient.CheckLeagueAsync(
                        _pendingPlayerTag!, _pendingAccountIdLo!.Value,
                        opponentAccountIdLo,
                        localDisplayName, opponentDisplayName,
                        _pendingPlayerTag!, opponentPlayerTag,
                        localHeroCardId, opponentHeroCardId,
                        region, mode, DateTime.UtcNow.ToString("o"));

                    if (_gameGeneration != generation)
                    {
                        Console.WriteLine("[API] ⏭️ 定时器回调已过期，丢弃结果");
                        StopRetry();
                        return;
                    }

                    if (retryOk == true)
                    {
                        HandleCheckLeagueResult();
                        StopRetry();
                        Console.WriteLine("[API] ✅ check-league 周期重试成功");
                    }
                    else if (retryOk == false)
                    {
                        if (!string.IsNullOrEmpty(ApiClient.VerificationCode))
                            VerificationCode = ApiClient.VerificationCode;
                        StopRetry();
                        OnStateChanged?.Invoke();
                    }
                    else
                    {
                        Console.WriteLine("[API] ⏳ check-league 重试失败，15 秒后继续...");
                    }
                }
                catch (ObjectDisposedException) { StopRetry(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API] ❌ check-league 定时重试异常: {ex.Message}");
                    StopRetry();
                }
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        });
    }

    /// <summary>对局结束时异步上传排名</summary>
    public async Task<bool> OnGameEnd(string gameUuid, string playerTag,
        ulong accountIdLo, int placement, List<string> reconnectTimes,
        List<(ulong lo, int placement)> otherPlacements)
    {
        return await ApiClient.UpdatePlacementAsync(
            gameUuid, playerTag, accountIdLo, placement,
            reconnectTimes, otherPlacements);
    }

    /// <summary>重置状态（PID 变化时）</summary>
    public void Reset()
    {
        StopRetry();
        IsLeagueGame = false;
        GameUuid = "";
    }

    /// <summary>停止重试（不重置联赛状态）</summary>
    public void StopRetry()
    {
        _retryTimer?.Dispose();
        _retryTimer = null;
        _pendingPlayerTag = null;
        _pendingAccountIdLo = null;
        _pendingLobbyPlayers = null;
        _pendingRegion = null;
        _pendingMode = null;
    }

    void HandleCheckLeagueResult()
    {
        if (!string.IsNullOrEmpty(ApiClient.ServerGameUuid))
            GameUuid = ApiClient.ServerGameUuid;

        if (_config.TestMode)
        {
            Console.WriteLine("[API] [TEST] 强制标记为联赛对局");
            IsLeagueGame = true;
        }
        else if (ApiClient.LastLeagueResult)
            IsLeagueGame = true;

        if (!string.IsNullOrEmpty(ApiClient.VerificationCode))
            VerificationCode = ApiClient.VerificationCode;

        OnStateChanged?.Invoke();
    }
}

}
