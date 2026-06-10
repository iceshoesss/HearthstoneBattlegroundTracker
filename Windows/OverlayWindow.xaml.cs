using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private bool _scoreboardAllowed; // 当前场景是否允许显示计分板
    private bool _hsIsForeground;

    // Card browser filter state
    private string? _activeRace;
    private int? _activeTier;
    private string? _activeKeyword;

    // Disconnect tracking
    private int _disconnectCount;
    private const int MaxDisconnectCount = 9;
    private const int DisconnectTimeoutSeconds = 10;
    private Timer _disconnectTimer;

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

    // 防止 GC 回收委托
    private WinEventProc _winEventProc;

    public OverlayWindow(HearthMirrorService hm, CardDatabaseService cardDb)
    {
        _hm = hm;
        _cardDb = cardDb;
        InitializeComponent();
        LoadScoreboardSettings();

        // 位置跟踪定时器（100ms 跟踪窗口移动，比 500ms 更流畅）
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

        // 卡牌浏览器始终显示，关闭 click-through 让按钮可点击
        BobsBuddyPanel.Visibility = Visibility.Visible;
        CardBrowserPanel.Visibility = Visibility.Visible;
        SetClickThrough(false);
        LoadCardData();

        // 安装前台窗口切换钩子（即时回调，无轮询延迟）
        _winEventProc = OnForegroundChanged;
        _hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // 初始检测一次
        UpdateForegroundState();
    }

    /// <summary>
    /// Toggle WS_EX_TRANSPARENT. When click-through is ON, the overlay doesn't receive
    /// mouse events (game can be played). When OFF, buttons on the overlay work.
    /// </summary>
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

    /// <summary>前台窗口切换回调（系统即时通知）</summary>
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

            // 前台是炉石或 overlay 自身 → 保持显示
            bool shouldShow = foreground == _gameWindowHandle || foreground == _hwnd;

            if (shouldShow != _hsIsForeground)
            {
                _hsIsForeground = shouldShow;
                Dispatcher.Invoke(() => Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed);
            }
        }
        catch { }
    }

    /// <summary>强制显示 overlay（场景变化时调用，不管前台是谁）</summary>
    public void ForceShow()
    {
        Dispatcher.Invoke(() =>
        {
            _hsIsForeground = true;
            Visibility = Visibility.Visible;
        });
    }

    /// <summary>设置本局可用种族（由 GameMonitorService 调用）</summary>
    public void SetAvailableRaces(List<string> raceCodes)
    {
        Dispatcher.Invoke(() =>
        {
            _availableRaces = raceCodes;
            LoadRaceFilters();
            LoadScoreRaceIcons(raceCodes);
            LoadGameToolsRaceIcons(raceCodes);
        });
    }

    private static readonly Dictionary<string, string> RaceImageMap = new Dictionary<string, string>
    {
        {"Beast", "pet"}, {"Mech", "mech"}, {"Murloc", "murloc"}, {"Demon", "demon"},
        {"Dragon", "dragon"}, {"Pirate", "pirate"}, {"Elemental", "elemental"},
        {"Quilboar", "quilboar"}, {"Naga", "naga"}, {"Undead", "undead"},
    };

    private void LoadScoreRaceIcons(List<string> raceCodes)
    {
        ScoreRacePanel.Children.Clear();
        foreach (var code in raceCodes)
        {
            if (!RaceImageMap.TryGetValue(code, out var imgName)) continue;
            var chinese = CardDatabaseService.GetRaceChinese(code);

            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(3, 0, 3, 0) };
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = 26, Height = 26,
                Fill = new System.Windows.Media.ImageBrush(
                    new System.Windows.Media.Imaging.BitmapImage(
                        new Uri($"pack://application:,,,/Resources/TribeIcons/{imgName}.jpg"))),
            };
            var label = new TextBlock
            {
                Text = chinese, FontSize = 8, Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            };
            panel.Children.Add(ellipse);
            panel.Children.Add(label);
            ScoreRacePanel.Children.Add(panel);
        }
    }

    private void LoadGameToolsRaceIcons(List<string> raceCodes)
    {
        GameToolsRacePanel.Children.Clear();
        foreach (var code in raceCodes)
        {
            if (!RaceImageMap.TryGetValue(code, out var imgName)) continue;
            var chinese = CardDatabaseService.GetRaceChinese(code);

            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 0, 4, 0) };
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = 28, Height = 28,
                Fill = new System.Windows.Media.ImageBrush(
                    new System.Windows.Media.Imaging.BitmapImage(
                        new Uri($"pack://application:,,,/Resources/TribeIcons/{imgName}.jpg"))),
            };
            var label = new TextBlock
            {
                Text = chinese, FontSize = 9, Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            };
            panel.Children.Add(ellipse);
            panel.Children.Add(label);
            GameToolsRacePanel.Children.Add(panel);
        }
    }

    private List<string> _availableRaces;

    /// <summary>定时跟踪炉石窗口位置和大小</summary>
    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_hsIsForeground || _gameWindowHandle == IntPtr.Zero) return;
        try
        {
            // 好友列表打开时隐藏计分板
            if (_hm.IsFriendsListVisible())
            {
                if (ScoreboardPanel.Visibility == Visibility.Visible)
                    Dispatcher.Invoke(() => ScoreboardPanel.Visibility = Visibility.Collapsed);
            }
            else
            {
                // 好友列表关闭后恢复计分板显示（仅在允许的场景）
                if (ScoreboardPanel.Visibility == Visibility.Collapsed && _scoreboardAllowed && _scoreGames.Count > 0)
                    Dispatcher.Invoke(() => ScoreboardPanel.Visibility = Visibility.Visible);
            }

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

    // === BobsBuddy (read-only) ===

    public void ShowBobsBuddyResult(float winRate, float tieRate, float lossRate,
        int playerDmgMin, int playerDmgMax, float playerDmgAvg,
        int opponentDmgMin, int opponentDmgMax, float opponentDmgAvg)
    {
        System.Diagnostics.Debug.WriteLine($"[BobsBuddy] ShowBobsBuddyResult: win={winRate} tie={tieRate} loss={lossRate}");
        Dispatcher.Invoke(() =>
        {
            BobsBuddyPanel.Visibility = Visibility.Visible;
            WinRateText.Text = $"{winRate * 100:F0}%";
            TieRateText.Text = $"{tieRate * 100:F0}%";
            LossRateText.Text = $"{lossRate * 100:F0}%";

            // 我方伤害范围
            if (playerDmgMin == playerDmgMax)
                PlayerDamageText.Text = $"{playerDmgMin}";
            else
                PlayerDamageText.Text = $"{playerDmgMin}~{playerDmgMax} (avg {playerDmgAvg:F1})";

            // 对方伤害范围
            if (opponentDmgMin == opponentDmgMax)
                OpponentDamageText.Text = $"{opponentDmgMin}";
            else
                OpponentDamageText.Text = $"{opponentDmgMin}~{opponentDmgMax} (avg {opponentDmgAvg:F1})";
        });
    }

    // === Card Browser (interactive) ===

    public void ToggleCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            if (CardBrowserPanel.Visibility == Visibility.Visible)
                HideCardBrowser();
            else
                ShowCardBrowser();
        });
    }

    public void ShowCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            CardBrowserPanel.Visibility = Visibility.Visible;
            SetClickThrough(false); // allow button clicks
            LoadCardData();
        });
    }

    public void HideCardBrowser()
    {
        Dispatcher.Invoke(() =>
        {
            CardBrowserPanel.Visibility = Visibility.Collapsed;
            // 只有没有其他交互面板需要鼠标时才开启穿透
            if (ScoreboardPanel.Visibility != Visibility.Visible)
                SetClickThrough(true);
        });
    }

    private void LoadCardData()
    {
        try
        {
            _cardDb.EnsureLoaded();
            LoadRaceFilters();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadCardData error: {ex}");
        }
    }

    private static readonly string[] RaceOrder =
        { "DEMON", "QUILBOAR", "ELEMENTAL", "MECHANICAL", "MURLOC", "NAGA", "PET", "PIRATE", "DRAGON", "UNDEAD" };

    private void LoadRaceFilters()
    {
        try
        {
            _cardDb.EnsureLoaded();
            List<string> races;
            if (_availableRaces != null && _availableRaces.Count > 0)
                races = _availableRaces.ToList();
            else
                races = _cardDb.GetRaces().ToList();

            // 按固定顺序排列
            races = races.OrderBy(r => Array.IndexOf(RaceOrder, r) >= 0 ? Array.IndexOf(RaceOrder, r) : 99).ToList();

            var normal = GetBtnStyle();
            RaceRow1.Children.Clear();

            foreach (var code in races)
            {
                var name = CardDatabaseService.GetRaceChinese(code);
                var btn = new Button
                {
                    Content = name,
                    Tag = code,
                    Style = normal,
                };
                btn.Click += RaceFilter_Click;
                RaceRow1.Children.Add(btn);
            }

            // 末尾加"无种族"
            var neutralBtn = new Button
            {
                Content = "无种族",
                Tag = "NEUTRAL",
                Style = normal,
            };
            neutralBtn.Click += RaceFilter_Click;
            RaceRow1.Children.Add(neutralBtn);
        }
        catch { }
    }

    private void ApplyFilters()
    {
        try
        {
            // 未选任何筛选项 → 显示空
            if (_activeTier == null && _activeRace == null && _activeKeyword == null)
            {
                CardList.ItemsSource = new List<MinionDisplayItem>();
                return;
            }

            var filtered = _cardDb.SearchMinions(null, _activeRace, _activeTier);

            if (!string.IsNullOrEmpty(_activeKeyword))
            {
                filtered = _activeKeyword switch
                {
                    "战吼" => filtered.Where(m => m.HasKeyword("Battlecry")).ToList(),
                    "亡语" => filtered.Where(m => m.HasKeyword("Deathrattle")).ToList(),
                    "复生" => filtered.Where(m => m.HasKeyword("Reborn")).ToList(),
                    "圣盾" => filtered.Where(m => m.HasKeyword("Divine Shield")).ToList(),
                    "烈毒" => filtered.Where(m => m.HasKeyword("Venomous")).ToList(),
                    "风怒" => filtered.Where(m => m.HasKeyword("Windfury") || m.HasKeyword("Mega-Windfury")).ToList(),
                    "嘲讽" => filtered.Where(m => m.HasKeyword("Taunt")).ToList(),
                    "光环" => filtered.Where(m => m.HasKeyword("Aura")).ToList(),
                    "回合开始" => filtered.Where(m => m.HasKeyword("Start of Turn")).ToList(),
                    "回合结束" => filtered.Where(m => m.HasKeyword("End of Turn")).ToList(),
                    _ => filtered,
                };
            }

            var items = filtered.Select(m => new MinionDisplayItem(m)).ToList();
            CardList.ItemsSource = items;

            // 异步加载图片
            _ = LoadCardImagesAsync(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyFilters error: {ex}");
        }
    }

    private async Task LoadCardImagesAsync(List<MinionDisplayItem> items)
    {
        foreach (var item in items)
        {
            // 先尝试同步加载（已缓存）
            var cached = _imgCache.GetTileOrPlaceholder(item.CardId);
            if (cached != _imgCache.Placeholder)
            {
                item.Image = cached;
                continue;
            }

            // 异步下载
            try
            {
                var img = await _imgCache.GetTileAsync(item.CardId);
                item.Image = img;
            }
            catch { }
        }
    }

    private Style GetBtnStyle() => TryFindResource("FilterBtn") as Style;
    private Style GetActiveBtnStyle() => TryFindResource("FilterBtnActive") as Style;

    /// <summary>重置某个 WrapPanel 内所有 Button 的样式</summary>
    private void ResetPanelButtons(Panel panel, Style style)
    {
        foreach (var c in panel.Children)
            if (c is Button b) b.Style = style;
    }

    private void TierFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;
        if (!int.TryParse(btn.Tag?.ToString(), out var t)) return;
        var normal = GetBtnStyle();
        var active = GetActiveBtnStyle();
        if (normal == null || active == null) return;

        // Reset tier buttons (parent WrapPanel)
        if (btn.Parent is Panel p) ResetPanelButtons(p, normal);

        if (_activeTier == t)
        {
            _activeTier = null; // 取消选择
        }
        else
        {
            _activeTier = t;
            btn.Style = active;
        }
        ApplyFilters();
    }

    private void RaceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;
        var race = btn.Tag?.ToString();
        var normal = GetBtnStyle();
        var active = GetActiveBtnStyle();
        if (normal == null || active == null) return;

        // 重置种族按钮
        ResetPanelButtons(RaceRow1, normal);

        if (_activeRace == race)
        {
            _activeRace = null;
        }
        else
        {
            _activeRace = race;
            btn.Style = active;
        }
        ApplyFilters();
    }

    private void KeywordFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;
        var keyword = btn.Tag?.ToString();
        var normal = GetBtnStyle();
        var active = GetActiveBtnStyle();
        if (normal == null || active == null) return;

        // 重置两行
        ResetPanelButtons(KeywordRow1, normal);
        ResetPanelButtons(KeywordRow2, normal);

        if (_activeKeyword == keyword)
        {
            _activeKeyword = null;
        }
        else
        {
            _activeKeyword = keyword;
            btn.Style = active;
        }
        ApplyFilters();
    }

    /// <summary>
    /// 通过 VisualTreeHelper 遍历 ItemsControl 的容器，重置内部 Button 的样式
    /// </summary>
    private void ResetItemsControlButtons(ItemsControl itemsControl, Style style)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as DependencyObject;
            if (container == null) continue;
            FindAndResetButtons(container, style);
        }
    }

    private void FindAndResetButtons(DependencyObject parent, Style style)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn)
                btn.Style = style;
            else
                FindAndResetButtons(child, style);
        }
    }

    private void CloseCardBrowser_Click(object sender, RoutedEventArgs e) => HideCardBrowser();

    // === Opponent (read-only) ===

    public void ShowOpponentHistory(string opponentTag, string heroName, int placement)
    {
        Dispatcher.Invoke(() =>
        {
            OpponentPanel.Visibility = Visibility.Visible;
            OpponentInfo.Text = $"{opponentTag}\n英雄: {heroName}\n排名: {placement}";
        });
    }

    /// <summary>渲染阵容到指定面板（HDT 风格）</summary>
    /// <param name="cardSize">卡片大小（默认 120）</param>
    private List<string> RenderBoardToPanel(Panel panel, List<Dictionary<string, object>> boardState, double cardSize = 120)
    {
        _cardDb.EnsureLoaded();
        panel.Children.Clear();
        var missingCards = new List<string>();

        foreach (var minion in boardState)
        {
            var cardId = minion.ContainsKey("cardId") ? minion["cardId"]?.ToString() : "";
            if (string.IsNullOrEmpty(cardId)) continue;

            var golden = minion.ContainsKey("golden") && (bool)minion["golden"];
            var hasTaunt = minion.ContainsKey("taunt") && (bool)minion["taunt"];
            var hasDivineShield = minion.ContainsKey("divineShield") && (bool)minion["divineShield"];
            var hasReborn = minion.ContainsKey("reborn") && (bool)minion["reborn"];
            var hasDeathrattle = minion.ContainsKey("deathrattle") && (bool)minion["deathrattle"];
            var hasPoisonous = minion.ContainsKey("poisonous") && (bool)minion["poisonous"];
            var hasVenomous = minion.ContainsKey("venomous") && (bool)minion["venomous"];
            var atk = minion.ContainsKey("attack") ? Convert.ToInt32(minion["attack"]) : 0;
            var hp = minion.ContainsKey("health") ? Convert.ToInt32(minion["health"]) : 0;
            var lookupId = cardId.EndsWith("_G") ? cardId.Substring(0, cardId.Length - 2) : cardId;
            var baseCard = _cardDb.GetMinion(lookupId);
            var multiplier = golden ? 2 : 1;
            var atkBrush = (baseCard != null && atk > baseCard.Attack * multiplier)
                ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.White;
            var hpBrush = (baseCard != null && hp > baseCard.Health * multiplier)
                ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.White;

            var canvas = new Canvas { Width = 256, Height = 256 };

            // 嘲讽
            if (hasTaunt)
                canvas.Children.Add(MinionOverlay(golden ? "taunt_premium" : "taunt", -24, -36, 300, 350));

            // 卡牌肖像（使用缓存服务，替代旧的 _portraitCache）
            var portraitBmp = _imgCache256.GetTileOrPlaceholder(lookupId);
            if (portraitBmp == _imgCache256.Placeholder && !missingCards.Contains(lookupId))
                missingCards.Add(lookupId);
            var portraitRect = new System.Windows.Shapes.Rectangle
            {
                Width = 256, Height = 256,
                Fill = new System.Windows.Media.ImageBrush(portraitBmp),
                Clip = new System.Windows.Media.EllipseGeometry(
                    new System.Windows.Point(128, 128), 87, 120),
            };
            canvas.Children.Add(portraitRect);

            // 边框
            canvas.Children.Add(MinionOverlay(golden ? "border_premium" : "border", -24, -36, 300, 350));

            // 复生
            if (hasReborn)
                canvas.Children.Add(MinionOverlay("reborn", -24, -36, 300, 350));

            // 亡语/毒/烈毒
            if (hasDeathrattle)
                canvas.Children.Add(MinionOverlay("deathrattle", -24, -36, 300, 350));
            if (hasPoisonous)
                canvas.Children.Add(MinionOverlay("poisonous", -24, -36, 300, 350));
            if (hasVenomous)
                canvas.Children.Add(MinionOverlay("venomous", -24, -36, 300, 350));

            // 属性面板
            canvas.Children.Add(MinionOverlay(golden ? "stats_premium" : "stats", -24, -36, 300, 350));

            // 圣盾
            if (hasDivineShield)
                canvas.Children.Add(MinionOverlay("divine-shield", -36, -24, 325, 311));

            // 攻击力
            var (atkText, atkSize, atkWidth, atkTop) = FormatStat(atk);
            canvas.Children.Add(OutlinedText(atkText, atkSize, atkBrush, atkWidth, 75));
            Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 29);
            Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 185 + atkTop);

            // 生命值
            var (hpText, hpSize, hpWidth, hpTop) = FormatStat(hp);
            canvas.Children.Add(OutlinedText(hpText, hpSize, hpBrush, hpWidth, 75));
            Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 151);
            Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 185 + hpTop);

            // Viewbox 缩放
            var viewbox = new Viewbox { Width = cardSize, Height = cardSize, Margin = new Thickness(-2) };
            viewbox.Child = canvas;
            panel.Children.Add(viewbox);
        }
        return missingCards;
    }

    /// <summary>显示对手历史阵容（悬停排行榜时调用）</summary>
    public void ShowOpponentBoard(string heroCardId, List<Dictionary<string, object>> boardState,
        int turnsAgo = 0, string headToHeadText = "")
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(heroCardId))
            {
                OpponentPanel.Visibility = Visibility.Collapsed;
                return;
            }

            OpponentBoardPanel.Children.Clear();
            OpponentHeadToHead.Text = headToHeadText;

            if (boardState == null)
            {
                // 未遇到过该对手 - 居中显示大字
                OpponentInfo.Text = "";
                OpponentBoardPanel.Children.Add(new TextBlock
                {
                    Text = "还没有遇到过该对手",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                OpponentPanel.Visibility = Visibility.Visible;
                return;
            }

            if (boardState.Count == 0)
            {
                // 遇到过但场面无随从 - 居中显示大字
                OpponentInfo.Text = turnsAgo > 0 ? $"{turnsAgo}回合前" : "";
                OpponentBoardPanel.Children.Add(new TextBlock
                {
                    Text = "场上无随从",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                OpponentPanel.Visibility = Visibility.Visible;
                return;
            }

            // 显示阵容 - 回合信息在左下角
            OpponentInfo.Text = turnsAgo > 0 ? $"{turnsAgo}回合前" : "";
            var missing = RenderBoardToPanel(OpponentBoardPanel, boardState);
            OpponentPanel.Visibility = Visibility.Visible;

            // 异步下载缺失卡牌图片
            if (missing.Count > 0)
                _ = RefreshBoardImagesAsync(OpponentBoardPanel, boardState, missing);
        });
    }

    private void CloseOpponentPanel_Click(object sender, RoutedEventArgs e)
    {
        OpponentPanel.Visibility = Visibility.Collapsed;
    }

    // === Scoreboard Panel ===

    private readonly List<ScoreboardGameDisplay> _scoreGames = new();
    private const int MaxScoreGames = 9;

    public void ShowScoreboard()
    {
        _scoreboardAllowed = true;
        Dispatcher.Invoke(() => ScoreboardPanel.Visibility = Visibility.Visible);
    }

    public void HideScoreboard()
    {
        _scoreboardAllowed = false;
        Dispatcher.Invoke(() => ScoreboardPanel.Visibility = Visibility.Collapsed);
    }

    public void SetRacePanelVisible(bool visible)
    {
        Dispatcher.Invoke(() => ScoreRacePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed);
    }

    public void UpdateScoreStartMmr(int mmr)
    {
        Dispatcher.Invoke(() => ScoreStartMmr.Text = mmr > 0 ? mmr.ToString() : "-");
    }

    public void UpdateScoreCurrentMmr(int mmr)
    {
        Dispatcher.Invoke(() => ScoreCurrentMmr.Text = mmr > 0 ? mmr.ToString() : "-");
    }

    public void AddScoreGame(GameRecord record)
    {
        Dispatcher.Invoke(() =>
        {
            var heroName = record.HeroName ?? "";
            var heroCardId = record.HeroCardId ?? "";
            var placement = record.Placement;
            // 直接用服务器返回的 RatingChange，不自己算（startMmr 可能和 rc.OldRating 不一致）
            var mmrDelta = record.RatingChange;

            string placementText = placement switch
            {
                1 => "1st",
                2 => "2nd",
                3 => "3rd",
                _ => $"{placement}th"
            };

            var placementBrush = placement <= 4
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.LightCoral;

            if (placement == 1)
            {
                placementBrush = System.Windows.Media.Brushes.Gold;
            }

            string mmrDeltaText = mmrDelta >= 0 ? $"+{mmrDelta}" : mmrDelta.ToString();
            var mmrDeltaBrush = mmrDelta > 0
                ? System.Windows.Media.Brushes.LightGreen
                : mmrDelta < 0
                    ? System.Windows.Media.Brushes.LightCoral
                    : System.Windows.Media.Brushes.LightGray;

            // 先用缓存/占位符
            var heroImage = _imgCache256.GetTileOrPlaceholder(heroCardId);

            _scoreGames.Insert(0, new ScoreboardGameDisplay
            {
                GameUuid = record.GameUuid ?? "",
                HeroName = heroName,
                HeroCardId = heroCardId,
                HeroImage = heroImage,
                PlacementText = placementText,
                PlacementBrush = placementBrush,
                MMRDeltaText = mmrDeltaText,
                MMRDeltaBrush = mmrDeltaBrush,
            });

            while (_scoreGames.Count > MaxScoreGames)
                _scoreGames.RemoveAt(_scoreGames.Count - 1);

            ScoreGamesList.ItemsSource = null;
            ScoreGamesList.ItemsSource = _scoreGames;
        });

        // 异步下载英雄头像，完成后刷新 UI
        _ = RefreshScoreboardImagesAsync();
    }

    public void LoadScoreRecentGames(List<GameRecord> recentGames)
    {
        Dispatcher.Invoke(() =>
        {
            _scoreGames.Clear();
            foreach (var record in recentGames.Take(MaxScoreGames))
            {
                var heroName = record.HeroName ?? "";
                var heroCardId = record.HeroCardId ?? "";
                var placement = record.Placement;
                // 直接用服务器返回的 RatingChange
                var mmrDelta = record.RatingChange;

                string placementText = placement switch
                {
                    1 => "1st",
                    2 => "2nd",
                    3 => "3rd",
                    _ => $"{placement}th"
                };

                var placementBrush = placement <= 4
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.LightCoral;

                if (placement == 1)
                {
                    placementBrush = System.Windows.Media.Brushes.Gold;
                }

                string mmrDeltaText = mmrDelta >= 0 ? $"+{mmrDelta}" : mmrDelta.ToString();
                var mmrDeltaBrush = mmrDelta > 0
                    ? System.Windows.Media.Brushes.LightGreen
                    : mmrDelta < 0
                        ? System.Windows.Media.Brushes.LightCoral
                        : System.Windows.Media.Brushes.LightGray;

                // 先用缓存/占位符
                var heroImage = _imgCache256.GetTileOrPlaceholder(heroCardId);

                _scoreGames.Add(new ScoreboardGameDisplay
                {
                    GameUuid = record.GameUuid ?? "",
                    HeroName = heroName,
                    HeroCardId = heroCardId,
                    HeroImage = heroImage,
                    PlacementText = placementText,
                    PlacementBrush = placementBrush,
                    MMRDeltaText = mmrDeltaText,
                    MMRDeltaBrush = mmrDeltaBrush,
                });
            }

            ScoreGamesList.ItemsSource = null;
            ScoreGamesList.ItemsSource = _scoreGames;
        });

        // 异步下载英雄头像，完成后刷新 UI
        _ = RefreshScoreboardImagesAsync();
    }

    /// <summary>异步下载计分板所有占位头像，完成后刷新 UI</summary>
    private async Task RefreshScoreboardImagesAsync()
    {
        var toRefresh = new List<ScoreboardGameDisplay>();
        foreach (var g in _scoreGames)
        {
            if (g.HeroImage == _imgCache256.Placeholder && !string.IsNullOrEmpty(g.HeroCardId))
                toRefresh.Add(g);
        }
        if (toRefresh.Count == 0) return;

        foreach (var g in toRefresh)
        {
            try
            {
                var cardId = HeroNameResolver.GetBaseCardId(g.HeroCardId);
                if (string.IsNullOrEmpty(cardId)) continue;
                var img = await _imgCache256.GetTileAsync(cardId);
                if (img != _imgCache256.Placeholder)
                {
                    g.HeroImage = img;
                }
            }
            catch { }
        }

        // 刷新 UI
        Dispatcher.Invoke(() =>
        {
            ScoreGamesList.ItemsSource = null;
            ScoreGamesList.ItemsSource = _scoreGames;
        });
    }

    /// <summary>异步下载阵容面板缺失卡牌图片，完成后重新渲染</summary>
    private async Task RefreshBoardImagesAsync(Panel panel, List<Dictionary<string, object>> boardState, List<string> missingCards)
    {
        foreach (var cardId in missingCards)
        {
            try
            {
                await _imgCache256.GetTileAsync(cardId);
            }
            catch { }
        }
        // 重新渲染（图片已缓存）
        Dispatcher.Invoke(() => RenderBoardToPanel(panel, boardState));
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionTimer?.Stop();
        base.OnClosed(e);
    }

    // === Scoreboard Settings ===

    private bool _scoreSettingsOpen;
    private bool _scoreDragging;
    private System.Windows.Point _scoreDragStart;

    private void ScoreSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _scoreSettingsOpen = !_scoreSettingsOpen;
        
        if (_scoreSettingsOpen)
        {
            ScoreSettingsPanel.Visibility = Visibility.Visible;
            ScoreRacePanel.Visibility = Visibility.Collapsed;
            ScoreGearIcon.Visibility = Visibility.Collapsed;
            ScoreConfirmBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ScoreSettingsPanel.Visibility = Visibility.Collapsed;
            ScoreRacePanel.Visibility = Visibility.Visible;
            ScoreGearIcon.Visibility = Visibility.Visible;
            ScoreConfirmBtn.Visibility = Visibility.Collapsed;
        }
    }

    private bool _loadingSettings;

    private void ScoreScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScoreboardPanel == null || ScoreScaleText == null || _loadingSettings) return;
        var scale = e.NewValue;
        ScoreboardPanel.RenderTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        ScoreScaleText.Text = $"{scale:F1}x";
        SaveScoreboardSettings();
    }

    private void ScoreboardPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 只有设置面板打开时才允许拖动
        if (ScoreSettingsPanel.Visibility != Visibility.Visible) return;
        _scoreDragging = true;
        _scoreDragStart = e.GetPosition(ScoreboardPanel);
        ScoreboardPanel.CaptureMouse();
    }

    private void ScoreboardPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_scoreDragging) return;
        _scoreDragging = false;
        ScoreboardPanel.ReleaseMouseCapture();
        SaveScoreboardSettings();
    }

    private void ScoreboardPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_scoreDragging) return;
        var pos = e.GetPosition(this);
        ScoreboardPanel.Margin = new Thickness(
            pos.X - _scoreDragStart.X,
            pos.Y - _scoreDragStart.Y,
            0, 0);
        ScoreboardPanel.HorizontalAlignment = HorizontalAlignment.Left;
        ScoreboardPanel.VerticalAlignment = VerticalAlignment.Top;
    }

    private static string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scoreboard.json");

    private void SaveScoreboardSettings()
    {
        try
        {
            var m = ScoreboardPanel.Margin;
            double scale = ScoreScaleSlider != null ? ScoreScaleSlider.Value : 1.2;
            var json = $"{{\"left\":{(int)m.Left},\"top\":{(int)m.Top},\"scale\":{scale.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}}}";
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadScoreboardSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var text = File.ReadAllText(SettingsPath).Trim();
            if (string.IsNullOrEmpty(text)) return;

            int left = 80, top = 60;
            double scale = 1.2;

            // 简单解析
            foreach (var line in text.Split(','))
            {
                var parts = line.Split(':');
                if (parts.Length != 2) continue;
                var key = parts[0].Trim().Trim('"', ' ', '{');
                var val = parts[1].Trim().Trim('"', ' ', '}');
                switch (key)
                {
                    case "left": int.TryParse(val, out left); break;
                    case "top": int.TryParse(val, out top); break;
                    case "scale": double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale); break;
                }
            }

            _loadingSettings = true;
            ScoreboardPanel.Margin = new Thickness(left, top, 0, 0);
            ScoreboardPanel.HorizontalAlignment = HorizontalAlignment.Left;
            ScoreboardPanel.VerticalAlignment = VerticalAlignment.Top;
            ScoreScaleSlider.Value = scale;
            ScoreboardPanel.RenderTransform = new System.Windows.Media.ScaleTransform(scale, scale);
            ScoreScaleText.Text = $"{scale:F1}x";
            _loadingSettings = false;
        }
        catch { }
    }

    // === Board State Tooltip ===

    private string _hoveredGameUuid = "";

    private void ScoreGamesList_MouseMove(object sender, MouseEventArgs e)
    {
        var item = FindDataContextUnderMouse<ScoreboardGameDisplay>(ScoreGamesList, e);
        if (item == null || item.GameUuid == _hoveredGameUuid) return;
        _hoveredGameUuid = item.GameUuid;

        var record = BoardStateStore.GetByGameUuid(item.GameUuid);
        if (record == null || record.BoardState == null || record.BoardState.Count == 0)
        {
            BoardStatePopup.IsOpen = false;
            return;
        }

        // 使用公共渲染方法（计分板悬浮用小卡片）
        var missing = RenderBoardToPanel(BoardStatePanel, record.BoardState, 55);

        BoardStatePopup.IsOpen = true;

        // 异步下载缺失卡牌图片
        if (missing.Count > 0)
            _ = RefreshBoardImagesAsync(BoardStatePanel, record.BoardState, missing);
    }

    private void ScoreGamesList_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveredGameUuid = "";
        BoardStatePopup.IsOpen = false;
    }

    private static readonly System.Windows.Media.FontFamily ChunkfiveFont =
        new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), "./Resources/#Chunkfive");

    private static System.Windows.FrameworkElement OutlinedText(string text, double fontSize, System.Windows.Media.Brush fill, double width, double height)
    {
        // 黑色描边层（四个方向偏移）
        var grid = new System.Windows.Controls.Grid { Width = width, Height = height };
        double[] dx = { -1.5, 1.5, 0, 0 };
        double[] dy = { 0, 0, -1.5, 1.5 };
        for (int i = 0; i < 4; i++)
        {
            grid.Children.Add(new TextBlock
            {
                Text = text, FontSize = fontSize, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Black,
                FontFamily = ChunkfiveFont,
                Width = width, Height = height,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(dx[i], dy[i], 0, 0),
            });
        }
        // 填充层
        grid.Children.Add(new TextBlock
        {
            Text = text, FontSize = fontSize, FontWeight = FontWeights.Bold,
            Foreground = fill,
            FontFamily = ChunkfiveFont,
            Width = width, Height = height,
            TextAlignment = TextAlignment.Center,
        });
        return grid;
    }

    /// <summary>格式化属性数值：限制最多4字符，自动缩写万/亿，返回字号和宽度</summary>
    private static (string text, double fontSize, double width, double topOffset) FormatStat(int value)
    {
        if (value < 1000) return (value.ToString(), 45, 75, 0);                    // 1-3位
        if (value < 10000) return (value.ToString(), 38, 100, 4);                  // 4位
        if (value < 100000) return ($"{value / 10000.0:0.##}万", 38, 120, 4);     // 5位: a.bc万
        if (value < 1000000) return ($"{value / 10000.0:0.#}万", 38, 120, 4);      // 6位: ab.c万
        if (value < 10000000) return ($"{value / 10000}万", 38, 120, 4);           // 7位: abc万
        if (value < 100000000) return ($"{value / 10000}万", 38, 120, 4);          // 8位: abcd万
        if (value < 1000000000) return ($"{value / 100000000.0:0.##}亿", 38, 120, 4); // 9位: a.bc亿
        return ($"{value / 100000000.0:0.#}亿", 38, 120, 4);                           // 10+位: ab.c亿
    }

    private static System.Windows.Controls.Image MinionOverlay(string name, double left, double top, double width, double height)
    {
        var img = new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri($"pack://application:,,,/Resources/Minion/{name}.png")),
            Width = width, Height = height,
            RenderTransform = new System.Windows.Media.ScaleTransform(1, 1),
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(img, left);
        Canvas.SetTop(img, top);
        return img;
    }

    private static T FindDataContextUnderMouse<T>(ItemsControl itemsControl, MouseEventArgs e) where T : class
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null)
        {
            if (hit is ContentPresenter cp && cp.DataContext is T data)
                return data;
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    // === Game Tools Panel (turn number + disconnect) ===

    private int _lastDisplayedTurn;

    public void ShowGameToolsPanel()
    {
        _disconnectCount = 0;
        DisconnectService.ResetReconnectCount();
        DisconnectService.IsGameEnded = false;

        Task.Run(() =>
        {
            try
            {
                var serverInfo = _hm.GetServerInfo();
                if (serverInfo == null)
                    return;
                var tcpRow = DisconnectService.GetTcpRow(serverInfo.Value.address, serverInfo.Value.port);
                DisconnectService.CachedTcpRow = tcpRow;
            }
            catch { }
        });

        Dispatcher.Invoke(() =>
        {
            DisconnectBtn.Content = "一键拔线";
            DisconnectBtn.IsEnabled = true;
            GameToolsPanel.Visibility = Visibility.Visible;
        });
    }

    public void HideGameToolsPanel()
    {
        _disconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        DisconnectService.EndReconnect();
        Dispatcher.Invoke(() => GameToolsPanel.Visibility = Visibility.Collapsed);
    }

    public void UpdateTurnNumber(int turnNumber)
    {
        Dispatcher.Invoke(() =>
        {
            if (turnNumber != _lastDisplayedTurn)
            {
                _lastDisplayedTurn = turnNumber;
                TurnNumberText.Text = $"第{turnNumber}回合";
            }
        });
    }

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!DisconnectService.IsElevated())
            return;

        if (DisconnectService.IsGameEnded)
        {
            DisconnectBtn.Content = "拔线失败";
            return;
        }

        if (_disconnectCount >= MaxDisconnectCount)
        {
            DisconnectBtn.Content = "超过上限";
            DisconnectBtn.IsEnabled = false;
            return;
        }

        if (!DisconnectService.CachedTcpRow.HasValue)
        {
            var serverInfo = _hm.GetServerInfo();
            if (serverInfo == null)
            {
                DisconnectBtn.Content = "拔线失败";
                return;
            }
            var tcpRow = DisconnectService.GetTcpRow(serverInfo.Value.address, serverInfo.Value.port);
            DisconnectService.CachedTcpRow = tcpRow;
            if (!tcpRow.HasValue)
            {
                DisconnectBtn.Content = "拔线失败";
                return;
            }
        }

        DisconnectBtn.IsEnabled = false;
        DisconnectBtn.Content = "拔线中...";

        Task.Run(() =>
        {
            // 参考团子版：后台执行拔线 + 重试
            var error = DisconnectService.DisconnectWithRetry();

            Dispatcher.Invoke(() =>
            {
                if (error == null)
                {
                    _disconnectCount++;
                    // 启动超时定时器（参考团子版 10 秒超时）
                    _disconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _disconnectTimer = new Timer(DisconnectTimeoutCallback, null,
                        DisconnectTimeoutSeconds * 1000, Timeout.Infinite);
                }

                if (_disconnectCount >= MaxDisconnectCount)
                {
                    DisconnectBtn.Content = "超过上限";
                    DisconnectBtn.IsEnabled = false;
                }
                else
                {
                    DisconnectBtn.Content = error == null
                        ? $"一键拔线 ({_disconnectCount}/{MaxDisconnectCount})"
                        : "拔线失败";
                    DisconnectBtn.IsEnabled = true;
                }
            });
        });
    }

    /// <summary>拔线超时回调（参考团子版 DisconnectedTimeout）</summary>
    private void DisconnectTimeoutCallback(object state)
    {
        Console.WriteLine("[Disconnect] 拔线超时，结束重连状态");
        DisconnectService.EndReconnect();
    }
}
}

public class ScoreboardGameDisplay
{
    public string GameUuid { get; set; } = "";
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
    public BitmapImage HeroImage { get; set; }
    public string PlacementText { get; set; } = "";
    public System.Windows.Media.Brush PlacementBrush { get; set; } = System.Windows.Media.Brushes.White;
    public string MMRDeltaText { get; set; } = "";
    public System.Windows.Media.Brush MMRDeltaBrush { get; set; } = System.Windows.Media.Brushes.White;
}
