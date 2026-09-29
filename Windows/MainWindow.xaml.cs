using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    private string _matchTicket = "";
    private MatchSessionClient _matchSession;
    private int _pendingPlacement = -1;
    private int _sseReconnects;

    /// <summary>组队完成后隐藏排队进度条</summary>
    private void SetQueueProgressVisible(bool visible)
    {
        if (SideQueuePanel == null) return;
        SideQueuePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>同桌名单：n/8 + 可逐条复制</summary>
    private void SetRoster(IList<string> names, string emptyText = "成组后展示")
    {
        if (SideRosterList == null || SideRosterText == null) return;
        var list = names ?? new List<string>();
        if (list.Count == 0)
        {
            SideRosterList.Visibility = Visibility.Collapsed;
            SideRosterList.ItemsSource = null;
            SideRosterText.Visibility = Visibility.Visible;
            SideRosterText.Text = emptyText;
            if (SideRosterCount != null) SideRosterCount.Text = "";
            return;
        }
        SideRosterText.Visibility = Visibility.Collapsed;
        SideRosterList.Visibility = Visibility.Visible;
        var items = new List<RosterItem>();
        foreach (var n in list) items.Add(new RosterItem { Name = n });
        SideRosterList.ItemsSource = items;
        if (SideRosterCount != null) SideRosterCount.Text = list.Count + " / 8";
    }

    private void BtnCopyName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var btn = sender as System.Windows.Controls.Button;
            var name = btn?.Tag as string;
            if (string.IsNullOrEmpty(name) || btn == null) return;
            Clipboard.SetText(name);
            AppendLog("已复制: " + name);
            ShowCopySuccess(btn);
        }
        catch (Exception ex)
        {
            AppendLog("复制失败: " + ex.Message);
        }
    }

    /// <summary>复制成功后按钮短暂变为 ✓ 绿色</summary>
    private void ShowCopySuccess(System.Windows.Controls.Button btn)
    {
        btn.Content = "✓";
        btn.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
        btn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
        btn.Opacity = 0.95;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        timer.Tick += (_, __) =>
        {
            timer.Stop();
            try
            {
                btn.Content = "⧉";
                btn.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
                btn.Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x29, 0x37));
                btn.Opacity = 1;
            }
            catch { /* 窗口可能已关 */ }
        };
        timer.Start();
    }

    private sealed class RosterItem
    {
        public string Name { get; set; }
    }

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

        // 关闭 HBT 时自动退出排队（方案一）；已开赛/确认名次中由服务端拒绝，不会误退
        LeaveQueueOnExit();

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

    private async void BtnSidePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (BtnSidePrimary.IsEnabled == false) return;
        BtnSidePrimary.IsEnabled = false;
        try
        {
            if (string.IsNullOrEmpty(_matchTicket))
            {
                await JoinQueueAsync();
            }
            else if (_pendingPlacement >= 0)
            {
                await ConfirmPlacementAsync(_pendingPlacement, true);
            }
            else
            {
                await LeaveQueueAsync();
            }
        }
        finally
        {
            BtnSidePrimary.IsEnabled = true;
        }
    }

    /// <summary>副按钮仅用于「名次有异议」；退排/取消只保留主按钮「退出排队」</summary>
    private async void BtnSideSecondary_Click(object sender, RoutedEventArgs e)
    {
        if (BtnSideSecondary.IsEnabled == false) return;
        BtnSideSecondary.IsEnabled = false;
        try
        {
            if (_pendingPlacement >= 0)
            {
                await ConfirmPlacementAsync(_pendingPlacement, false);
            }
        }
        finally
        {
            BtnSideSecondary.IsEnabled = true;
        }
    }

    private string ResolveBattleTag()
    {
        // 权威来源：GameMonitorService.PlayerName（Name#Number），不要用 UI 文本
        var tag = _monitor != null ? _monitor.PlayerName : null;
        return string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();
    }


    private void OnMatchStreamClosed()
    {
        if (string.IsNullOrEmpty(_matchTicket)) return;
        _sseReconnects++;
        if (_sseReconnects <= 3)
        {
            AppendLog("匹配事件流断开，自动重连 (" + _sseReconnects + "/3)");
            MarkSseDisconnected();
            StartMatchSession();
            return;
        }
        AppendLog("匹配事件流不可用，已退出排队");
        var _ = LeaveQueueAsync();
    }

    private void ApplyMatchState(string state, string tableNamesJson)
    {
        if (state == "grouped")
        {
            MatchStatusText.Text = "组队完成";
            MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            SideStateText.Text = "组队完成";
            SideStateSub.Text = "请准备进入游戏";
            SetQueueProgressVisible(false);
            SideQueueCount.Text = "8 / 8";
            var names = TryParseTableNames("tableNames\":" + (string.IsNullOrEmpty(tableNamesJson) ? "[]" : tableNamesJson));
            if (names.Count > 0)
            {
                SetRoster(names);
                SideQueueHint.Text = "同桌已分配，请进游戏";
            }
            else
            {
                SetRoster(null, "已分桌");
                SideQueueHint.Text = "同桌已分配，请进游戏";
            }
            return;
        }
        if (state == "in_game")
        {
            MatchStatusText.Text = "对局中";
            SideStateText.Text = "对局中";
            SideStateSub.Text = "结束后请确认名次";
            SetQueueProgressVisible(false);
            return;
        }
        MatchStatusText.Text = "排队中";
        MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x3b, 0x82, 0xf6));
        SideStateText.Text = "排队中";
        SideStateSub.Text = "盲配中，组队完成后显示同桌名单";
        SideQueueHint.Text = "盲配中，组队完成后显示同桌名单";
        SetQueueProgressVisible(true);
    }

    private async Task JoinQueueAsync()
    {
        try
        {
            var tag = ResolveBattleTag();
            if (string.IsNullOrWhiteSpace(tag) || tag == "待接入")
            {
                SideStateSub.Text = "请先启动炉石并读到战网 ID";
                AppendLog("参赛失败：尚未读取到选手 ID");
                return;
            }
            SideStateText.Text = "连接中…";
            SideStateSub.Text = "正在报名联赛匹配";
            BtnSidePrimary.IsEnabled = false;
            var join = await ApiClient.QueueJoinAsync(tag);
            BtnSidePrimary.IsEnabled = true;
            if (string.IsNullOrEmpty(join.Ticket))
            {
                SideStateText.Text = "报名失败";
                SideStateSub.Text = ApiClient.LastError.Length > 80 ? ApiClient.LastError.Substring(0, 80) : ApiClient.LastError;
                MatchStatusText.Text = "报名失败";
                AppendLog("报名失败: " + ApiClient.LastError);
                return;
            }
            _matchTicket = join.Ticket;
            BtnSidePrimary.Content = "退出排队";
            BtnSideSecondary.Visibility = Visibility.Collapsed;
            // 报名回包可能已是 grouped（插件中途加入）
            ApplyMatchState(join.State ?? "queued", join.TableNamesJson ?? "");
            StartMatchSession();
            _sseReconnects = 0;
            AppendLog("已报名，ticket=" + join.Ticket.Substring(0, Math.Min(8, join.Ticket.Length)) + "… state=" + join.State);
        }
        catch (Exception ex)
        {
            BtnSidePrimary.IsEnabled = true;
            SideStateText.Text = "报名异常";
            SideStateSub.Text = ex.Message;
            AppendLog("报名异常: " + ex.Message);
        }
    }

    private async Task LeaveQueueAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_matchTicket))
            {
                await ApiClient.QueueLeaveAsync(_matchTicket);
            }
            StopMatchSession();
            _matchTicket = "";
            _pendingPlacement = -1;
            MatchStatusText.Text = "未报名";
            MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
            SideStateText.Text = "未报名";
            SideStateSub.Text = "点击「参赛报名」开始";
            BtnSidePrimary.Content = "参赛报名";
            BtnSideSecondary.Visibility = Visibility.Collapsed;
            SetQueueProgressVisible(true);
            SideQueueBar.Width = 0;
            SideQueueCount.Text = "— / 8";
            SetRoster(null);
            SideConfirmText.Text = "对局结束后在此确认名次";
            SideQueueHint.Text = "盲配中，组队完成后显示同桌名单";
            AppendLog("已退出排队");
        }
        catch (Exception ex)
        {
            AppendLog("退出排队失败: " + ex.Message);
        }
    }

    /// <summary>进程/窗口退出时静默退排，避免残留在队列里</summary>
    private void LeaveQueueOnExit()
    {
        if (string.IsNullOrEmpty(_matchTicket)) return;
        try
        {
            AppendLog("退出 HBT，自动退出排队…");
            var task = ApiClient.QueueLeaveAsync(_matchTicket);
            // 短暂等待，尽量让请求发出去；超时则随进程结束（服务器另有超时清理）
            if (!task.Wait(1500))
            {
                AppendLog("退排请求未确认（进程即将退出）");
            }
        }
        catch (Exception ex)
        {
            AppendLog("退出时退排失败: " + ex.Message);
        }
        finally
        {
            try { StopMatchSession(); } catch { /* ignore */ }
            _matchTicket = "";
        }
    }

    private async Task ConfirmPlacementAsync(int placement, bool ok)
    {
        try
        {
            var done = await ApiClient.ConfirmPlacementAsync(_matchTicket, placement, ok);
            if (done)
            {
                _pendingPlacement = -1;
                SideStateText.Text = ok ? "已确认名次" : "已提交异议";
                SideStateSub.Text = ok ? $"第 {placement} 名无误，本局结束" : "已记录异议，等待处理";
                SideConfirmText.Text = ok ? "本局名次已确认" : "异议已提交";
                BtnSidePrimary.Content = "返回";
                BtnSideSecondary.Visibility = Visibility.Collapsed;
                AppendLog(ok ? $"名次确认 OK：第 {placement} 名" : $"名次异议：第 {placement} 名");
            }
            else
            {
                AppendLog("名次确认失败: " + ApiClient.LastError);
            }
        }
        catch (Exception ex)
        {
            AppendLog("名次确认异常: " + ex.Message);
        }
    }

    private void StartMatchSession()
    {
        StopMatchSession();
        MarkSseDisconnected();
        _matchSession = new MatchSessionClient(ApiClient.BaseUrl, _matchTicket)
        {
            // 绿/黄点只反映 WS 是否连着，与按钮无关
            OnOpen = () => Dispatcher.BeginInvoke(new Action(MarkSseConnected)),
            OnEventJson = json => Dispatcher.BeginInvoke(new Action(() => HandleMatchEvent(json))),
            OnClosed = () => Dispatcher.BeginInvoke(new Action(() =>
            {
                MarkSseDisconnected();
                OnMatchStreamClosed();
            })),
        };
        _matchSession.Start();
    }

    private void StopMatchSession()
    {
        try { _matchSession?.Dispose(); } catch { }
        _matchSession = null;
        MarkSseDisconnected();
    }

    private void HandleMatchEvent(string json)
    {
        AppendLog("事件: " + json);
        var type = TryParseStrField(json, "type");
        if (type == "state")
        {
            var state = TryParseStrField(json, "state");
            if (state == "grouped")
            {
                MatchStatusText.Text = "组队完成";
                MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
                SideStateText.Text = "组队完成";
                SideStateSub.Text = "请准备进入游戏";
                SetQueueProgressVisible(false);
                SideQueueCount.Text = "8 / 8";
                var names = TryParseTableNames(json);
                if (names.Count > 0)
                {
                    SetRoster(names);
                    SideQueueHint.Text = "同桌已分配，请进游戏";
                }
            }
            else if (state == "ready")
            {
                MatchStatusText.Text = "准备就绪";
                SideStateText.Text = "准备就绪";
                SideStateSub.Text = "请创建/进入房间";
                BtnSidePrimary.Content = "准备好了";
            }
            else if (state == "in_game")
            {
                MatchStatusText.Text = "对局中";
                SideStateText.Text = "对局中";
                SideStateSub.Text = "结束后请确认名次";
            }
            else if (state == "reporting")
            {
                SideStateText.Text = "收集中名次";
            }
            else if (state == "confirm")
            {
                MatchStatusText.Text = "请确认名次";
                MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
                SideStateText.Text = "请确认你的名次";
                SideConfirmText.Text = "请核对系统记录的名次，有问题可提交异议";
                BtnSidePrimary.Content = "名次无误";
                BtnSideSecondary.Content = "有异议";
                BtnSideSecondary.Visibility = Visibility.Visible;
            }
            else if (state == "settled")
            {
                MatchStatusText.Text = "已结算";
                SideStateText.Text = "已结算";
                BtnSidePrimary.Content = "返回";
            }
        }
        else if (type == "queue_count" || type == "ping")
        {
            if (type == "queue_count")
            {
                // 组队完成后不再显示排队进度
                if (SideQueuePanel != null && SideQueuePanel.Visibility != Visibility.Visible) return;
                var count = TryParseIntField(json, "count");
                var pool = TryParseIntField(json, "poolQueued");
                var minPlayers = TryParseIntField(json, "minPlayers");
                if (minPlayers <= 0) minPlayers = 8;
                if (count < 0) count = 0;
                // 排队中优先显示真实排队池人数（与网页一致）
                if (pool >= 0 && (MatchStatusText.Text == "排队中" || count < minPlayers))
                {
                    if (pool <= count || MatchStatusText.Text == "排队中") count = pool;
                }
                SideQueueCount.Text = count + " / " + minPlayers;
                var barWidth = Math.Min(220.0, 220.0 * count / minPlayers);
                SideQueueBar.Width = barWidth;
                if (_pendingPlacement < 0 && string.IsNullOrEmpty(_matchTicket) == false && MatchStatusText.Text == "排队中")
                {
                    SideQueueHint.Text = count >= minPlayers
                        ? "已满员，正在分桌…"
                        : "盲配中，组队完成后显示同桌名单";
                }
            }
        }
        else if (type == "table_update" || type == "roster")
        {
            // roster: {type, names[], tags[], count}  table_update: {tableNames, tableTags, ...}
            var names = type == "roster" ? TryParseNamedArray(json, "names") : TryParseNamedArray(json, "tableNames");
            var tags = type == "roster" ? TryParseNamedArray(json, "tags") : TryParseNamedArray(json, "tableTags");
            var list = tags.Count >= names.Count && tags.Count > 0 ? tags : names;
            if (list.Count == 0) list = names;
            SetRoster(list, "（空）");
            SideQueueHint.Text = "同桌名单已更新（含新补入 / 已退出）";
            SideQueueCount.Text = list.Count + " / 8";
            SetQueueProgressVisible(false);
            if (SideStateText.Text == "排队中" || SideStateText.Text == "已成组" || SideStateText.Text == "组队完成")
            {
                SideStateText.Text = "组队完成";
                SideStateSub.Text = "请准备进入游戏";
                MatchStatusText.Text = "组队完成";
                MatchStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            }
        }
        else if (type == "confirm_placement")
        {
            var placement = TryParseIntField(json, "yourPlacement");
            if (placement >= 0)
            {
                _pendingPlacement = placement;
                SideStateSub.Text = "系统记录：第 " + placement + " 名";
                SideConfirmText.Text = "请确认第 " + placement + " 名是否正确";
            }
        }
    }

    /// <summary>联赛匹配左侧圆点：绿=事件流已连接，黄=未连接/重连中</summary>
    private void MarkSseConnected()
    {
        if (SseDot == null) return;
        SseDot.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
    }

    private void MarkSseDisconnected()
    {
        if (SseDot == null) return;
        SseDot.Background = new SolidColorBrush(Color.FromRgb(0xe2, 0xb7, 0x14));
    }

    private static List<string> TryParseNamedArray(string json, string key)
    {
        var list = new List<string>();
        var needle = "\"" + key + "\":";
        var idx = json.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return list;
        var bracket = json.IndexOf('[', idx + needle.Length);
        if (bracket < 0) return list;
        var end = json.IndexOf(']', bracket + 1);
        if (end < 0) return list;
        var arr = json.Substring(bracket + 1, end - bracket - 1);
        foreach (var part in arr.Split(','))
        {
            var s = part.Trim().Trim('"');
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    private static List<string> TryParseTableNames(string json)
    {
        var list = new List<string>();
        const string key = "\"tableNames\":";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return list;
        var bracket = json.IndexOf('[', idx + key.Length);
        if (bracket < 0) return list;
        var end = json.IndexOf(']', bracket + 1);
        if (end < 0) return list;
        var arr = json.Substring(bracket + 1, end - bracket - 1);
        foreach (var part in arr.Split(','))
        {
            var s = part.Trim().Trim('"');
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    private static string TryParseStrField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var key = "\"" + field + "\":";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return "";
        var i = idx + key.Length;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '"') return "";
        i++;
        var sb = new System.Text.StringBuilder();
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i += 2; continue; }
            if (c == '"') break;
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static int TryParseIntField(string json, string field)
    {
        var key = "\"" + field + "\":";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return -1;
        var i = idx + key.Length;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        var start = i;
        if (i < json.Length && json[i] == '-') i++;
        while (i < json.Length && char.IsDigit(json[i])) i++;
        if (i <= start || (i == start + 1 && json[start] == '-')) return -1;
        int val;
        return int.TryParse(json.Substring(start, i - start), out val) ? val : -1;
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
