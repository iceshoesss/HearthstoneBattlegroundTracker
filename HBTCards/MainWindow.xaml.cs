using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BattlegroundDB;
using HBT.Services;

namespace HBTCards
{
public partial class MainWindow : Window
{
    /// <summary>种族展示顺序（Title Case，与 BattlegroundDB 一致）</summary>
    private static readonly string[] RaceOrder =
        { "Beast", "Demon", "Dragon", "Elemental", "Mech", "Murloc", "Naga", "Pirate", "Quilboar", "Undead" };

    /// <summary>种族 → 图标文件名（Beast 暂用 pet.jpg）</summary>
    private static readonly Dictionary<string, string> TribeIcons = new()
    {
        ["Beast"] = "pet.jpg",
        ["Demon"] = "demon.jpg",
        ["Dragon"] = "dragon.jpg",
        ["Elemental"] = "elemental.jpg",
        ["Mech"] = "mech.jpg",
        ["Murloc"] = "murloc.jpg",
        ["Naga"] = "naga.jpg",
        ["Pirate"] = "pirate.jpg",
        ["Quilboar"] = "quilboar.jpg",
        ["Undead"] = "undead.jpg",
    };

    /// <summary>关键词 → 角标图标（无图标的显示文字）</summary>
    private static readonly Dictionary<string, string> KeywordIcons = new()
    {
        ["圣盾"] = "divine-shield.png",
        ["剧毒"] = "poisonous.png",
        ["烈毒"] = "venomous.png",
        ["复生"] = "reborn.png",
        ["嘲讽"] = "taunt.png",
        ["亡语"] = "deathrattle.png",
    };

    private readonly CardDatabaseService _db = new();
    private readonly ImageCacheService _imgCache = new(
        "https://art.hearthstonejson.com/v1/tiles", "hbtcards_tiles", "png");

    private int? _activeTier;
    private string _activeRace;   // null=全部, Title Case 种族, "NEUTRAL"

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { BuildTierFilters(); BuildRaceFilters(); ApplyFilters(); };
    }

    // ═══════════════════════════════════════════════
    //  筛选器构建
    // ═══════════════════════════════════════════════

    private void BuildTierFilters()
    {
        for (int tier = 1; tier <= 6; tier++)
        {
            var stars = new TextBlock
            {
                Text = new string('★', tier),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 52,
            };
            var btn = new ToggleButton { Style = (Style)FindResource("ShieldBtn"), Content = stars, Tag = tier };
            btn.Click += TierFilter_Click;
            TierRow.Children.Add(btn);
        }
    }

    private void BuildRaceFilters()
    {
        _db.EnsureLoaded();
        var available = _db.GetRaces();
        var races = RaceOrder.Where(available.Contains).ToList();

        AddCircleButton("全部种族", null, MakeCrownVisual());
        foreach (var r in races)
            AddCircleButton(CardDatabaseService.GetRaceChinese(r), r, MakeIconVisual(TribeIcons[r]));
        AddCircleButton("中立", "NEUTRAL", MakeNeutralVisual());
    }

    private void AddCircleButton(string caption, string raceCode, UIElement visual)
    {
        var btn = new ToggleButton { Style = (Style)FindResource("CircleBtn"), Tag = caption, Content = visual };
        btn.Click += (_, _) =>
        {
            if (btn.IsChecked == true)
            {
                // 组内互斥：取消其他选中项
                foreach (var c in RaceRow.Children.OfType<ToggleButton>())
                    if (c != btn) c.IsChecked = false;
                _activeRace = raceCode;
            }
            else
            {
                _activeRace = null;
            }
            ApplyFilters();
        };
        RaceRow.Children.Add(btn);
    }

