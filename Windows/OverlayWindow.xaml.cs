using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{

public partial class OverlayWindow : Window
{
    private readonly HearthMirrorService _hm;
    private readonly CardDatabaseService _cardDb;
    private readonly ImageCacheService _imgCache = new("tiles");
    private readonly ImageCacheService _imgCache256 = new("256x");
    private readonly DispatcherTimer _positionTimer;
    private IntPtr _gameWindowHandle;
    private IntPtr _hwnd;
    private IntPtr _hookHandle;
    private bool _clickThrough = true;
    private bool _hsIsForeground;

    // 子面板
    private BobsBuddyPanel _bobsBuddy;
    private CardBrowserPanel _cardBrowser;
    private ScoreboardPanel _scoreboard;
    private OpponentBoardPanel _opponent;
    private GameToolsPanel _gameTools;

    #region Win32
    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private struct RECT { public int Left, Top, Right, Bottom; }
    #endregion

    private WinEventProc _winEventProc;

    public OverlayWindow(HearthMirrorService hm, CardDatabaseService cardDb)
    {
        _hm = hm;
        _cardDb = cardDb;
        InitializeComponent();

        // 创建子面板（注入依赖）并添加到 OverlayGrid
        _bobsBuddy = new BobsBuddyPanel();
        SetPlacement(_bobsBuddy, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 8, 0, 0));

        _cardBrowser = new CardBrowserPanel(_cardDb, _imgCache);
        SetPlacement(_cardBrowser, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 42, 8, 0));

        _scoreboard = new ScoreboardPanel(_imgCache256, _cardDb);
        SetPlacement(_scoreboard, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(80, 60, 0, 0));

        _opponent = new OpponentBoardPanel(_cardDb, _imgCache256);
        SetPlacement(_opponent, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0));

        _gameTools = new GameToolsPanel(_hm);
        SetPlacement(_gameTools, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 8, 8, 0));

        // 初始隐藏
        _bobsBuddy.Visibility = Visibility.Collapsed;
        _cardBrowser.Visibility = Visibility.Collapsed;
        _scoreboard.Visibility = Visibility.Collapsed;
        _opponent.Visibility = Visibility.Collapsed;
        _gameTools.Visibility = Visibility.Collapsed;

        // GameTools 面板请求切换卡牌浏览器
        _gameTools.CardBrowserToggleRequested += ToggleCardBrowser;

        // 添加到 OverlayGrid
        OverlayGrid.Children.Add(_bobsBuddy);
        OverlayGrid.Children.Add(_cardBrowser);
        OverlayGrid.Children.Add(_scoreboard);
        OverlayGrid.Children.Add(_opponent);
        OverlayGrid.Children.Add(_gameTools);

        // 位置跟踪定时器
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _positionTimer.Tick += PositionTimer_Tick;
        _positionTimer.Start();

        Loaded += OverlayWindow_Loaded;
        Closed += (_, _) =>
        {
            if (_hookHandle != IntPtr.Zero) UnhookWinEvent(_hookHandle);
            _positionTimer.Stop();
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleCardBrowser();
                e.Handled = true;
            }
        };
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        _winEventProc = OnForegroundChanged;
        _hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        UpdateForegroundState();
    }

    private static void SetPlacement(UIElement element,
        HorizontalAlignment hAlign, VerticalAlignment vAlign, Thickness margin)
    {
        if (element is FrameworkElement fe)
        {
            fe.HorizontalAlignment = hAlign;
            fe.VerticalAlignment = vAlign;
            fe.Margin = margin;
        }
    }

    private void SetClickThrough(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (enable)
            SetWindowLong(_hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        else
            SetWindowLong(_hwnd, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW) & ~WS_EX_TRANSPARENT);
        _clickThrough = enable;
    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        UpdateForegroundState();
    }

    private void UpdateForegroundState()
    {
        try
        {
            var hsProcesses = Process.GetProcessesByName("Hearthstone");
            _gameWindowHandle = hsProcesses.Length > 0 ? hsProcesses[0].MainWindowHandle : IntPtr.Zero;

            if (_gameWindowHandle == IntPtr.Zero)
            {
                if (_hsIsForeground) { _hsIsForeground = false; Dispatcher.Invoke(() => Visibility = Visibility.Collapsed); }
                return;
            }

            var foreground = GetForegroundWindow();
            bool shouldShow = foreground == _gameWindowHandle || foreground == _hwnd;

            if (shouldShow != _hsIsForeground)
            {
                _hsIsForeground = shouldShow;
                Dispatcher.Invoke(() => Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed);
            }
        }
        catch { }
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_hsIsForeground || _gameWindowHandle == IntPtr.Zero) return;
        try
        {
            // 好友列表可见性 → 计分板联动
            _scoreboard.OnFriendsListVisibilityChanged(_hm.IsFriendsListVisible());

            if (GetWindowRect(_gameWindowHandle, out var r))
            {
                Left = r.Left;
                Top = r.Top;
                Width = r.Right - r.Left;
                Height = r.Bottom - r.Top;
                OverlayGrid.Width = Width;
                OverlayGrid.Height = Height;
            }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionTimer?.Stop();
        base.OnClosed(e);
    }

    // ═══════════════════════════════════════════════
    //  公共 API（MainWindow 调用，保持签名不变）
    // ═══════════════════════════════════════════════

    public void ForceShow()
    {
        Dispatcher.Invoke(() =>
        {
            _hsIsForeground = true;
            Visibility = Visibility.Visible;
        });
    }

    public void SetAvailableRaces(List<string> raceCodes)
    {
        Dispatcher.Invoke(() =>
        {
            _cardBrowser.SetAvailableRaces(raceCodes);
            _gameTools.LoadRaceIcons(raceCodes);
        });
    }

    public void ToggleCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            if (_cardBrowser.Visibility == Visibility.Visible)
            {
                _cardBrowser.Hide();
                // 只有没有其他交互面板需要鼠标时才开启穿透
                if (_scoreboard.Visibility != Visibility.Visible)
                    SetClickThrough(true);
            }
            else
            {
                _cardBrowser.Show();
                SetClickThrough(false);
            }
        });
    }

    public void ShowCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            _cardBrowser.Show();
            SetClickThrough(false);
        });
    }

    public void HideCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            _cardBrowser.Hide();
            if (_scoreboard.Visibility != Visibility.Visible)
                SetClickThrough(true);
        });
    }

    // === BobsBuddy ===

    public void ShowBobsBuddyResult(float winRate, float tieRate, float lossRate,
        int playerDmgMin, int playerDmgMax, float playerDmgAvg,
        int opponentDmgMin, int opponentDmgMax, float opponentDmgAvg)
    {
        Dispatcher.Invoke(() => _bobsBuddy.ShowResult(winRate, tieRate, lossRate,
            playerDmgMin, playerDmgMax, playerDmgAvg,
            opponentDmgMin, opponentDmgMax, opponentDmgAvg));
    }

    // === Opponent ===

    public void ShowOpponentHistory(string opponentTag, string heroName, int placement)
    {
        Dispatcher.Invoke(() => _opponent.ShowHistory(opponentTag, heroName, placement));
    }

    public void ShowOpponentBoard(string heroCardId, List<Dictionary<string, object>> boardState,
        int turnsAgo = 0, string headToHeadText = "")
    {
        Dispatcher.Invoke(() => _opponent.ShowBoard(heroCardId, boardState, turnsAgo, headToHeadText));
    }

    // === Scoreboard ===

    public void ShowScoreboard() => _scoreboard.Show();
    public void HideScoreboard() => _scoreboard.Hide();

    public void UpdateScoreStartMmr(int mmr) => _scoreboard.UpdateStartMmr(mmr);
    public void UpdateScoreCurrentMmr(int mmr) => _scoreboard.UpdateCurrentMmr(mmr);

    public void AddScoreGame(GameRecord record) => _scoreboard.AddGame(record);
    public void LoadScoreRecentGames(List<GameRecord> recentGames) => _scoreboard.LoadRecentGames(recentGames);

    // === Game Tools ===

    public void ShowGameToolsPanel()
    {
        Dispatcher.Invoke(() =>
        {
            _gameTools.Show();
            _bobsBuddy.Visibility = Visibility.Visible;
        });
    }

    public void HideGameToolsPanel()
    {
        Dispatcher.Invoke(() =>
        {
            _gameTools.Hide();
            _bobsBuddy.Visibility = Visibility.Collapsed;
        });
    }

    public void UpdateTurnNumber(int turnNumber) => _gameTools.UpdateTurnNumber(turnNumber);
}
}
