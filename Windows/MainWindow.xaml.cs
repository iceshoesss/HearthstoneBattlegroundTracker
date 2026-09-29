using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using HBT.Services;

namespace HBT.Windows
{

public partial class MainWindow : Window
{
    private HearthMirrorService _hm;
    private GameMonitorService _monitor;
    private readonly string _logPath;
    private StreamWriter _logWriter;
    private int _logLineCount;

    public MainWindow()
    {
        InitializeComponent();
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HBT_debug.log");

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"HBT - 联赛工具 v{version?.Major}.{version?.Minor}.{version?.Build ?? 0}";

        Opacity = 0;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fadeIn);

        try
        {
            _logWriter = new StreamWriter(_logPath, append: false) { AutoFlush = true };
            _logWriter.WriteLine("=== HBT League Tool Started ===");
            Console.SetOut(_logWriter);
            Console.SetError(_logWriter);
        }
        catch { }

        _hm = new HearthMirrorService();
        var config = Config.Load();
        _monitor = new GameMonitorService(config, _hm);

        // 订阅核心事件
        _monitor.OnPhaseChanged += (phase, game) => Dispatcher.Invoke(() => UpdatePhaseUI(phase, game));
        _monitor.OnVerifyCodeChanged += code => Dispatcher.Invoke(() => VerifyCode.Text = code);
        _monitor.OnLogMessage += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _monitor.OnPlayerNameChanged += name => Dispatcher.Invoke(() =>
        {
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
        });
        _monitor.OnMmrChanged += mmr => Dispatcher.Invoke(() =>
        {
            PlayerMmr.Text = mmr > 0 ? $"MMR: {mmr}" : "";
        });

        _monitor.Start();
        AppendLog("联赛工具已启动");
    }

    private bool _fadeCloseStarted;
    private bool _realClose;

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 渐隐完成后的第二次 Close：放行，让 WPF 正常销毁窗口并自然退出应用
        if (_realClose) return;

        e.Cancel = true;
        // 渐隐进行中忽略重复关闭请求（连点 X / Alt+F4），避免动画从 1 重播
        if (_fadeCloseStarted) return;
        _fadeCloseStarted = true;

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            // 不用 Environment.Exit：进程暴毙会让分层窗口表面残留为纯黑矩形。
            // 走正常 Close → OnLastWindowClose 自然退出，后台线程均为 IsBackground 会随之结束。
            _realClose = true;
            Close();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

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

    private async void BtnRefreshCode_Click(object sender, RoutedEventArgs e)
    {
        if (BtnRefreshCode.IsEnabled == false) return;

        BtnRefreshCode.IsEnabled = false;
        BtnRefreshCode.Content = "⏳ 刷新中";
        try
        {
            var code = await System.Threading.Tasks.Task.Run(() => _monitor?.RefreshVerifyCode());
            if (!string.IsNullOrEmpty(code))
            {
                VerifyCode.Text = code;
                BtnRefreshCode.Content = "✅ 已更新";
            }
            else
            {
                BtnRefreshCode.Content = "❌ 失败";
            }
        }
        catch
        {
            BtnRefreshCode.Content = "❌ 失败";
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            BtnRefreshCode.Content = "🔄 刷新";
            BtnRefreshCode.IsEnabled = true;
            timer.Stop();
        };
        timer.Start();
    }

    // ── 侧栏展开 / 收起 ──────────────────────────────────────
    private const double SidePanelWidth = 280;
    private bool _sideOpen;
    private bool _sideAnimating;

    private void BtnJoinMatch_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(open: !_sideOpen);
    }

    private void BtnCloseSide_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel(open: false);
    }

    private void ToggleSidePanel(bool open)
    {
        if (_sideAnimating || _sideOpen == open) return;
        _sideAnimating = true;
        _sideOpen = open;

        // 主按钮文案：展开后变为「收起」
        BtnJoinMatch.Content = open ? "收起" : "参赛";
        BtnJoinMatch.Background = open
            ? new SolidColorBrush(Color.FromRgb(0x2a, 0x30, 0x40))
            : new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xeb));
        BtnJoinMatch.BorderBrush = BtnJoinMatch.Background;
        BtnJoinMatch.Foreground = open
            ? new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8))
            : Brushes.White;

        if (open)
        {
            SidePanel.Visibility = Visibility.Visible;
            SideCol.Width = new GridLength(SidePanelWidth);
        }

        var targetWidth = open ? 400 + SidePanelWidth : 400;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = Width,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        anim.Completed += (_, _) =>
        {
            if (!open)
            {
                SideCol.Width = new GridLength(0);
                SidePanel.Visibility = Visibility.Collapsed;
            }
            _sideAnimating = false;
        };
        BeginAnimation(WidthProperty, anim);
    }

    private void BtnSidePrimary_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Phase 2 — 接入 WS 报名
        SideStateText.Text = "排队中";
        SideStateSub.Text = "报名流程接入后生效";
        MatchStatusText.Text = "排队中";
        MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x3b, 0x82, 0xf6));
        AppendLog("参赛：UI 已就绪，等待接入匹配服务");
    }

    private void BtnSideSecondary_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 取消排队
    }

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
/// 卡牌显示模型（含关键词标签 + 图片）
/// </summary>
public class MinionDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    private readonly BattlegroundDB.BgdbCard _m;
    public MinionDisplayItem(BattlegroundDB.BgdbCard m) { _m = m; }
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
