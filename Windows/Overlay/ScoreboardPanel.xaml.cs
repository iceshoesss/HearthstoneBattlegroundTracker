using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{
public partial class ScoreboardPanel : UserControl
{
    private readonly ImageCacheService _imgCache256;
    private readonly CardDatabaseService _cardDb;
    private readonly List<ScoreboardGameDisplay> _scoreGames = new();
    private const int MaxScoreGames = 9;

    private bool _scoreSettingsOpen;
    private bool _scoreDragging;
    private System.Windows.Point _scoreDragMouseStart;
    private Thickness _scoreDragMarginStart;
    private string _hoveredGameUuid = "";
    private bool _loadingSettings;
    private bool _allowed; // 当前场景是否允许显示

    private static string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scoreboard.json");

    public ScoreboardPanel(ImageCacheService imgCache256, CardDatabaseService cardDb)
    {
        _imgCache256 = imgCache256;
        _cardDb = cardDb;
        InitializeComponent();
        LoadSettings();
    }

    public new void Show()
    {
        _allowed = true;
        Visibility = Visibility.Visible;
    }

    public new void Hide()
    {
        _allowed = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>由 OverlayWindow 定时调用，检查好友列表可见性</summary>
    public void OnFriendsListVisibilityChanged(bool friendsVisible)
    {
        if (friendsVisible)
        {
            if (Visibility == Visibility.Visible)
                Visibility = Visibility.Collapsed;
        }
        else
        {
            if (Visibility == Visibility.Collapsed && _allowed && _scoreGames.Count > 0)
                Visibility = Visibility.Visible;
        }
    }

    public void UpdateStartMmr(int mmr)
    {
        Dispatcher.Invoke(() => StartMmrText.Text = mmr > 0 ? mmr.ToString() : "-");
    }

    public void UpdateCurrentMmr(int mmr)
    {
        Dispatcher.Invoke(() => CurrentMmrText.Text = mmr > 0 ? mmr.ToString() : "-");
    }

    public void AddGame(GameRecord record)
    {
        Dispatcher.Invoke(() =>
        {
            var display = CreateDisplay(record);
            _scoreGames.Insert(0, display);
            while (_scoreGames.Count > MaxScoreGames)
                _scoreGames.RemoveAt(_scoreGames.Count - 1);
            RefreshList();
        });
        _ = RefreshImagesAsync();
    }

    public void LoadRecentGames(List<GameRecord> recentGames)
    {
        Dispatcher.Invoke(() =>
        {
            _scoreGames.Clear();
            foreach (var record in recentGames.Take(MaxScoreGames))
                _scoreGames.Add(CreateDisplay(record));
            RefreshList();
        });
        _ = RefreshImagesAsync();
    }

    private ScoreboardGameDisplay CreateDisplay(GameRecord record)
    {
        var heroName = record.HeroName ?? "";
        var heroCardId = record.HeroCardId ?? "";
        var placement = record.Placement;
        var mmrDelta = record.RatingChange;

        string placementText = placement switch
        {
            1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{placement}th"
        };

        var placementBrush = placement <= 4 ? Brushes.LightGreen : Brushes.LightCoral;
        if (placement == 1) placementBrush = Brushes.Gold;

        string mmrDeltaText = mmrDelta >= 0 ? $"+{mmrDelta}" : mmrDelta.ToString();
        var mmrDeltaBrush = mmrDelta > 0 ? Brushes.LightGreen
            : mmrDelta < 0 ? Brushes.LightCoral : Brushes.LightGray;

        var heroImage = _imgCache256.GetTileOrPlaceholder(heroCardId);

        return new ScoreboardGameDisplay
        {
            GameUuid = record.GameUuid ?? "",
            HeroName = heroName, HeroCardId = heroCardId, HeroImage = heroImage,
            PlacementText = placementText, PlacementBrush = placementBrush,
            MMRDeltaText = mmrDeltaText, MMRDeltaBrush = mmrDeltaBrush,
        };
    }

    private void RefreshList()
    {
        GamesList.ItemsSource = null;
        GamesList.ItemsSource = _scoreGames;
    }

    private async System.Threading.Tasks.Task RefreshImagesAsync()
    {
        var toRefresh = _scoreGames
            .Where(g => g.HeroImage == _imgCache256.Placeholder && !string.IsNullOrEmpty(g.HeroCardId))
            .ToList();
        if (toRefresh.Count == 0) return;

        foreach (var g in toRefresh)
        {
            try
            {
                var cardId = HeroNameResolver.GetBaseCardId(g.HeroCardId);
                if (string.IsNullOrEmpty(cardId)) continue;
                var img = await _imgCache256.GetTileAsync(cardId);
                if (img != _imgCache256.Placeholder) g.HeroImage = img;
            }
            catch { }
        }
        Dispatcher.Invoke(RefreshList);
    }

    // === Settings ===

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _scoreSettingsOpen = !_scoreSettingsOpen;
        SettingsPanel.Visibility = _scoreSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        GearIcon.Visibility = _scoreSettingsOpen ? Visibility.Collapsed : Visibility.Visible;
        ConfirmBtn.Visibility = _scoreSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScoreBorder == null || ScaleText == null || _loadingSettings) return;
        var scale = e.NewValue;
        ScoreBorder.RenderTransform = new ScaleTransform(scale, scale);
        ScaleText.Text = $"{scale:F1}x";
        SaveSettings();
    }

    // === Drag ===

    private void ScoreBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsPanel.Visibility != Visibility.Visible) return;
        _scoreDragging = true;

        // 记录鼠标在父容器中的起始位置和面板当前 Margin
        var parent = FindParentPanel();
        _scoreDragMouseStart = e.GetPosition(parent);
        _scoreDragMarginStart = ScoreBorder.Margin;

        ScoreBorder.CaptureMouse();
    }

    private void ScoreBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_scoreDragging) return;
        _scoreDragging = false;
        ScoreBorder.ReleaseMouseCapture();
        SaveSettings();
    }

    private void ScoreBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_scoreDragging) return;
        var parent = FindParentPanel();
        var pos = e.GetPosition(parent);
        ScoreBorder.Margin = new Thickness(
            _scoreDragMarginStart.Left + (pos.X - _scoreDragMouseStart.X),
            _scoreDragMarginStart.Top + (pos.Y - _scoreDragMouseStart.Y),
            0, 0);
        ScoreBorder.HorizontalAlignment = HorizontalAlignment.Left;
        ScoreBorder.VerticalAlignment = VerticalAlignment.Top;
    }

    private FrameworkElement FindParentPanel()
    {
        var parent = VisualTreeHelper.GetParent(ScoreBorder) as FrameworkElement;
        while (parent != null && !(parent is Panel)) parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
        return parent;
    }

    // === Settings Persistence ===

    private void SaveSettings()
    {
        try
        {
            var m = ScoreBorder.Margin;
            double scale = ScaleSlider?.Value ?? 1.2;
            var json = $"{{\"left\":{(int)m.Left},\"top\":{(int)m.Top},\"scale\":{scale.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}}}";
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var text = File.ReadAllText(SettingsPath).Trim();
            if (string.IsNullOrEmpty(text)) return;

            int left = 80, top = 60;
            double scale = 1.2;

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
            ScoreBorder.Margin = new Thickness(left, top, 0, 0);
            ScoreBorder.HorizontalAlignment = HorizontalAlignment.Left;
            ScoreBorder.VerticalAlignment = VerticalAlignment.Top;
            ScaleSlider.Value = scale;
            ScoreBorder.RenderTransform = new ScaleTransform(scale, scale);
            ScaleText.Text = $"{scale:F1}x";
            _loadingSettings = false;
        }
        catch { }
    }

    // === Board State Tooltip ===

    private void GamesList_MouseMove(object sender, MouseEventArgs e)
    {
        var item = FindDataContextUnderMouse<ScoreboardGameDisplay>(GamesList, e);
        if (item == null || item.GameUuid == _hoveredGameUuid) return;
        _hoveredGameUuid = item.GameUuid;

        var record = BoardStateStore.GetByGameUuid(item.GameUuid);
        if (record == null || record.BoardState == null || record.BoardState.Count == 0)
        {
            BoardStatePopup.IsOpen = false;
            return;
        }

        BoardRenderer.RenderBoardToPanel(BoardStatePanel, record.BoardState, _cardDb, _imgCache256, 55);
        BoardStatePopup.IsOpen = true;

        // Check which cards still need images
        var missing = record.BoardState
            .Where(m => m.ContainsKey("cardId"))
            .Select(m => m["cardId"]?.ToString() ?? "")
            .Where(id => !string.IsNullOrEmpty(id) && _imgCache256.GetTileOrPlaceholder(id.EndsWith("_G") ? id.Substring(0, id.Length - 2) : id) == _imgCache256.Placeholder)
            .Distinct().ToList();
        if (missing.Count > 0)
            _ = BoardRenderer.RefreshBoardImagesAsync(BoardStatePanel, record.BoardState, missing, _cardDb, _imgCache256);
    }

    private void GamesList_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveredGameUuid = "";
        BoardStatePopup.IsOpen = false;
    }

    private static T FindDataContextUnderMouse<T>(ItemsControl itemsControl, MouseEventArgs e) where T : class
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null)
        {
            if (hit is ContentPresenter cp && cp.DataContext is T data)
                return data;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
}

public class ScoreboardGameDisplay
{
    public string GameUuid { get; set; } = "";
    public string HeroName { get; set; } = "";
    public string HeroCardId { get; set; } = "";
    public BitmapImage HeroImage { get; set; }
    public string PlacementText { get; set; } = "";
    public Brush PlacementBrush { get; set; } = Brushes.White;
    public string MMRDeltaText { get; set; } = "";
    public Brush MMRDeltaBrush { get; set; } = Brushes.White;
}
}
