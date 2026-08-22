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

    /// <summary>种族 → 图标文件名（HDT 同款，Beast 即 pet.jpg）</summary>
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

    private readonly CardDatabaseService _db = new();

    // 与主工程 OverlayWindow 相同的 256x 图源；原图解码 + 跳过 ETag 校验（批量浏览提速）
    private readonly ImageCacheService _imgCache256 = new("256x", decodePixelWidth: 0, verifyEtags: false);

    private int? _activeTier;
    private string _activeRace;   // null=全部, Title Case 种族, "NEUTRAL"

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // 数据库加载较重，移出 UI 线程避免窗口假死
            await Task.Run(() => _db.EnsureLoaded());
            BuildTierFilters();
            BuildRaceFilters();
            ApplyFilters();
        };
    }

    // ═══════════════════════════════════════════════
    //  筛选器构建
    // ═══════════════════════════════════════════════

    private void BuildTierFilters()
    {
        for (int tier = 1; tier <= 7; tier++)
        {
            var icon = new Image
            {
                Source = new ImageSourceConverter().ConvertFromString(
                    $"pack://application:,,,/Resources/Tiers/tier-{tier}.png") as ImageSource,
                Width = 76,
            };
            var btn = new ToggleButton { Style = (Style)FindResource("TierBtn"), Content = icon, Tag = tier };
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

        // 分组顺序: 全部(All) → 中立 → 各种族（与游戏内浏览器一致）
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
                Cards = g.OrderBy(m => m.Tier ?? 0).ThenBy(m => m.NameZh ?? "").Select(CreateMinionVM).ToList(),
            })
            .Where(s => s.Cards.Count > 0)
            .ToList();

        SectionsView.ItemsSource = sections;
        _ = LoadImagesAsync(sections.SelectMany(s => s.Cards).ToList());
    }

    private MinionVM CreateMinionVM(BgdbCard m)
    {
        var kws = m.Keywords ?? new List<string>();
        bool Has(string k) => kws.Contains(k);

        return new MinionVM
        {
            CardId = m.CardId ?? "",
            Name = $"{m.NameZh ?? m.Name} （{CardDatabaseService.GetRaceChinese(m.MinionType ?? "")}）",
            TierIcon = $"pack://application:,,,/Resources/Tiers/tier-{Math.Max(1, m.Tier ?? 1)}.png",
            TauntVis = Has("Taunt") ? Visibility.Visible : Visibility.Collapsed,
            RebornVis = Has("Reborn") ? Visibility.Visible : Visibility.Collapsed,
            DeathrattleVis = Has("Deathrattle") ? Visibility.Visible : Visibility.Collapsed,
            PoisonousVis = Has("Poisonous") ? Visibility.Visible : Visibility.Collapsed,
            VenomousVis = Has("Venomous") ? Visibility.Visible : Visibility.Collapsed,
            DivineShieldVis = Has("Divine Shield") ? Visibility.Visible : Visibility.Collapsed,
            AtkText = FormatStat(m.Attack),
            HpText = FormatStat(m.Health),
            AtkBrush = Brushes.White,
            HpBrush = Brushes.White,
        };
    }

    /// <summary>大数值缩写（与 BoardRenderer.FormatStat 一致）</summary>
    private static string FormatStat(int value)
    {
        if (value < 100000) return value.ToString();
        if (value < 10000000) return $"{value / 10000.0:0.#}万";
        return $"{value / 100000000.0:0.##}亿";
    }

    private async Task LoadImagesAsync(List<MinionVM> items)
    {
        // 已缓存的同步上屏；未缓存的并发下载（限 8 路）
        using var gate = new System.Threading.SemaphoreSlim(8);
        var tasks = items.Select(async item =>
        {
            var cached = _imgCache256.GetTileOrPlaceholder(item.CardId);
            if (cached != _imgCache256.Placeholder)
            {
                item.Image = cached;
                return;
            }
            try
            {
                await gate.WaitAsync();
                try { item.Image = await _imgCache256.GetTileAsync(item.CardId); }
                finally { gate.Release(); }
            }
            catch { /* 网络失败保留占位符 */ }
        });
        await Task.WhenAll(tasks);
    }

    // ═══════════════════════════════════════════════
    //  视图模型
    // ═══════════════════════════════════════════════

    public class Section
    {
        public string Title { get; set; } = "";
        public List<MinionVM> Cards { get; set; } = new();
    }

    public class MinionVM : INotifyPropertyChanged
    {
        private ImageSource _image;

        public string CardId { get; set; } = "";
        public string Name { get; set; } = "";
        public string TierIcon { get; set; } = "";
        public string AtkText { get; set; } = "";
        public string HpText { get; set; } = "";
        public Brush AtkBrush { get; set; } = Brushes.White;
        public Brush HpBrush { get; set; } = Brushes.White;

        public Visibility TauntVis { get; set; }
        public Visibility RebornVis { get; set; }
        public Visibility DeathrattleVis { get; set; }
        public Visibility PoisonousVis { get; set; }
        public Visibility VenomousVis { get; set; }
        public Visibility DivineShieldVis { get; set; }

        public ImageSource Image
        {
            get => _image;
            set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
}
