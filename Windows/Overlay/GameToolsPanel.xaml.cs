using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HBT.Services;

namespace HBT.Windows
{
public partial class GameToolsPanel : UserControl
{
    private readonly HearthMirrorService _hm;
    private int _lastDisplayedTurn;
    private int _disconnectCount;
    private const int MaxDisconnectCount = 9;
    private const int DisconnectTimeoutSeconds = 10;
    private Timer _disconnectTimer;

    /// <summary>请求切换卡牌浏览器（由 OverlayWindow 处理）</summary>
    public event Action CardBrowserToggleRequested;

    public GameToolsPanel(HearthMirrorService hm)
    {
        _hm = hm;
        InitializeComponent();
    }

    public void Show()
    {
        _disconnectCount = 0;
        DisconnectService.ResetReconnectCount();
        DisconnectService.IsGameEnded = false;

        Task.Run(() =>
        {
            try
            {
                var serverInfo = _hm.GetServerInfo();
                if (serverInfo == null) return;
                var tcpRow = DisconnectService.GetTcpRow(serverInfo.Value.address, serverInfo.Value.port);
                DisconnectService.CachedTcpRow = tcpRow;
            }
            catch { }
        });

        Dispatcher.Invoke(() =>
        {
            DisconnectBtn.Content = "一键拔线";
            DisconnectBtn.IsEnabled = true;
            Visibility = Visibility.Visible;
        });
    }

    public new void Hide()
    {
        _disconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        DisconnectService.EndReconnect();
        Dispatcher.Invoke(() => Visibility = Visibility.Collapsed);
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

    public void LoadRaceIcons(List<string> raceCodes)
    {
        Dispatcher.Invoke(() =>
        {
            RacePanel.Children.Clear();
            foreach (var code in raceCodes)
            {
                if (!RaceImageMap.TryGetValue(code, out var imgName)) continue;
                var chinese = CardDatabaseService.GetRaceChinese(code);

                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5, 0, 5, 0) };
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = 29, Height = 29,
                    Fill = new ImageBrush(
                        new BitmapImage(new Uri($"pack://application:,,,/Resources/TribeIcons/{imgName}.jpg"))),
                };
                var label = new TextBlock
                {
                    Text = chinese, FontSize = 10, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
                };
                panel.Children.Add(ellipse);
                panel.Children.Add(label);
                RacePanel.Children.Add(panel);
            }
        });
    }

    private static readonly Dictionary<string, string> RaceImageMap = new Dictionary<string, string>
    {
        {"Beast", "pet"}, {"Mech", "mech"}, {"Murloc", "murloc"}, {"Demon", "demon"},
        {"Dragon", "dragon"}, {"Pirate", "pirate"}, {"Elemental", "elemental"},
        {"Quilboar", "quilboar"}, {"Naga", "naga"}, {"Undead", "undead"},
    };

    private void CardBrowserToggleBtn_Click(object sender, RoutedEventArgs e)
        => CardBrowserToggleRequested?.Invoke();

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!DisconnectService.IsElevated()) return;

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
            var error = DisconnectService.DisconnectWithRetry();

            Dispatcher.Invoke(() =>
            {
                if (error == null)
                {
                    _disconnectCount++;
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

    private void DisconnectTimeoutCallback(object state)
    {
        Console.WriteLine("[Disconnect] 拔线超时，结束重连状态");
        DisconnectService.EndReconnect();
    }
}
}
