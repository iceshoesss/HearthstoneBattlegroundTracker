using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BattlegroundSpy.Objects;
using HBT.Models;

namespace HBT.Services
{

/// <summary>
/// 游戏状态机
/// </summary>
public enum GamePhase { Idle, PreLobby, Lobby, Active, PostGame }

/// <summary>
/// 炉石场景模式（对应 SceneMgr.m_mode）
/// </summary>
public static class SceneMode
{
    public const int INVALID = 0;
    public const int STARTUP = 1;
    public const int LOGIN = 2;
    public const int HUB = 3;
    public const int GAMEPLAY = 4;
    public const int TOURNAMENT = 7;
    public const int BACON = 15;  // 酒馆战棋大厅
}

/// <summary>
/// 游戏监控服务
///
/// 架构（HDT 方案）：
/// - Power.log 解析：游戏状态主驱动（CREATE_GAME/STEP/STATE=COMPLETE）
/// - SceneWatcher：场景变化检测（UI 显示 + 辅助判断）
/// - BattlegroundSpy：内存数据读取（大厅信息、玩家信息）
/// </summary>
public class GameMonitorService : IDisposable
{
    private readonly Config _config;
    private readonly HearthMirrorService _hm;
    private readonly LeagueClient _league;
    private readonly EntityTracker _entityTracker = new EntityTracker();

    // 游戏状态
    private volatile GamePhase _phase = GamePhase.Idle;
    private volatile int _gameGeneration;
    private Game _currentGame = new Game();
    private GameState _gameState = new GameState();
    private readonly List<Game> _games = new List<Game>();
    private string _verifyCode = "";
    private string _currentGameUuid = "";
    private ulong _localPlayerHi;
    private ulong _localPlayerLo;
    private string _localPlayerBattleTag = "";
    private string _localPlayerDisplayName = "";
    private bool _playerNameReported;
    private bool _hmReady;
    private bool _scanning;
    private int _lastKnownMmr;
    private int _startMmr;
    private int _hsPid;
    private string _logPath = "";
    private bool _disposed;

    // teamId → Lo 映射（用于 update-placement 匹配）
    private readonly Dictionary<int, ulong> _teamIdToLo = new();
    // playerId → Lo 映射（从大厅 m_playerMap 构建，用于匹配排行榜）
    private Dictionary<int, ulong> _playerIdToLo = new();

    // 场景状态（仅用于 UI 显示和辅助判断）
    private int _lastSceneMode = SceneMode.INVALID;

    // 线程
    private Thread _sceneThread;
    private Thread _logThread;
    private volatile bool _running;

    // 事件
    public event Action<GamePhase, Game> OnPhaseChanged;
    public event Action<GameRecord> OnGameEnded;
    public event Action<string> OnVerifyCodeChanged;
    public event Action<string> OnLogMessage;
    public event Action<string> OnPlayerNameChanged;  // 格式: "玩家名#1234 (CN)"
    public event Action<int> OnMmrChanged;
    public event Action<int> OnSceneChanged;  // 场景变化（UI 显示用）

    public GamePhase Phase => _phase;
    public string VerifyCode => _verifyCode;
    public string PlayerName => _localPlayerBattleTag;
    public int CurrentScene => _lastSceneMode;
    public int LastScene { get; private set; } = SceneMode.INVALID;
    public int StartMmr => _startMmr;
    public int CurrentMmr => _lastKnownMmr;

    public GameMonitorService(Config config, HearthMirrorService hm)
    {
        _config = config;
        _hm = hm;
        ApiClient.Init(config.ApiBaseUrl);
        GameStore.Init();
        _league = new LeagueClient(config);
        _league.OnStateChanged = OnLeagueStateChanged;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        // SceneWatcher：场景变化检测（UI 显示 + 进程监控）
        _sceneThread = new Thread(SceneWatcherLoop) { IsBackground = true, Name = "SceneWatcher" };
        _sceneThread.Start();

        // LogWatcher：Power.log 解析（游戏状态主驱动）
        _logThread = new Thread(LogWatcherLoop) { IsBackground = true, Name = "LogWatcher" };
        _logThread.Start();
    }

    public void Stop() => _running = false;

    // ═══════════════════════════════════════════════════════════════
    //  SceneWatcher - 场景检测（UI 显示 + 进程监控，不驱动游戏状态）
    // ═══════════════════════════════════════════════════════════════

