using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BattlegroundSpy.Objects;
using HBT.Models;
using HBT.Plugins;
using HBTCombat;

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
    private readonly PluginManager _plugins;
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
    private bool _racesFetched;
    private int _lastKnownMmr;
    private int _startMmr;
    private int _hsPid;
    private string _logPath = "";
    private bool _disposed;

    // 场景状态（仅用于 UI 显示和辅助判断）
    private int _lastSceneMode = SceneMode.INVALID;

    // 线程
    private Thread _sceneThread;
    private Thread _logThread;
    private Thread _hoverThread;
    private volatile bool _running;

    // 游戏状态锁
    private readonly object _gameLock = new object();

    // 悬停状态
    private string _lastHoveredHeroCardId = "";
    private int _lastHoveredPlayerId = 0;

    /// <summary>对手阵容记录</summary>
    private class OpponentBoardRecord
    {
        public List<Dictionary<string, object>> BoardState { get; set; }
        public int CapturedTurn { get; set; }  // 捕获时的实际回合数
        public string HeroCardId { get; set; }
        public int PlayerId { get; set; }       // PLAYER_ID (tag 2)，唯一标识
    }

    // 对手阵容缓存（按 PLAYER_ID 索引，唯一且稳定）
    private readonly Dictionary<int, OpponentBoardRecord> _opponentBoardCache = new();
    private OpponentBoardRecord _lastCapturedRecord;
    private bool _boardCapturedThisCombat;

    // teamId → Lo 映射（用于 update-placement 匹配）
    private readonly Dictionary<int, ulong> _teamIdToLo = new();
    // playerId → Lo 映射（从大厅 m_playerMap 构建，用于匹配排行榜）
    private Dictionary<int, ulong> _playerIdToLo = new();

    // 回合数跟踪
    private int _lastRawTurn;

    // 事件
    public event Action<GamePhase, Game> OnPhaseChanged;
    public event Action<GameRecord> OnGameEnded;
    public event Action<string> OnVerifyCodeChanged;
    public event Action<string> OnLogMessage;
    public event Action<string> OnPlayerNameChanged;  // 格式: "玩家名#1234 (CN)"
    public event Action<int> OnMmrChanged;
    public event Action<int> OnSceneChanged;  // 场景变化（UI 显示用）
    public event Action<List<string>>? OnAvailableRacesChanged;  // 本局可用种族代码
    public event Action<int> OnGameStarted;  // 游戏开始（记分板用，参数为起始 MMR）
    public event Action<string, List<Dictionary<string, object>>?, int, string> OnOpponentBoardHover;  // 对手阵容悬停（英雄卡牌ID, 阵容数据, 回合数, 对战胜率文本）
    public event Action<int> OnTurnNumberChanged;  // 回合数变化（参数为实际回合数）
    public event Action<float, float, float, int, int, float, int, int, float> OnCombatSimulationResult;  // 战斗模拟结果

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
        BoardStateStore.Init();
        // HeadToHeadStore 在获取到 localPlayerLo 后初始化
        _league = new LeagueClient(config);
        _league.OnStateChanged = OnLeagueStateChanged;
        _plugins = new PluginManager(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));
        _plugins.OnLog = msg => Log(msg);
        _plugins.LoadAll();
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

        // HoverWatcher：排行榜悬停检测（对手阵容显示）
        _hoverThread = new Thread(HoverWatcherLoop) { IsBackground = true, Name = "HoverWatcher" };
        _hoverThread.Start();
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
                                                lock (_gameLock) { _currentGame = newGame; }
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
                    lock (_gameLock) { _currentGame = newGame; }

                    if (evt != null)
                        HandleParserEvent(evt);
                }

                // 插件定时更新
                _plugins.Update();
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
        switch (evt)
        {
            case GameStartEvent:
                // Power.log 检测到 CREATE_GAME + GameType=BG → 游戏开始
                _gameGeneration++;
                _league.Reset();
                _currentGameUuid = "";
                _racesFetched = false;
                _entityTracker.Clear(); // 清除实体追踪
                _opponentBoardCache.Clear(); // 清除对手阵容缓存
                _lastCapturedRecord = null;
                SetPhase(GamePhase.PreLobby);
                if (!string.IsNullOrEmpty(_currentGame.HeroName))
                    Log($"新对局开始 - 英雄: {_currentGame.HeroName}");
                else
                    Log("新对局开始");
                // 提前读取种族（此时内存可能已就绪）
                _racesFetched = TryReadAvailableRaces();
                // 通知插件
                _plugins.OnGameStart();
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

            case CombatStartEvent:
                // BG 战斗阶段开始：立即捕获对手阵容
                Log($"[Debug] CombatStartEvent 收到, phase={_phase}");
                if (_phase == GamePhase.Active)
                {
                    HandleCombatStart();
                }
                else
                {
                    Log($"[Debug] phase={_phase}, 跳过 HandleCombatStart");
                }
                break;

            case BoardSnapshotEvent:
                // BG 阵容快照：tag 3533 1→0，对齐 HDT SnapshotCurrentBoard 时机
                if (_phase == GamePhase.Active)
                {
                    HandleBoardSnapshot();
                }
                break;
        }
    }

    // ═══════════════════════════════════════
    //  check-league（STEP MAIN_CLEANUP 触发）
    // ═══════════════════════════════════════

    private void HandleCheckLeague()
    {
        Log("获取大厅玩家信息...");

        // 种族：PreLobby 可能已读取，未成功则再试一次
        if (!_racesFetched)
            _racesFetched = TryReadAvailableRaces();

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
        {
            _startMmr = _lastKnownMmr;
            OnGameStarted?.Invoke(_startMmr);
        }

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
    //  HoverWatcher - 排行榜悬停检测（对手阵容显示）
    // ═══════════════════════════════════════════════════════════════

    private void HoverWatcherLoop()
    {
        while (_running)
        {
            Thread.Sleep(100);  // 每 100ms 检测一次

            try
            {
                // 只在战棋游戏中检测
                if (_phase != GamePhase.Active && _phase != GamePhase.PostGame)
                {
                    if (!string.IsNullOrEmpty(_lastHoveredHeroCardId))
                    {
                        _lastHoveredHeroCardId = "";
                        OnOpponentBoardHover?.Invoke("", null, 0, "");
                    }
                    _boardCapturedThisCombat = false;
                    _lastRawTurn = 0;
                    continue;
                }

                // 更新回合数
                var rawTurn = _hm.GetTurnNumber();
                if (rawTurn != null && rawTurn.Value != _lastRawTurn)
                {
                    _lastRawTurn = rawTurn.Value;
                    // 只在奇数 rawTurn 时更新（偶数 rawTurn 是战斗阶段，回合数不变）
                    if (rawTurn.Value % 2 == 1)
                    {
                        var actualTurn = (rawTurn.Value + 1) / 2;
                        OnTurnNumberChanged?.Invoke(actualTurn);
                    }
                }
                // rawTurn 为 0 时（英雄选择阶段），显示第 1 回合
                else if (rawTurn == null || rawTurn.Value == 0)
                {
                    if (_lastRawTurn != 0)
                    {
                        _lastRawTurn = 0;
                    }
                    OnTurnNumberChanged?.Invoke(1);
                }

                var heroCardId = _hm.GetLeaderboardHoveredHeroCardId();

                // 优先从 tile 直接读取 PLAYER_ID
                int playerId = _hm.GetLeaderboardHoveredPlayerId();

                // fallback: 通过 entity ID 在 EntityTracker 中查找（对齐 HDT 方案）
                if (playerId == 0)
                {
                    int entityId = _hm.GetLeaderboardHoveredEntityId();
                    if (entityId > 0 && _entityTracker.Entities.TryGetValue(entityId, out var entity))
                    {
                        playerId = entity.GetTag(2); // PLAYER_ID
                    }
                }

                // fallback: 通过 heroCardId 在 EntityTracker 中查找
                if (playerId == 0 && !string.IsNullOrEmpty(heroCardId))
                {
                    var localController = _hm.GetLocalControllerIdPublic();
                    if (localController != null)
                    {
                        foreach (var entity in _entityTracker.Entities.Values)
                        {
                            if (entity.IsHero && entity.CardId == heroCardId && entity.Controller != localController.Value && entity.Controller > 0)
                            {
                                playerId = entity.GetTag(2); // PLAYER_ID
                                break;
                            }
                        }
                    }
                }

                // 悬停变化（用 playerId + heroCardId 判断变化）
                if (playerId != _lastHoveredPlayerId || heroCardId != _lastHoveredHeroCardId)
                {
                    _lastHoveredHeroCardId = heroCardId ?? "";
                    _lastHoveredPlayerId = playerId;

                    if (string.IsNullOrEmpty(heroCardId) && playerId == 0)
                    {
                        // 移开鼠标，清除显示
                        OnOpponentBoardHover?.Invoke("", null, 0, "");
                    }
                    else
                    {
                        // 从缓存查找对手阵容（优先用 PLAYER_ID，fallback 到 heroCardId）
                        OpponentBoardRecord record = null;

                        if (playerId > 0 && _opponentBoardCache.TryGetValue(playerId, out var cached))
                        {
                            record = cached;
                        }
                        else if (_lastCapturedRecord != null && _lastCapturedRecord.PlayerId == playerId && playerId > 0)
                        {
                            record = _lastCapturedRecord;
                        }
                        else
                        {
                            Log($"悬停未命中: playerId={playerId} ({heroCardId})");
                        }

                        // 查找对手 Lo（用于胜率查询）
                        ulong opponentLo = 0;
                        if (playerId > 0 && _playerIdToLo.TryGetValue(playerId, out var mappedLo))
                            opponentLo = mappedLo;
                        else if (heroCardId != null)
                        {
                            List<LobbyPlayer> players;
                            lock (_gameLock) { players = _currentGame.LobbyPlayers.ToList(); }
                            var matched = players.FirstOrDefault(lp =>
                                string.Equals(lp.HeroCardId, heroCardId, StringComparison.OrdinalIgnoreCase));
                            if (matched != null) opponentLo = matched.Lo;
                        }

                        // 查询胜率（无记录也显示 0-0）
                        var h2h = HeadToHeadStore.GetRecord(opponentLo);
                        var h2hTotal = h2h.Wins + h2h.Losses;
                        var h2hText = h2hTotal > 0
                            ? $"{h2h.Wins}-{h2h.Losses} ({(double)h2h.Wins / h2hTotal * 100:F0}%)"
                            : "0-0 (0%)";

                        if (record == null)
                        {
                            // 未遇到过该对手（显示胜率，不显示阵容）
                            OnOpponentBoardHover?.Invoke(heroCardId, null, 0, h2hText);
                        }
                        else
                        {
                            // 计算回合差
                            var currentTurn = GetActualTurn(_lastRawTurn);
                            var turnsAgo = currentTurn - record.CapturedTurn;

                            // 阵容可能为空（遇到过但场面无随从）
                            var boardState = record.BoardState ?? new List<Dictionary<string, object>>();
                            OnOpponentBoardHover?.Invoke(heroCardId, boardState, turnsAgo, h2hText);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hover] 异常: {ex.Message}");
            }
        }
    }


    /// <summary>计算实际回合数（rawTurn → actualTurn）</summary>
    private int GetActualTurn(int rawTurn)
    {
        return rawTurn % 2 == 0 ? rawTurn / 2 : (rawTurn + 1) / 2;
    }

    /// <summary>BG 战斗阶段开始：从 EntityTracker 获取对手阵容（纯 HDT 方案）</summary>
    private void HandleCombatStart()
    {
        try
        {
            Log("战斗阶段开始，从 EntityTracker 获取对手阵容");

            // 等待一小段时间让 EntityTracker 收集当前战斗的数据
            Thread.Sleep(100);

            var localController = _hm.GetLocalControllerIdPublic();
            if (localController == null)
            {
                Log("localController=null，跳过捕获");
                return;
            }

            // 从 EntityTracker 获取对手随从（ZONE=PLAY, CARDTYPE=MINION, CONTROLLER≠本地）
            var opponentMinions = _entityTracker.GetOpponentMinions(localController.Value);

            // 去重：同一 (cardId, zonePosition) 只保留 entityId 最大的（最新实体）
            // 解决战斗场景切换时旧实体未清理导致的重复问题
            var deduped = opponentMinions
                .GroupBy(m => $"{m.CardId}_{m.ZonePosition}")
                .Select(g => g.OrderByDescending(m => m.EntityId).First())
                .OrderBy(m => m.ZonePosition)
                .ToList();

            if (deduped.Count < opponentMinions.Count)
                Log($"[诊断] 去重: {opponentMinions.Count} → {deduped.Count} 个随从");

            // 用 BGSpy 获取对手英雄 cardId
            var boardState = _hm.GetOpponentBoardState();
            var heroCardId = boardState?.HeroCardId ?? _hm.GetOpponentHeroCardId() ?? "";

            // 优先从 ZONE_PLAY 英雄实体直接读取 PLAYER_ID（畸变时也能正确识别）
            int playerId = _hm.GetOpponentPlayerIdInPlay();

            // fallback: 从 EntityTracker 获取
            if (playerId == 0 && !string.IsNullOrEmpty(heroCardId))
            {
                foreach (var entity in _entityTracker.Entities.Values)
                {
                    if (entity.IsHero && entity.CardId == heroCardId && entity.Controller != localController.Value && entity.Controller > 0)
                    {
                        playerId = entity.GetTag(2); // PLAYER_ID
                        break;
                    }
                }
            }

            Log($"英雄: {heroCardId}, playerId={playerId}, 随从={deduped.Count}");

            // 转换为字典格式（即使为空也要保存，表示遇到过该对手）
            var boardDicts = new List<Dictionary<string, object>>();
            foreach (var minion in deduped.Take(7))
            {
                boardDicts.Add(new Dictionary<string, object>
                {
                    ["cardId"] = minion.CardId,
                    ["attack"] = minion.Attack,
                    ["health"] = minion.MaxHealth,
                    ["golden"] = minion.Golden,
                    ["taunt"] = minion.Taunt,
                    ["divineShield"] = minion.DivineShield,
                    ["poisonous"] = minion.Poisonous,
                    ["venomous"] = minion.Venomous,
                    ["windfury"] = minion.Windfury,
                    ["reborn"] = minion.Reborn,
                    ["stealth"] = minion.Stealth,
                    ["deathrattle"] = minion.Deathrattle,
                    ["techLevel"] = minion.TechLevel,
                });
            }

            // 计算实际回合数
            var actualTurn = GetActualTurn(_lastRawTurn);

            var record = new OpponentBoardRecord
            {
                BoardState = boardDicts,
                CapturedTurn = actualTurn,
                HeroCardId = heroCardId,
                PlayerId = playerId,
            };

            _lastCapturedRecord = record;
            _boardCapturedThisCombat = true;

            // 用 PLAYER_ID 作为缓存键（唯一且稳定）
            if (playerId > 0)
            {
                if (_opponentBoardCache.TryGetValue(playerId, out var old))
                    Log($"覆盖缓存: playerId={playerId} ({heroCardId}) 旧回合{old.CapturedTurn}→新回合{actualTurn}");

                _opponentBoardCache[playerId] = record;
                Log($"战斗开始捕获: playerId={playerId} ({heroCardId}), {boardDicts.Count}个随从, 回合{actualTurn}");
            }
            else
            {
                Log($"PlayerId=0, 跳过缓存存储 ({heroCardId})");
            }

            // 清理 EntityTracker 残留（防止下次战斗累积）
            var cleared = _entityTracker.ClearStaleEntities(localController.Value);
            Log($"[诊断] ClearStaleEntities 清除了 {cleared} 个实体，剩余 {_entityTracker.Entities.Count} 个");

            // ── 触发战斗模拟 ──
            RunCombatSimulation(deduped, heroCardId);
        }
        catch (Exception ex)
        {
            Log($"战斗开始捕获失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 运行战斗模拟（后台线程）
    /// </summary>
    private void RunCombatSimulation(List<TrackedEntity> opponentMinions, string opponentHeroCardId)
    {
        Log($"[模拟] RunCombatSimulation 被调用, 对手随从={opponentMinions.Count}");
        try
        {
            // 读取己方阵容（允许为空）
            var playerMinions = _hm.GetPlayerBoardMinions() ?? new List<BoardMinion>();
            Log($"[模拟] 己方随从={playerMinions.Count}");

            // 构建输入（允许一方为空）
            int playerTier = playerMinions.Count > 0 ? playerMinions.Max(m => m.TechLevel) : 6;
            var input = new SimulationInput
            {
                PlayerHealth = 40,
                OpponentHealth = 40,
                PlayerTier = playerTier,
                OpponentTier = 6,
                Turn = GetActualTurn(_lastRawTurn),
                DamageCap = _hm.GetDamageCap(),
            };

            // 初始化 MinionFactory 并注册行为
            var factory = new MinionFactory();
            MinionFactoryCache.SetFactory(factory);
            MinionBehaviors.RegisterAll();

            // 转换己方随从（使用 MinionFactory 创建带行为的 Minion）
            foreach (var m in playerMinions)
            {
                var minion = factory.CreateFromCardId(
                    m.CardId, true,
                    m.Attack, m.MaxHealth, m.MaxHealth,
                    m.Taunt, m.DivineShield, m.Poisonous, m.Venomous,
                    m.Windfury, false, false, m.Reborn,
                    m.Golden, m.TechLevel
                );
                input.PlayerBoard.Add(minion);
            }

            // 转换对手随从
            foreach (var m in opponentMinions)
            {
                var minion = factory.CreateFromCardId(
                    m.CardId, false,
                    m.Attack, m.MaxHealth, m.MaxHealth,
                    m.Taunt, m.DivineShield, m.Poisonous, m.Venomous,
                    m.Windfury, false, false, m.Reborn,
                    m.Golden, m.TechLevel
                );
                input.OpponentBoard.Add(minion);
            }

            Log($"[模拟] 开始模拟: 己方{input.PlayerBoard.Count}个 vs 对方{input.OpponentBoard.Count}个, DamageCap={input.DamageCap}");

            // 后台运行模拟
            Task.Run(() =>
            {
                var result = SimulationRunner.Run(input, iterations: 10000, maxMs: 1500);
                Log($"[模拟] 完成: 胜{result.WinRate * 100:F0}% 平{result.TieRate * 100:F0}% 负{result.LossRate * 100:F0}% 我方{result.PlayerDamageMin}~{result.PlayerDamageMax} 对方{result.OpponentDamageMin}~{result.OpponentDamageMax}");
                OnCombatSimulationResult?.Invoke(
                    result.WinRate, result.TieRate, result.LossRate,
                    result.PlayerDamageMin, result.PlayerDamageMax, result.PlayerDamageAvg,
                    result.OpponentDamageMin, result.OpponentDamageMax, result.OpponentDamageAvg);
            });
        }
        catch (Exception ex)
        {
            Log($"[模拟] 异常: {ex.Message}");
        }
    }

    /// <summary>已知顺劈随从 CardId 列表</summary>
    private static readonly HashSet<string> CleaveMinions = new HashSet<string>
    {
        "BOT_559",      // 洞穴九头蛇 Cave Hydra
        "BG22_002",     // 急速潜行者 Frenzied Lefthander
        "BG26_127",     // 潮汐女皇 Tidal Empress
        "LOE_073",      // 迪恩巴拉瑟布甲虫 Djinn-Bound Scarab
    };

    private static bool IsCleaveMinion(string cardId) => CleaveMinions.Contains(cardId);

    /// <summary>
    /// BG 阵容快照：tag 3533 1→0 时触发（对齐 HDT SnapshotCurrentBoard）。
    /// 用 BGSpy 读取实时内存快照，替代 EntityTracker（Power.log 累积）。
    /// </summary>
    private void HandleBoardSnapshot()
    {
        try
        {
            // 用 BGSpy 读取对手场面（实时内存快照，类似 HDT 的 _game.Entities）
            var boardState = _hm.GetOpponentBoardState();
            if (boardState == null)
            {
                Log("[Snapshot] GetOpponentBoardState 返回 null");
                return;
            }

            var heroCardId = boardState.HeroCardId ?? "";
            Log($"[诊断] BGSpy 快照: hero={heroCardId}, minions={boardState.BoardCards.Count}");

            // 优先从 ZONE_PLAY 英雄实体直接读取 PLAYER_ID（畸变时也能正确识别）
            int playerId = _hm.GetOpponentPlayerIdInPlay();

            // fallback: 从 EntityTracker 获取
            if (playerId == 0 && !string.IsNullOrEmpty(heroCardId))
            {
                var localController = _hm.GetLocalControllerIdPublic();
                if (localController != null)
                {
                    foreach (var entity in _entityTracker.Entities.Values)
                    {
                        if (entity.IsHero && entity.CardId == heroCardId && entity.Controller != localController.Value && entity.Controller > 0)
                        {
                            playerId = entity.GetTag(2); // PLAYER_ID
                            break;
                        }
                    }
                }
            }

            // 转换为字典格式，限制最多 7 个随从（BG 场面上限）
            var boardDicts = new List<Dictionary<string, object>>();
            foreach (var card in boardState.BoardCards.Take(7))
            {
                boardDicts.Add(new Dictionary<string, object>
                {
                    ["cardId"] = card.CardId,
                    ["attack"] = card.Attack,
                    ["health"] = card.Health,
                    ["golden"] = card.Golden,
                    ["taunt"] = card.Taunt,
                    ["divineShield"] = card.DivineShield,
                    ["poisonous"] = card.Poisonous,
                    ["venomous"] = card.Venomous,
                    ["windfury"] = card.Windfury,
                    ["reborn"] = card.Reborn,
                    ["stealth"] = card.Stealth,
                    ["deathrattle"] = card.Deathrattle,
                    ["techLevel"] = card.TechLevel,
                });
            }

            var actualTurn = GetActualTurn(_lastRawTurn);

            var record = new OpponentBoardRecord
            {
                BoardState = boardDicts,
                CapturedTurn = actualTurn,
                HeroCardId = heroCardId,
                PlayerId = playerId,
            };

            _lastCapturedRecord = record;
            _boardCapturedThisCombat = true;

            // 用 PLAYER_ID 作为缓存键（唯一且稳定）
            if (playerId > 0)
            {
                if (_opponentBoardCache.TryGetValue(playerId, out var old))
                    Log($"[Snapshot] 覆盖缓存: playerId={playerId} ({heroCardId}) 旧回合{old.CapturedTurn}→新回合{actualTurn}");

                _opponentBoardCache[playerId] = record;
                Log($"阵容快照: playerId={playerId} ({heroCardId}), {boardDicts.Count}个随从, 回合{actualTurn}");
            }
            else
            {
                Log($"[Snapshot] PlayerId=0, 跳过缓存存储 ({heroCardId})");
            }
        }
        catch (Exception ex)
        {
            Log($"阵容快照失败: {ex.Message}");
        }
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

        // 仅通知 UI 更新计分板（不保存到 GameStore，避免 Placement=0 污染统计数据）
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
        _entityTracker.Clear(); // 清空实体追踪，防止下局累积

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

        // ── 计算 otherPlacements（所有对局共用）──
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

        // ── update-placement（仅联赛对局）──
        if (_league.IsLeagueGame && !string.IsNullOrEmpty(_currentGameUuid))
        {
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

        // ── 记录对战胜负（所有对局）──
        if (_localPlayerLo != 0)
        {
            var wonSet = new HashSet<ulong>(otherPlacements.Select(p => p.lo));
            foreach (var lp in _currentGame.LobbyPlayers)
            {
                if (lp.Lo == 0 || lp.Lo == _localPlayerLo) continue;
                HeadToHeadStore.RecordGame(lp.Lo, wonSet.Contains(lp.Lo));
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

        // 通知插件
        _plugins.OnGameEnd(placement, rc?.Change ?? 0);

        // ── report-game-stats（rating + 阵容 + ratingChange）──
        ReportGameStats(placement, rc);
    }

    // ═══════════════════════════════════════
    //  report-game-stats
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

    private void ReportGameStats(int placement, RatingChangeData rc)
    {
        try
        {
            var boardState = ReadBoardState();
            // 本地保存最终阵容
            var boardDicts = boardState.OfType<Dictionary<string, object>>().ToList();
            BoardStateStore.Save(_currentGameUuid, _localPlayerBattleTag, boardDicts);
            var trinkets = ReadTrinkets();
            var anomalyDbfId = ReadAnomalyDbfId();

            Dictionary<string, object> ratingChange = null;
            if (rc != null)
            {
                ratingChange = new Dictionary<string, object>
                {
                    ["oldRating"] = rc.OldRating,
                    ["newRating"] = rc.NewRating,
                    ["change"] = rc.Change,
                };
            }
            ApiClient.ReportGameStatsAsync(
                _currentGameUuid, _localPlayerBattleTag, _localPlayerLo,
                _lastKnownMmr, boardState, ratingChange, placement, trinkets, anomalyDbfId,
                _currentGame.HeroCardId, _currentGame.HeroName)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log($"report-game-stats 异常: {ex.Message}");
        }
    }

    private int ReadAnomalyDbfId()
    {
        try { return _hm.GetAnomalyDbfId(); }
        catch { return 0; }
    }

    private List<string> ReadTrinkets()
    {
        try
        {
            // 调试：先读详细信息看 zone 值
            var detailed = _hm.GetPlayerTrinketsDetailed();
            foreach (var d in detailed)
                Log($"[DEBUG] 饰品详情: {d}");

            var trinkets = _hm.GetPlayerTrinkets() ?? new List<string>();
            Log($"[DEBUG] 读取饰品: {trinkets.Count}个");
            return trinkets;
        }
        catch (Exception ex)
        {
            Log($"[DEBUG] 读取饰品异常: {ex.Message}");
            return new List<string>();
        }
    }

    private List<object> ReadBoardState()
    {
        var result = new List<object>();
        try
        {
            var minions = _hm.GetPlayerBoardMinions();
            if (minions == null) return result;
            foreach (var m in minions)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["cardId"] = m.CardId,
                    ["attack"] = m.Attack,
                    ["health"] = m.Health,
                    ["techLevel"] = m.TechLevel,
                    ["golden"] = m.CardId.EndsWith("_G"),
                    ["taunt"] = m.Taunt,
                    ["divineShield"] = m.DivineShield,
                    ["poisonous"] = m.Poisonous,
                    ["venomous"] = m.Venomous,
                    ["windfury"] = m.Windfury,
                    ["reborn"] = m.Reborn,
                    ["stealth"] = m.Stealth,
                    ["deathrattle"] = m.Deathrattle,
                });
            }
        }
        catch (Exception ex)
        {
            Log($"读取阵容失败: {ex.Message}");
        }
        return result;
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

    private bool TryReadAvailableRaces()
    {
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                var tagRaces = _hm.GetAvailableRaces();
                if (tagRaces != null && tagRaces.Count > 0)
                {
                    var codes = CardDatabaseService.TagRacesToCodes(tagRaces);
                    if (codes.Count > 0)
                    {
                        Log($"本局种族: {string.Join(", ", codes.Select(c => CardDatabaseService.GetRaceChinese(c)))}");
                        OnAvailableRacesChanged?.Invoke(codes);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"读取种族失败(重试{retry + 1}/3): {ex.Message}");
            }
            if (retry < 2) Thread.Sleep(500);
        }
        Log("无法获取本局种族信息");
        return false;
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
                HeadToHeadStore.Init(_localPlayerLo);
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
        lock (_gameLock) { _currentGame = new Game(); }
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
            _plugins?.Dispose();
        }
    }
}
}
