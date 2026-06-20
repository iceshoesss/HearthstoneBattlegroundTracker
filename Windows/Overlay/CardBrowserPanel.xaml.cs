using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{
public partial class CardBrowserPanel : UserControl
{
    private readonly CardDatabaseService _cardDb;
    private readonly ImageCacheService _imgCache;
    private List<string> _availableRaces;

    private string _activeRace;
    private int? _activeTier;
    private string _activeKeyword;

    private static readonly string[] RaceOrder =
        { "DEMON", "QUILBOAR", "ELEMENTAL", "MECHANICAL", "MURLOC", "NAGA", "PET", "PIRATE", "DRAGON", "UNDEAD" };

    public CardBrowserPanel(CardDatabaseService cardDb, ImageCacheService imgCache)
    {
        _cardDb = cardDb;
        _imgCache = imgCache;
        InitializeComponent();
    }

    public new void Show()
    {
        Visibility = Visibility.Visible;
        LoadCardData();
    }

    public new void Hide()
    {
        Visibility = Visibility.Collapsed;
    }

    public void SetAvailableRaces(List<string> raceCodes)
    {
        _availableRaces = raceCodes;
        LoadRaceFilters();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

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

            races = races.OrderBy(r => Array.IndexOf(RaceOrder, r) >= 0 ? Array.IndexOf(RaceOrder, r) : 99).ToList();

            var normal = GetBtnStyle();
            RaceRow.Children.Clear();

            foreach (var code in races)
            {
                var name = CardDatabaseService.GetRaceChinese(code);
                var btn = new Button { Content = name, Tag = code, Style = normal };
                btn.Click += RaceFilter_Click;
                RaceRow.Children.Add(btn);
            }

            var neutralBtn = new Button { Content = "无种族", Tag = "NEUTRAL", Style = normal };
            neutralBtn.Click += RaceFilter_Click;
            RaceRow.Children.Add(neutralBtn);
        }
        catch { }
    }

    private void ApplyFilters()
    {
        try
        {
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
            var cached = _imgCache.GetTileOrPlaceholder(item.CardId);
            if (cached != _imgCache.Placeholder)
            {
                item.Image = cached;
                continue;
            }
            try { item.Image = await _imgCache.GetTileAsync(item.CardId); }
            catch { }
        }
    }

    private Style GetBtnStyle() => TryFindResource("FilterBtn") as Style;
    private Style GetActiveBtnStyle() => TryFindResource("FilterBtnActive") as Style;

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

        if (btn.Parent is Panel p) ResetPanelButtons(p, normal);

        if (_activeTier == t) { _activeTier = null; }
        else { _activeTier = t; btn.Style = active; }
        ApplyFilters();
    }

    private void RaceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;
        var race = btn.Tag?.ToString();
        var normal = GetBtnStyle();
        var active = GetActiveBtnStyle();
        if (normal == null || active == null) return;

        ResetPanelButtons(RaceRow, normal);

        if (_activeRace == race) { _activeRace = null; }
        else { _activeRace = race; btn.Style = active; }
        ApplyFilters();
    }

    private void KeywordFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;
        var keyword = btn.Tag?.ToString();
        var normal = GetBtnStyle();
        var active = GetActiveBtnStyle();
        if (normal == null || active == null) return;

        ResetPanelButtons(KeywordRow1, normal);
        ResetPanelButtons(KeywordRow2, normal);

        if (_activeKeyword == keyword) { _activeKeyword = null; }
        else { _activeKeyword = keyword; btn.Style = active; }
        ApplyFilters();
    }
}
}