    private void SceneWatcherLoop()
    {
        while (_running)
        {
            Thread.Sleep(16);

            try
            {
                // 炉石未运行时跳过
                var hsProcs = Process.GetProcessesByName("Hearthstone");
                if (hsProcs.Length == 0)
                {
                    if (_lastSceneMode != SceneMode.INVALID)
                    {
                        _lastSceneMode = SceneMode.INVALID;
                        OnSceneChanged?.Invoke(SceneMode.INVALID);
                        // 炉石退出时重置状态
                        if (_phase != GamePhase.Idle)
                        {
                            Log("炉石进程退出，重置状态");
                            ResetToIdle();
                        }
                    }
                    continue;
                }

                var currentPid = hsProcs[0].Id;
                if (currentPid != _hsPid)
                {
                    _hsPid = currentPid;
                    _lastSceneMode = SceneMode.INVALID;
                    Log($"检测到炉石进程 PID={currentPid}");
                    // 进程重启，重置玩家信息以便重新获取
                    ResetPlayerInfo();
                }

                // 读取 SceneMgr.m_mode
                var mode = _hm.GetSceneMode();
                if (mode == null || mode == _lastSceneMode)
                    continue;

                var prevScene = _lastSceneMode;
                _lastSceneMode = mode.Value;
                LastScene = prevScene;
                OnSceneChanged?.Invoke(mode.Value);

                // ── 兜底：场景 4→15（游戏中→战棋大厅）──
                if (prevScene == SceneMode.GAMEPLAY && mode == SceneMode.BACON)
                {
                    if (_phase == GamePhase.Active && _currentGame.IsActive)
                    {
                        Log("检测到场景 4→15，等待菜单稳定...");
                        // 等待场景稳定在 15（最多 5 秒）
                        if (WaitForSceneStable(SceneMode.BACON, 5000))
                        {
                            ForceEndGame();
                        }
                    }
                }
            }
            catch
            {
                // 场景切换时 ReadProcessMemory 可能失败，静默重试
            }
        }

        Log("SceneWatcher 退出");
    }

    // ═══════════════════════════════════════════════════════════════
    //  LogWatcher - Power.log 解析（游戏状态主驱动）
    // ═══════════════════════════════════════════════════════════════