    // ═══════════════════════════════════════════════
    //  圆形按钮视觉（代码构建）
    // ═══════════════════════════════════════════════

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r, g, b));

    private UIElement MakeCrownVisual()
    {
        var g = new Grid();
        g.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Brush(0x9b, 0x30, 0xc0).Color, 0),
                    new GradientStop(Brush(0x5c, 0x15, 0x68).Color, 1),
                }),
        });
        g.Children.Add(new TextBlock
        {
            Text = "♛",
            FontSize = 30,
            Foreground = Brush(0xff, 0xd7, 0x5e),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0),
        });
        return g;
    }

    private UIElement MakeIconVisual(string fileName)
    {
        var image = new Image
        {
            Source = new ImageSourceConverter().ConvertFromString(
                $"pack://application:,,,/Resources/TribeIcons/{fileName}") as ImageSource,
            Stretch = Stretch.UniformToFill,
        };
        // 圆形裁剪
        image.Loaded += (_, _) =>
        {
            image.Clip = new EllipseGeometry(
                new Point(image.ActualWidth / 2, image.ActualHeight / 2),
                image.ActualWidth / 2, image.ActualHeight / 2);
        };
        return image;
    }

    private UIElement MakeNeutralVisual()
    {
        var g = new Grid();
        g.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Brush(0x4a, 0x4a, 0x50).Color, 0),
                    new GradientStop(Brush(0x23, 0x23, 0x26).Color, 1),
                }),
        });
        g.Children.Add(new TextBlock
        {
            Text = "无",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(0x9a, 0x9a, 0xa2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    // ═══════════════════════════════════════════════
    //  等级筛选
    // ═══════════════════════════════════════════════

    private void TierFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        if (btn.IsChecked == true)
        {
            foreach (var c in TierRow.Children.OfType<ToggleButton>())
                if (c != btn) c.IsChecked = false;
            _activeTier = (int)btn.Tag;
        }
        else
        {
            _activeTier = null;
        }
        ApplyFilters();
    }

    // ═══════════════════════════════════════════════
    //  应用筛选 + 分组渲染
    // ═══════════════════════════════════════════════

    private void ApplyFilters()
    {
        var minions = _db.SearchMinions(null, null, _activeTier);

        if (_activeRace == "NEUTRAL")
            minions = minions.Where(m => string.IsNullOrEmpty(m.MinionType)).ToList();
        else if (!string.IsNullOrEmpty(_activeRace))
            minions = minions.Where(m => m.MinionType == _activeRace).ToList();

        // 分组顺序: 全部(All) → 中立 → 各种族
        int GroupOrder(string raw) => raw switch
        {
            "All" => 0,
            "" or null => 1,
            _ => 2 + Array.IndexOf(RaceOrder, raw),
        };

        var sections = minions
            .GroupBy(m => m.MinionType ?? "")
            .OrderBy(g => GroupOrder(g.Key))
            .Select(g => new Section
            {
                Title = g.Key switch
                {
                    "All" => "全部",
                    "" => "中立",
                    _ => CardDatabaseService.GetRaceChinese(g.Key),
                },
                Items = g.OrderBy(m => m.Tier ?? 0).ThenBy(m => m.NameZh ?? "").Select(ToDisplayItem).ToList(),
            })
            .Where(s => s.Items.Count > 0)
            .ToList();

        SectionsView.ItemsSource = sections;
        _ = LoadImagesAsync(sections.SelectMany(s => s.Items).ToList());
    }

    private MinionDisplayItem ToDisplayItem(BgdbCard m)
    {
        var keywords = CardDatabaseService.GetKeywords(m);
        var chips = keywords.Take(3).Select(kw => new Chip
        {
            Icon = KeywordIcons.TryGetValue(kw, out var icon)
                ? $"pack://application:,,,/Resources/Keywords/{icon}"
                : "",
            Text = KeywordIcons.ContainsKey(kw) ? "" : kw,
        }).ToList();

        return new MinionDisplayItem
        {
            CardId = m.CardId ?? "",
            Name = m.NameZh ?? m.Name ?? m.CardId ?? "",
            Attack = m.Attack.ToString(),
            Health = m.Health.ToString(),
            StarsText = new string('★', Math.Max(1, m.Tier ?? 1)),
            Chips = chips,
        };
    }

    private async Task LoadImagesAsync(List<MinionDisplayItem> items)
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
            catch { /* 网络失败保留占位符 */ }
        }
    }

    // ═══════════════════════════════════════════════
    //  视图模型
    // ═══════════════════════════════════════════════

    public class Chip
    {
        public string Icon { get; set; } = "";
        public string Text { get; set; } = "";
        public Visibility IconVis => string.IsNullOrEmpty(Icon) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility TextVis => string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class Section
    {
        public string Title { get; set; } = "";
        public List<MinionDisplayItem> Items { get; set; } = new();
    }

    public class MinionDisplayItem : INotifyPropertyChanged
    {
        private ImageSource _image;

        public string CardId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Attack { get; set; } = "";
        public string Health { get; set; } = "";
        public string StarsText { get; set; } = "";
        public List<Chip> Chips { get; set; } = new();

        public ImageSource Image
        {
            get => _image;
            set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
}
