using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{

public partial class MainWindow : Window
{
    private HearthMirrorService _hm;
    private GameMonitorService _monitor;
    private OverlayWindow _overlay;
    private CardDatabaseService _cardDb;
    private readonly string _logPath;
    private StreamWriter _logWriter;
    private int _logLineCount;

    public MainWindow()
    {
        InitializeComponent();
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HearthstoneBattlegroundTracker_debug.log");

        // 设置标题栏版本号
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"HBT v{version?.Major}.{version?.Minor}.{version?.Build ?? 0}";

        Opacity = 0;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 淡入动画
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fadeIn);

        // 初始化日志（同时重定向 Console 输出到文件）
        try
        {
            _logWriter = new StreamWriter(_logPath, append: false) { AutoFlush = true };
            _logWriter.WriteLine("=== HearthstoneBattlegroundTracker Monitor Started ===");
            Console.SetOut(_logWriter);
            Console.SetError(_logWriter);
        }
        catch { }

        // 初始化服务
        _hm = new HearthMirrorService();
        var config = Config.Load();
        _cardDb = new CardDatabaseService();
        _monitor = new GameMonitorService(config, _hm);

        // 创建覆盖层
        _overlay = new OverlayWindow(_hm, _cardDb);
        _overlay.Show();

        // Ctrl+B 切换卡牌浏览器（MainWindow 接收键盘，转发给 overlay）
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _overlay?.ToggleCardBrowser();
                e.Handled = true;
            }
        };

        // 订阅事件
        _monitor.OnPhaseChanged += (phase, game) => Dispatcher.Invoke(() => UpdatePhaseUI(phase, game));
        _monitor.OnGameEnded += record => Dispatcher.Invoke(() => RefreshStats());
        _monitor.OnAvailableRacesChanged += races => _overlay?.SetAvailableRaces(races);
        _monitor.OnVerifyCodeChanged += code => Dispatcher.Invoke(() =>
        {
            VerifyCode.Text = code;
        });
        _monitor.OnLogMessage += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _monitor.OnPlayerNameChanged += name => Dispatcher.Invoke(() =>
        {
            // name 格式: "玩家名#1234 (CN)"
            var lastParen = name.LastIndexOf('(');
            if (lastParen > 0)
            {
                PlayerName.Text = name.Substring(0, lastParen).Trim();
                var region = name.Substring(lastParen + 1).TrimEnd(')');
                RegionText.Text = region;
                RegionBadge.Visibility = Visibility.Visible;
            }
            else
            {
                PlayerName.Text = name;
                RegionBadge.Visibility = Visibility.Collapsed;
            }
            StatusLabel.Text = "● 已连接";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            // 账号切换时刷新战绩（主窗口 + 计分板）
            RefreshStats();
            _overlay?.LoadScoreRecentGames(_monitor.GetRecentGames(9));
        });
        _monitor.OnMmrChanged += mmr => Dispatcher.Invoke(() =>
        {
            PlayerMmr.Text = mmr > 0 ? $"MMR: {mmr}" : "";
            // 同步更新今日起始 MMR
            var startMmr = GameStore.GetTodayStartMmr(_monitor.PlayerName);
            if (startMmr > 0)
            {
                StatMmrStart.Text = $"起始: {startMmr}";
                _overlay?.UpdateScoreStartMmr(startMmr);
            }
            // 计分板：更新当前 MMR
            _overlay?.UpdateScoreCurrentMmr(mmr);
        });

        // 计分板：场景变化控制显示/隐藏
        _monitor.OnSceneChanged += scene => Dispatcher.Invoke(() =>
        {
            if (scene == SceneMode.BACON)
            {
                // 场景 15（战棋大厅）显示计分板，隐藏种族，隐藏拔线按钮
                _overlay?.ForceShow();
                _overlay?.ShowScoreboard();
                _overlay?.SetRacePanelVisible(false);
                _overlay?.HideGameToolsPanel();
                _overlay?.LoadScoreRecentGames(_monitor.GetRecentGames(9));
                var startMmr = GameStore.GetTodayStartMmr(_monitor.PlayerName);
                if (startMmr > 0) _overlay?.UpdateScoreStartMmr(startMmr);
            }
            else if (scene == SceneMode.GAMEPLAY)
            {
                // 场景 4：如果是从 15 过来的（15→4），一定是 BG，直接显示
                // 如果是断线重连（0→4 或 -1→4），也是 BG，直接显示
                // 如果是中途启动直接进入 4，需要验证是否为 BG
                _overlay?.ForceShow();
                var prevScene = _monitor.LastScene;
                if (prevScene == SceneMode.BACON || prevScene == SceneMode.INVALID)
                {
                    // 15→4 或 0/-1→4（断线重连），一定是战棋
                    _overlay?.ShowScoreboard();
                    _overlay?.SetRacePanelVisible(true);
                    _overlay?.ShowGameToolsPanel();
                    _overlay?.LoadScoreRecentGames(_monitor.GetRecentGames(9));
                    var startMmr = GameStore.GetTodayStartMmr(_monitor.PlayerName);
                    if (startMmr > 0) _overlay?.UpdateScoreStartMmr(startMmr);
                }
                else
                {
                    // 中途启动或其他场景→4，需要验证
                    Task.Run(() =>
                    {
                        var mode = _monitor.DetectCurrentGameMode();
                        Dispatcher.Invoke(() =>
                        {
                            if (mode == "BG")
                            {
                                _overlay?.ShowScoreboard();
                                _overlay?.SetRacePanelVisible(true);
                                _overlay?.ShowGameToolsPanel();
                                _overlay?.LoadScoreRecentGames(_monitor.GetRecentGames(9));
                                var startMmr = GameStore.GetTodayStartMmr(_monitor.PlayerName);
                                if (startMmr > 0) _overlay?.UpdateScoreStartMmr(startMmr);
                            }
                        });
                    });
                }
            }
            else
            {
                _overlay?.HideScoreboard();
                _overlay?.HideGameToolsPanel();
            }
        });

        // 计分板：游戏结束添加记录
        _monitor.OnGameEnded += record => Dispatcher.Invoke(() =>
        {
            _overlay?.AddScoreGame(record);
        });

        // 对手阵容悬停
        _monitor.OnOpponentBoardHover += (heroCardId, boardState, turnsAgo, h2hText) => Dispatcher.Invoke(() =>
        {
            _overlay?.ShowOpponentBoard(heroCardId, boardState, turnsAgo, h2hText);
        });

        // 回合数变化
        _monitor.OnTurnNumberChanged += turnNumber => Dispatcher.Invoke(() =>
        {
            _overlay?.UpdateTurnNumber(turnNumber);
        });

        // 战斗模拟结果
        _monitor.OnCombatSimulationResult += (winRate, tieRate, lossRate,
            playerDmgMin, playerDmgMax, playerDmgAvg,
            opponentDmgMin, opponentDmgMax, opponentDmgAvg) => Dispatcher.Invoke(() =>
        {
            _overlay?.ShowBobsBuddyResult(winRate, tieRate, lossRate,
                playerDmgMin, playerDmgMax, playerDmgAvg,
                opponentDmgMin, opponentDmgMax, opponentDmgAvg);
        });

        // 启动监控
        _monitor.Start();
        AppendLog("GameMonitorService 已启动");

        // 初始加载战绩
        RefreshStats();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 淡出动画
        e.Cancel = true;
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) => Environment.Exit(0);
        BeginAnimation(OpacityProperty, fadeOut);
    }

    // ═══════════════════════════════════════
    //  UI 更新
    // ═══════════════════════════════════════

    private void UpdatePhaseUI(GamePhase phase, Game game)
    {
        switch (phase)
        {
            case GamePhase.Idle:
                GameIcon.Text = "⏳";
                GameText.Text = "等待对局中...";
                GameSub.Text = "联赛模式已启用";
                break;

            case GamePhase.PreLobby:
                GameIcon.Text = "🎮";
                GameText.Text = string.IsNullOrEmpty(game.HeroName) ? "准备中..." : game.HeroName;
                GameSub.Text = "正在加载对局信息...";
                break;

            case GamePhase.Lobby:
                GameIcon.Text = "🔍";
                GameText.Text = string.IsNullOrEmpty(game.HeroName) ? "联赛检查中..." : game.HeroName;
                GameSub.Text = "正在检查...";
                break;

            case GamePhase.Active:
                GameIcon.Text = "⚔️";
                GameText.Text = string.IsNullOrEmpty(game.HeroName) ? (game.IsLeagueGame ? "联赛对局进行中" : "对局进行中") : game.HeroName;
                GameSub.Text = game.IsLeagueGame ? "联赛对局进行中" : "对局进行中";
                break;

            case GamePhase.PostGame:
                GameIcon.Text = "📊";
                GameText.Text = $"第 {game.HeroPlacement} 名";
                GameSub.Text = "正在上传成绩...";
                break;
        }
    }

    private void RefreshStats()
    {
        if (_monitor == null) return;

        // 最近战绩
        var recent = _monitor.GetRecentGames(5);
        var items = new List<GameDisplayItem>();
        foreach (var r in recent)
        {
            var change = r.RatingChange;
            var changeText = change == 0 ? "±0" : $"{change:+#;-#;0}";
            var changeColor = change > 0 ? "#22c55e" : change < 0 ? "#ef4444" : "#94a3b8";
            items.Add(new GameDisplayItem
            {
                PlacementText = r.Placement.ToString(),
                PlacementColor = GetPlacementColor(r.Placement),
                HeroName = r.HeroName,
                PointsText = changeText,
                PointsColor = changeColor,
            });
        }
        RecentGamesList.ItemsSource = items;

        // 今日统计
        var today = _monitor.GetTodayGames();
        StatGames.Text = $"总局数 {today.Count}";
        if (today.Count > 0)
        {
            var top4 = today.Count(r => r.Placement <= 4);
            StatWinRate.Text = $"胜率 {top4 * 100 / today.Count}%";
            StatAvgPlace.Text = $"场均排名 {today.Average(r => r.Placement):F1}";

            // MMR 变动（当前 MMR - 起始 MMR，比叠加更准确）
            var startMmr = GameStore.GetTodayStartMmr(_monitor.PlayerName);
            var currentMmr = _monitor.CurrentMmr;
            var totalMmr = (startMmr > 0 && currentMmr > 0) ? currentMmr - startMmr : today.Sum(r => r.RatingChange);
            StatMmrChange.Text = $"MMR: {totalMmr:+#;-#;0}";
            StatMmrChange.Foreground = new SolidColorBrush(
                totalMmr > 0 ? Color.FromRgb(0x22, 0xc5, 0x5e) :
                totalMmr < 0 ? Color.FromRgb(0xef, 0x44, 0x44) :
                Color.FromRgb(0x94, 0xa3, 0xb8));

            // 起始 MMR
            StatMmrStart.Text = startMmr > 0 ? $"起始: {startMmr}" : "起始: -";

            // 吃鸡 / 速8
            var firstCount = today.Count(r => r.Placement == 1);
            var eighthCount = today.Count(r => r.Placement == 8);
            StatFirst.Text = firstCount.ToString();
            StatFirstRate.Text = $" {firstCount * 100 / today.Count}%";
            StatEighth.Text = eighthCount.ToString();
            StatEighthRate.Text = $" {eighthCount * 100 / today.Count}%";
        }
        else
        {
            StatWinRate.Text = "胜率 0%";
            StatAvgPlace.Text = "场均排名 -";
            StatMmrChange.Text = "MMR: -";
            StatMmrStart.Text = "起始: -";
            StatFirst.Text = "0";
            StatFirstRate.Text = " 0%";
            StatEighth.Text = "0";
            StatEighthRate.Text = " 0%";
        }
    }

    private static string GetPlacementColor(int placement) => placement switch
    {
        1 => "#f59e0b",  // 金
        2 => "#94a3b8",  // 银
        3 => "#cd7f32",  // 铜
        <= 4 => "#22c55e", // 绿
        _ => "#64748b",  // 灰
    };

    private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
    {
        var code = VerifyCode.Text;
        if (!string.IsNullOrEmpty(code) && code != "待接入")
        {
            Clipboard.SetText(code);
            BtnCopyCode.Content = "✅ 已复制";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, _) => { BtnCopyCode.Content = "📋 复制"; timer.Stop(); };
            timer.Start();
        }
    }

    // ═══════════════════════════════════════
    //  日志
    // ═══════════════════════════════════════

    private void AppendLog(string msg)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{timestamp}] {msg}\n");
        LogBox.ScrollToEnd();

        _logLineCount++;
        if (_logLineCount > 200)
        {
            var text = LogBox.Text;
            var idx = text.IndexOf('\n', text.Length / 2);
            if (idx > 0) LogBox.Text = text.Substring(idx + 1);
            _logLineCount = 100;
        }
    }
}

/// <summary>
/// 最近战绩显示模型
/// </summary>
public class GameDisplayItem
{
    public string PlacementText { get; set; } = "";
    public string PlacementColor { get; set; } = "#64748b";
    public string HeroName { get; set; } = "";
    public string PointsText { get; set; } = "";
    public string PointsColor { get; set; } = "#94a3b8";
}

/// <summary>
/// 卡牌显示模型（含关键词标签 + 图片）
/// </summary>
public class MinionDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    private readonly BgdbCard _m;
    public MinionDisplayItem(BgdbCard m) { _m = m; }
    public string CardId => _m.CardId ?? "";
    public string Name => _m.NameZh ?? _m.Name ?? "";
    public string NameEn => _m.Name ?? "";
    public int Tier => _m.Tier ?? 0;
    public string Race => CardDatabaseService.GetRaceChinese(_m.MinionType ?? "");
    public int Attack => _m.Attack;
    public int Health => _m.Health;
    public List<string> Keywords => CardDatabaseService.GetKeywords(_m);

    private System.Windows.Media.Imaging.BitmapImage _image;
    public System.Windows.Media.Imaging.BitmapImage Image
    {
        get => _image;
        set { _image = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Image))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}
}