    private void LogWatcherLoop()
    {
        Log("LogWatcher 启动");

        LogWatcher watcher = null;
        int loopCount = 0;

        while (_running)
        {
            try
            {
                // 等待日志文件
                if (string.IsNullOrEmpty(_logPath))
                {
                    // 炉石未启动时跳过
                    if (Process.GetProcessesByName("Hearthstone").Length == 0)
                    {
                        Thread.Sleep(3000);
                        continue;
                    }

                    var logPath = LogPathFinder.Find();
                    if (logPath == null)
                    {
                        // 没有日志文件（未开始游戏），但仍需获取玩家信息
                        if (!_hmReady) TryFetchPlayerInfo();
                        Thread.Sleep(3000);
                        continue;
                    }

                    _logPath = logPath;
                    watcher = new LogWatcher(logPath);
                    Log($"找到日志: {Path.GetFileName(logPath)}");

                    // 先获取玩家信息
                    TryFetchPlayerInfo();

                    // BGSpy 检测当前游戏状态（重试最多 5 次，每次间隔 2 秒）
                    if (Process.GetProcessesByName("Hearthstone").Length > 0)
                    {
                        int? scene = null;
                        for (int retry = 0; retry < 5 && scene == null; retry++)
                        {
                            scene = _hm.GetSceneMode();
                            if (scene == null && retry < 4) Thread.Sleep(2000);
                        }

                        if (scene != null)
                        {
                            Log($"当前场景: {scene}");
                            if (scene == 4) // GAMEPLAY
                            {
                                if (LastScene == SceneMode.BACON)
                                {
                                    // 15→4，一定是 BG，不跳到末尾
                                    // 让 LogWatcher 从当前位置继续读，处理已有的 CREATE_GAME/STEP 事件
                                    Log("场景 15→4，等待 CheckLeagueEvent");
                                }
                                else
                                {
                                    // 中途启动，需确认游戏模式
                                    var mode = DetectCurrentGameMode();
                                    if (mode == "BG")
                                    {
                                        _currentGame.IsActive = true;

                                        // 中途启动：从上一局结束标记开始读取，填充 EntityTracker
                                        var startOffset = watcher.FindGameStartOffset();
                                        watcher.SetPosition(startOffset);
                                        Log($"中途启动 BG，从位置 {startOffset} 读取填充 EntityTracker...");
                                        var initialLines = watcher.TryReadLines();
                                        if (initialLines != null && initialLines.Length > 0)
                                        {
                                            foreach (var line in initialLines)
                                            {
                                                if (!line.Contains("GameState.") && !line.Contains("PowerTaskList."))
                                                    continue;
                                                // 只让 Parser 填充 EntityTracker，不触发游戏事件
                                                var (_, newState, newGame) = Parser.ProcessLine(line, _currentGame, _gameState, _games, _entityTracker);
                                                _gameState = newState;
                                                _currentGame = newGame;
                                            }
                                            Log($"EntityTracker: {_entityTracker.Entities.Count} 个实体");
                                        }

                                        HandleCheckLeague();
                                    }
                                    else
                                    {
                                        Log($"场景4但非战棋模式({mode ?? "未知"})，跳过");
                                    }
                                    // 跳到末尾避免重复处理
                                    watcher.SetPosition(new FileInfo(logPath).Length);
                                }
                            }
                            else
                            {
                                // 非 GAMEPLAY 场景，跳到末尾
                                watcher.SetPosition(new FileInfo(logPath).Length);
                            }
                        }
                        else
                        {
                            Log("场景读取失败，等待日志事件...");
                            // 场景读取失败，跳到末尾
                            watcher.SetPosition(new FileInfo(logPath).Length);
                        }
                    }
                    else
                    {
                        // 炉石未运行，跳到末尾
                        watcher.SetPosition(new FileInfo(logPath).Length);
                    }
                }

                if (!_hmReady && Process.GetProcessesByName("Hearthstone").Length > 0)
                    TryFetchPlayerInfo();

                // 检查日志文件变化
                loopCount++;
                if (loopCount % 100 == 0 && !string.IsNullOrEmpty(_logPath))
                {
                    var newPath = LogPathFinder.CheckNewLogFile(_logPath);
                    if (newPath != null)
                    {
                        _logPath = newPath;
                        watcher = new LogWatcher(newPath);
                        Log($"日志文件切换: {Path.GetFileName(newPath)}");
                    }
                }

                if (watcher == null) { Thread.Sleep(1000); continue; }

                // 读取新日志行
                var lines = watcher.TryReadLines();
                if (lines == null || lines.Length == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                foreach (var line in lines)
                {
                    if (!line.Contains("GameState.") && !line.Contains("PowerTaskList."))
                        continue;

                    var (evt, newState, newGame) = Parser.ProcessLine(line, _currentGame, _gameState, _games, _entityTracker);
                    _gameState = newState;
                    _currentGame = newGame;

                    if (evt != null)
                        HandleParserEvent(evt);
                }
            }
            catch (Exception ex)
            {
                Log($"LogWatcher 异常: {ex.Message}");
                Thread.Sleep(1000);
            }
        }

        Log("LogWatcher 退出");
    }

    // ═══════════════════════════════════════
    //  Parser 事件处理（游戏状态主驱动）
    // ═══════════════════════════════════════

    private void HandleParserEvent(GameEvent evt)
    {
        // 扫描模式：只更新 Parser 状态，不触发 UI 和 API
        if (_scanning) return;

        switch (evt)
        {
            case GameStartEvent:
                // Power.log 检测到 CREATE_GAME + GameType=BG → 游戏开始
                _gameGeneration++;
                _league.Reset();
                _currentGameUuid = "";
                SetPhase(GamePhase.PreLobby);
                if (!string.IsNullOrEmpty(_currentGame.HeroName))
                    Log($"新对局开始 - 英雄: {_currentGame.HeroName}");
                else
                    Log("新对局开始");
                break;

            case ReconnectEvent:
                _league.StopRetry();
                // 注意：重连时不清空 EntityTracker，因为游戏仍在进行中，现有数据仍然有效
                // EntityTracker 会在新游戏开始时（GameStartEvent）被清空

                // 参考团子版：重连状态管理
                DisconnectService.ReconnectCount++;
                Log($"断线重连 (ReconnectCount={DisconnectService.ReconnectCount})");

                if (_gameState.IsConstructed)
                {
                    Log("断线重连 - 构筑模式，跳过BG流程");
                    DisconnectService.EndReconnect();
                }
                else if (!string.IsNullOrEmpty(_currentGameUuid))
                {
                    SetPhase(GamePhase.Active);
                    DisconnectService.EndReconnect();
                    Log("断线重连 - 已有联赛对局");
                    // EntityTracker 数据仍然有效，无需重建映射
                }
                else if (_currentGame.LobbyPlayers.Count == 0)
                {
                    SetPhase(GamePhase.PreLobby);
                    DisconnectService.EndReconnect();
                    Log("断线重连 - 获取大厅信息");
                    HandleCheckLeague();
                }
                else
                {
                    // 已有大厅信息（BGSpy 启动时获取），跳过重复获取
                    DisconnectService.EndReconnect();
                    Log("断线重连 - 已有大厅信息，跳过");
                }
                break;

            case CheckLeagueEvent:
                // STEP MAIN_CLEANUP → 从内存获取大厅信息 → 联赛检查
                // 构筑模式不走BG流程
                if (_gameState.IsConstructed) break;
                // 如果英雄信息为空（启动时场景4立即调用但英雄未就绪），也允许重新获取
                if (_phase == GamePhase.PreLobby || _phase == GamePhase.Idle
                    || string.IsNullOrEmpty(_currentGame.HeroCardId))
                    HandleCheckLeague();
                break;

            case GameEndEvent endEvt:
                HandleGameEnd(endEvt);
                break;

            case ConcedeEvent:
                HandleGameEnd(new GameEndEvent { Placement = 8 });
                break;
        }
    }

    // ═══════════════════════════════════════
    //  check-league（STEP MAIN_CLEANUP 触发）
    // ═══════════════════════════════════════

    private void HandleCheckLeague()
    {
        Log("获取大厅玩家信息...");

        // 大厅信息：带重试（英雄信息可能延迟填充）
        BattlegroundsLobbyInfo lobbyInfo = null;
        for (int retry = 0; retry < 6; retry++)
        {
            lobbyInfo = _hm.GetBattlegroundsLobbyInfo();
            if (lobbyInfo != null && lobbyInfo.Players.Count > 0)
            {
                // 检查是否有英雄信息（m_playerHero 可能还没填充）
                var hasHeroes = lobbyInfo.Players.Any(p => !string.IsNullOrEmpty(p.HeroCardId));
                if (hasHeroes) break;
                if (retry < 5)
                {
                    Log($"大厅信息已获取但英雄为空，500ms 后重试({retry + 1}/6)...");
                    Thread.Sleep(500);
                    continue;
                }
            }
            if (retry < 5)
            {
                Log($"大厅信息为空，500ms 后重试({retry + 1}/6)...");
                Thread.Sleep(500);
            }
        }

        if (lobbyInfo == null || lobbyInfo.Players.Count == 0)
        {
            Log("无法获取大厅玩家信息（已重试6次）");
            return;
        }

        var players = new List<LobbyPlayer>();
        foreach (var p in lobbyInfo.Players)
        {
            var heroName = HeroNameResolver.Resolve(p.HeroCardId ?? "");
            players.Add(new LobbyPlayer
            {
                Lo = p.AccountId?.Lo ?? 0,
                HeroCardId = p.HeroCardId ?? "",
                HeroName = heroName,
                DisplayName = p.Name ?? "",
            });

            if (_localPlayerLo == 0 && p.AccountId?.Lo != 0
                && !string.IsNullOrEmpty(_localPlayerDisplayName)
                && string.Equals(p.Name?.Trim(), _localPlayerDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _localPlayerLo = p.AccountId?.Lo ?? 0;
            }
        }

        _currentGame.LobbyPlayers = players;

        // 保存 playerId → Lo 映射（从 m_playerMap 构建，用于匹配排行榜）
        _playerIdToLo = lobbyInfo.PlayerIdToLo ?? new Dictionary<int, ulong>();
        Console.WriteLine($"[Mapping] playerId→Lo: {_playerIdToLo.Count} entries");

        // 构建 teamId → Lo 映射（延迟到首次获取排名时构建）
        // 此时 LobbyPlayers 已就绪，等 GetPlayerRankings() 返回后再匹配
        _teamIdToLo.Clear();

        // 从大厅数据设置英雄信息（BGSpy 优先于 Parser 的 HERO_ENTITY 匹配）
        {
            var me = players.FirstOrDefault(p =>
                p.Lo == _localPlayerLo
                || (!string.IsNullOrEmpty(_localPlayerDisplayName)
                    && string.Equals(p.DisplayName?.Trim(), _localPlayerDisplayName.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (me != null)
            {
                _currentGame.HeroName = me.HeroName;
                _currentGame.HeroCardId = me.HeroCardId;
                Log($"英雄: {me.HeroName} ({me.HeroCardId})");

                // 补充 HeroEntityId（中途启动时 Parser 可能未匹配到 HERO_ENTITY）
                if (_currentGame.HeroEntityId == 0)
                {
                    var heroEntityId = _hm.GetLocalHeroEntityId();
                    if (heroEntityId > 0)
                    {
                        _currentGame.HeroEntityId = heroEntityId;
                        Log($"HeroEntityId: {heroEntityId} (从内存补充)");
                    }
                }
            }
        }

        // 英雄信息已设置，触发 UI 更新
        SetPhase(GamePhase.Lobby);

        // 每局开始时读取 MMR
        if (_lastKnownMmr == 0)
            TryReadMmr();

        // 记录起始 MMR（记分板用）
        if (_lastKnownMmr > 0 && _startMmr == 0)
            _startMmr = _lastKnownMmr;

        if (_localPlayerLo == 0)
        {
            for (int i = 0; i < 30 && _localPlayerLo == 0; i++)
                Thread.Sleep(100);
        }

        if (_localPlayerLo == 0)
        {
            Log("无法识别本地玩家 AccountId.Lo");
            return;
        }

        Log($"联赛检查中... ({players.Count} 名玩家)");
        _league.OnCheckLeague(
            _localPlayerBattleTag, _localPlayerLo,
            players, _config.Region, _config.Mode,
            () => _phase == GamePhase.Lobby);
    }

    // ═══════════════════════════════════════════════════════════════
    //  兜底：场景 4→15 强制结束
    // ═══════════════════════════════════════

    /// <summary>
    /// 等待场景稳定在目标值（连续 N 次读取不变）
    /// </summary>
    private bool WaitForSceneStable(int targetScene, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastScene = targetScene;
        var stableCount = 0;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(200);
            try
            {
                var scene = _hm.GetSceneMode();
                if (scene == null) continue;

                if (scene.Value == targetScene)
                {
                    stableCount++;
                    if (stableCount >= 3)  // 连续 3 次（600ms）稳定
                    {
                        Log($"场景已稳定在 {targetScene}");
                        return true;
                    }
                }
                else
                {
                    // 场景又变了，重置计数
                    stableCount = 0;
                    lastScene = scene.Value;
                }
            }
            catch { }
        }

        Log($"等待场景稳定超时 ({timeoutMs}ms)");
        return false;
    }

    private void ForceEndGame()
    {
        // 兜底处理：只更新计分板，不做任何 API 上报
        var heroName = _currentGame.HeroName ?? "未知英雄";
        var heroCardId = _currentGame.HeroCardId ?? "";

        Log($"兜底结束 - {heroName}");

        // 保存简化记录（排名=0，分数=0）
        var record = new GameRecord
        {
            BattleTag = _localPlayerBattleTag,
            HeroName = heroName,
            HeroCardId = heroCardId,
            Placement = 0,
            Points = 0,
            Rating = _lastKnownMmr,
            RatingAfter = 0,
            RatingChange = 0,
            GameUuid = _currentGameUuid,
            Mode = _config.Mode,
            Timestamp = DateTime.UtcNow.ToString("o"),
        };
        GameStore.Save(record);

        // 通知 UI 更新计分板
        OnGameEnded?.Invoke(record);

        // 重置状态
        SetPhase(GamePhase.Idle);
        _currentGame.IsActive = false;
    }

    // ═══════════════════════════════════════
    //  update-placement
    // ═══════════════════════════════════════

    private void HandleGameEnd(GameEndEvent evt)
    {
        _league.StopRetry();
        SetPhase(GamePhase.PostGame);

        // 游戏结束时清空拔线缓存（与团子版对齐：HandleGameEnd 立即禁用拔线）
        DisconnectService.IsGameEnded = true;
        DisconnectService.EndReconnect();

        var placement = evt.Placement;

        // 构筑模式：跳过所有 BG 流程
        if (_gameState.IsConstructed)
        {
            Log($"构筑模式对局结束 - 跳过 BG 流程");
            return;
        }

        Log($"对局结束 - 排名: {placement}");

        // ── 读取 RatingChange ──
        var startMmr = _lastKnownMmr; // 保存游戏开始时的 MMR
        var rc = PollRatingChange();
        if (rc != null)
        {
            _lastKnownMmr = rc.NewRating;
            OnMmrChanged?.Invoke(rc.NewRating);
        }

        // ── update-placement（仅联赛对局）──
        if (_league.IsLeagueGame && !string.IsNullOrEmpty(_currentGameUuid))
        {
            var otherPlacements = new List<(ulong lo, int placement)>();

            // 从内存读取实时排名
            var rankings = _hm.GetPlayerRankings();
            if (rankings.Count > 0)
            {
                // 构建 teamId → Lo 映射（如果还没构建）
                if (_teamIdToLo.Count == 0)
                    BuildTeamIdToLoMapping(rankings);

                foreach (var (heroCardId, rank, isDead, teamId, playerId) in rankings)
                {
                    // 排名比自己低的玩家
                    if (rank > placement)
                    {
                        // 用 teamId 查找 Lo
                        if (_teamIdToLo.TryGetValue(teamId, out var lo) && lo != 0 && lo != _localPlayerLo)
                            otherPlacements.Add((lo, rank));
                    }
                }
            }
            else
            {
                // 备用方案：从 AllHeroes 读取（Power.log 解析的数据）
                foreach (var hero in _currentGame.AllHeroes.Values)
                {
                    if (hero.Placement > placement)
                    {
                        var matched = _currentGame.LobbyPlayers.FirstOrDefault(lp =>
                            string.Equals(lp.HeroCardId, hero.CardId, StringComparison.OrdinalIgnoreCase));
                        if (matched != null && matched.Lo != 0 && matched.Lo != _localPlayerLo)
                            otherPlacements.Add((matched.Lo, hero.Placement));
                    }
                }
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var ok = ApiClient.UpdatePlacementAsync(_currentGameUuid, _localPlayerBattleTag,
                    _localPlayerLo, placement, _currentGame.ReconnectTimes, otherPlacements)
                    .GetAwaiter().GetResult();

                if (ok)
                {
                    Log($"排名已上传 - {placement}名");
                    break;
                }

                if (attempt < 2) Thread.Sleep(2000);
                else Log("上传失败，已重试3次");
            }
        }

        // ── 保存记录（联赛积分 + MMR 变动）──
        var points = _league.IsLeagueGame ? (placement == 1 ? 9 : Math.Max(1, 9 - placement)) : 0;
        var record = new GameRecord
        {
            BattleTag = _localPlayerBattleTag,
            HeroName = _currentGame.HeroName,
            HeroCardId = _currentGame.HeroCardId,
            Placement = placement,
            Points = points,
            Rating = startMmr,
            RatingAfter = rc?.NewRating ?? 0,
            RatingChange = rc?.Change ?? 0,
            GameUuid = _currentGameUuid,
            Mode = _config.Mode,
            Timestamp = DateTime.UtcNow.ToString("o"),
        };
        GameStore.Save(record);
        var rcText = rc != null ? $" MMR:{rc.OldRating}→{rc.NewRating}({rc.Change:+#;-#;0})" : "";
        Log($"记录已保存 - {placement}名{rcText}");
        OnGameEnded?.Invoke(record);
    }

    // ═══════════════════════════════════════
    //  PollRatingChange
    // ═══════════════════════════════════════

    private RatingChangeData PollRatingChange()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var rc = _hm.GetBaconRatingChangeData();
                if (rc != null && rc.NewRating > 0) return rc;
            }
            catch { }
            Thread.Sleep(500);
        }
        return null;
    }

    /// <summary>构建 teamId → Lo 映射（优先用 playerId，fallback 到 heroCardId）</summary>
    private void BuildTeamIdToLoMapping(List<(string heroCardId, int placement, bool isDead, int teamId, int playerId)> rankings)
    {
        _teamIdToLo.Clear();
        foreach (var (heroCardId, _, _, teamId, playerId) in rankings)
        {
            ulong lo = 0;

            // 优先用 playerId 匹配（不受英雄重复影响）
            if (playerId != 0 && _playerIdToLo.TryGetValue(playerId, out var mappedLo) && mappedLo != 0)
            {
                lo = mappedLo;
            }
            else
            {
                // fallback: 用 heroCardId 匹配 LobbyPlayers
                var matched = _currentGame.LobbyPlayers.FirstOrDefault(lp =>
                    string.Equals(lp.HeroCardId, heroCardId, StringComparison.OrdinalIgnoreCase));
                if (matched != null && matched.Lo != 0)
                    lo = matched.Lo;
            }

            if (lo != 0)
            {
                _teamIdToLo[teamId] = lo;
                Console.WriteLine($"[Mapping] teamId={teamId} → Lo={lo} (playerId={playerId}, hero={heroCardId})");
            }
        }
    }

    // ═══════════════════════════════════════
    //  LeagueClient 回调
    // ═══════════════════════════════════════

    private void OnLeagueStateChanged()
    {
        _currentGameUuid = _league.GameUuid ?? "";
        _verifyCode = _league.VerificationCode ?? "";

        if (!string.IsNullOrEmpty(_verifyCode))
            OnVerifyCodeChanged?.Invoke(_verifyCode);

        if (_league.IsLeagueGame)
        {
            _currentGame.IsLeagueGame = true;
            SetPhase(GamePhase.Active);
            Log($"联赛对局确认! GameUuid={_currentGameUuid}");
        }
        else
        {
            _currentGame.IsLeagueGame = false;
            SetPhase(GamePhase.Active);
            Log("非联赛对局");
        }
    }

    // ═══════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════

    /// <summary>
    /// 检测当前游戏模式。
    /// 场景 4 时回读 Power.log，每次 5MB，找到 GameType 为止。
    /// 非场景 4 返回 null。
    /// </summary>
    public string DetectCurrentGameMode()
    {
        var scene = _hm.GetSceneMode();
        if (scene != SceneMode.GAMEPLAY) return null;

        try
        {
            const long chunkSize = 5 * 1024 * 1024;
            var fileLen = new FileInfo(_logPath).Length;

            string lastGameType = null;
            string lastFormatType = null;

            // 从末尾向前每次读 5MB，找到 GameType 为止
            for (int attempt = 0; attempt < 10 && lastGameType == null; attempt++)
            {
                var chunkEnd = fileLen - attempt * chunkSize;
                var chunkStart = Math.Max(0, chunkEnd - chunkSize);
                Log($"DetectCurrentGameMode: 第{attempt + 1}次, 读取 {chunkStart / 1024}KB→{chunkEnd / 1024}KB");

                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    fs.Seek(chunkStart, SeekOrigin.Begin);
                    while (fs.Position < chunkEnd && !reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (!line.Contains("DebugPrintGame()")) continue;

                        if (line.Contains("GameType="))
                        {
                            var idx = line.IndexOf("GameType=") + 9;
                            var end = line.IndexOfAny(new[] { ' ', '\r', '\n' }, idx);
                            lastGameType = end > idx ? line.Substring(idx, end - idx) : line.Substring(idx);
                        }
                        if (line.Contains("FormatType="))
                        {
                            var idx = line.IndexOf("FormatType=") + 11;
                            var end = line.IndexOfAny(new[] { ' ', '\r', '\n' }, idx);
                            lastFormatType = end > idx ? line.Substring(idx, end - idx) : line.Substring(idx);
                        }
                    }
                }

                if (chunkStart == 0) break; // 已读到文件头
            }

            if (lastGameType == null)
            {
                Log("DetectCurrentGameMode: 遍历整个日志未找到 GameType");
                return null;
            }
            var mode = Parser.GameTypeToMode(lastGameType, lastFormatType ?? "");
            Log($"检测到 GameType={lastGameType}, FormatType={lastFormatType} → mode={mode ?? "(空)"}");
            return string.IsNullOrEmpty(mode) ? null : mode;
        }
        catch (Exception ex)
        {
            Log($"DetectCurrentGameMode 异常: {ex.Message}");
            return null;
        }
    }

