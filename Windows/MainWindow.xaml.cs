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

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) => Environment.Exit(0);
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