    private void TryReadMmr()
    {
        try
        {
            var ratingInfo = _hm.GetBattlegroundRatingInfo();
            if (ratingInfo != null && ratingInfo.Rating > 0)
            {
                _lastKnownMmr = ratingInfo.Rating;
                _currentGame.Rating = ratingInfo.Rating;
                GameStore.SaveTodayStartMmr(ratingInfo.Rating, PlayerName);
                OnMmrChanged?.Invoke(ratingInfo.Rating);
                Log($"MMR: {ratingInfo.Rating}");
            }
            else
            {
                Log($"[Debug] TryReadMmr: ratingInfo={ratingInfo?.Rating ?? 0}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Debug] TryReadMmr 异常: {ex.Message}");
        }
    }

    private int _fetchFailCount;

    private void TryFetchPlayerInfo()
    {
        if (_localPlayerBattleTag == "")
        {
            var tag = _hm.GetBattleTag();
            if (tag != null)
            {
                _localPlayerBattleTag = $"{tag.Name}#{tag.Number}";
                _localPlayerDisplayName = tag.Name;
                _fetchFailCount = 0;
                Log($"玩家: {_localPlayerBattleTag}");
            }
            else if (++_fetchFailCount % 50 == 1) // 每5秒打一次
            {
                Log($"[DEBUG] GetBattleTag 返回 null (第{_fetchFailCount}次), IsConnected={_hm.IsConnected}");
            }
        }

        if (_localPlayerLo == 0)
        {
            var acc = _hm.GetAccountId();
            if (acc != null && acc.Lo != 0)
            {
                _localPlayerHi = acc.Hi;
                _localPlayerLo = acc.Lo;
                Log($"AccountId.Hi: {_localPlayerHi}, Lo: {_localPlayerLo}");
            }
        }

        // 两个信息都就绪后触发 UI 更新
        if (_localPlayerBattleTag != "" && _localPlayerHi != 0 && !_playerNameReported)
        {
            _playerNameReported = true;
            var region = ApiClient.GetRegionString(ApiClient.GetRegionFromAccountIdHi(_localPlayerHi));
            OnPlayerNameChanged?.Invoke($"{_localPlayerBattleTag} ({region})");
        }

        if (_localPlayerBattleTag != "" && _localPlayerLo != 0 && !_hmReady)
        {
            _hmReady = true;

            // 先读 MMR
            TryReadMmr();

            // initialize-player（带上 MMR 一起上报，失败重试最多 5 次）
            var initializeSuccess = false;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    var ok = ApiClient.InitializePlayerAsync(_localPlayerBattleTag, _localPlayerHi, _localPlayerLo, _lastKnownMmr)
                        .GetAwaiter().GetResult();
                    if (ok)
                    {
                        _verifyCode = ApiClient.VerificationCode ?? "";
                        if (!string.IsNullOrEmpty(_verifyCode))
                            OnVerifyCodeChanged?.Invoke(_verifyCode);
                        Log($"验证码: {_verifyCode}");
                        initializeSuccess = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"initialize-player 失败({attempt}/5): {ex.Message}");
                }

                if (attempt < 5)
                    Thread.Sleep(2000);
            }

            if (!initializeSuccess)
                Log("initialize-player 重试 5 次后仍然失败");
        }
    }

    private void SetPhase(GamePhase phase)
    {
        if (_phase == phase) return;
        _phase = phase;
        OnPhaseChanged?.Invoke(phase, _currentGame);
    }

    private void ResetToIdle()
    {
        _phase = GamePhase.Idle;
        _currentGame = new Game();
        _gameState = new GameState();
        _games.Clear();
        _currentGameUuid = "";
        _startMmr = 0;
        _gameGeneration++;
        _league.Reset();
        DisconnectService.EndReconnect(); // 清除拔线状态
        ResetPlayerInfo();
        OnPhaseChanged?.Invoke(GamePhase.Idle, _currentGame);
    }

    private void ResetPlayerInfo()
    {
        _localPlayerBattleTag = "";
        _localPlayerDisplayName = "";
        _localPlayerHi = 0;
        _localPlayerLo = 0;
        _playerNameReported = false;
        _hmReady = false;
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[Monitor] {msg}");
        OnLogMessage?.Invoke(msg);
    }

    public List<GameRecord> GetRecentGames(int count = 5) => GameStore.GetRecent(count, PlayerName);
    public List<GameRecord> GetTodayGames() => GameStore.GetToday(PlayerName);

    public void Dispose()
    {
        if (!_disposed)
        {
            _running = false;
            _disposed = true;
        }
    }
}
}
