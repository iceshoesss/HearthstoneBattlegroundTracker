using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    // 大图预览渲染缓存（bgs 全卡渲染，与 master 查询器一致）
    private readonly ImageCacheService _renderCache = new(
        "https://art.hearthstonejson.com/v1/bgs/latest/zhCN/512x", "bgs_zhCN_512x", "png", decodePixelWidth: 256);

    private int? _activeTier;
    private string _activeRace;     // null=全部, Title Case 种族, "NEUTRAL"
    private string _activeSpecial;  // null=常规, "BUDDY"=伙伴, "TIMEWARPED"=扭曲虚空
    private string _activeKeyword;  // null=不限
    private MinionVM _pendingPreview;
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer;

    /// <summary>关键词（中文 → BgdbCard 关键词）</summary>
    private static readonly (string cn, string[] en)[] KeywordMap =
    {
        ("战吼",     new[] { "Battlecry" }),
        ("亡语",     new[] { "Deathrattle" }),
        ("复生",     new[] { "Reborn" }),
        ("圣盾",     new[] { "Divine Shield" }),
        ("烈毒",     new[] { "Venomous" }),
        ("剧毒",     new[] { "Poisonous" }),
        ("风怒",     new[] { "Windfury", "Mega-Windfury" }),
        ("嘲讽",     new[] { "Taunt" }),
        ("发动",     new[] { "Activate" }),
        ("抉择",     new[] { "Choose One" }),
        ("光环",     new[] { "Aura" }),
        ("回合开始", new[] { "Start of Turn" }),
        ("回合结束", new[] { "End of Turn" }),
    };

    public MainWindow()
    {
        InitializeComponent();
        _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _previewTimer.Tick += PreviewTimer_Tick;
        SectionsView.PreviewMouseMove += CardArea_MouseMove;
        SectionsView.MouseLeave += CardArea_MouseLeave;
        Loaded += async (_, _) =>
        {
            try
            {
                // 串行启动：先完成更新检查（可能退出重启），再加载库数据，最后构建界面。
                // 旧版 DLL 与新编译代码 API 不匹配时，必须让更新提示先于数据加载执行，
                // 否则后台线程抢先崩溃、更新提示永远弹不出来。
                await BgdbUpdateService.CheckAndPromptAsync();

                await Task.Run(() => _db.EnsureLoaded());

                BuildTierFilters();
                BuildRaceFilters();
                BuildKeywordFilters();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                App.LogCrash("启动", ex);
                MessageBox.Show($"初始化失败：{ex.Message}", "HBTCards",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        AddCircleButton("中立", "NEUTRAL", MakeIconVisual("other.jpg"));

        // ── 特殊：伙伴 / 时空扭曲（默认隐藏的随从类别；时空扭曲图标暂缺） ──
        AddCircleButton("伙伴", "BUDDY", MakeIconVisual("buddy.jpg"), special: true);
        AddCircleButton("时空扭曲", "TIMEWARPED", MakeEmptyVisual(), special: true);
    }

    private void BuildKeywordFilters()
    {
        foreach (var (cn, _) in KeywordMap)
        {
            var btn = new ToggleButton { Style = (Style)FindResource("ChipBtn"), Content = cn, Tag = cn };
            btn.Click += (_, _) =>
            {
                if (btn.IsChecked == true)
                {
                    foreach (var c in KeywordRow.Children.OfType<ToggleButton>())
                        if (c != btn) c.IsChecked = false;
                    _activeKeyword = cn;
                }
                else
                {
                    _activeKeyword = null;
                }
                ApplyFilters();
            };
            KeywordRow.Children.Add(btn);
        }
    }

    private void AddCircleButton(string caption, string raceCode, UIElement visual, bool special = false)
    {
        var btn = new ToggleButton { Style = (Style)FindResource("CircleBtn"), Tag = caption, Content = visual };
        btn.Click += (_, _) =>
        {
            if (btn.IsChecked == true)
            {
                // 类型维度组内互斥（常规种族与特殊共用一个筛选槽位）
                ClearTypeSelections(except: btn);
                if (special) { _activeSpecial = raceCode; _activeRace = null; }
                else { _activeRace = raceCode; _activeSpecial = null; }
            }
            else
            {
                _activeRace = null;
                _activeSpecial = null;
            }
            ApplyFilters();
        };
        (special ? SpecialRow : RaceRow).Children.Add(btn);
    }

    private void ClearTypeSelections(ToggleButton except = null)
    {
        foreach (var c in RaceRow.Children.OfType<ToggleButton>())
            if (c != except) c.IsChecked = false;
        foreach (var c in SpecialRow.Children.OfType<ToggleButton>())
            if (c != except) c.IsChecked = false;
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

    /// <summary>空占位：深色虚空渐变圆（图标暂缺时使用）</summary>
    private UIElement MakeEmptyVisual()
    {
        var g = new Grid();
        g.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Brush(0x3a, 0x2f, 0x52).Color, 0),
                    new GradientStop(Brush(0x17, 0x10, 0x22).Color, 1),
                }),
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
        try
        {
            List<BgdbCard> minions;

            if (_activeSpecial == "BUDDY" || _activeSpecial == "TIMEWARPED")
            {
                // 特殊类别：伙伴 / 时空扭曲（常规浏览不包含这些随从）
                minions = _db.GetSpecialMinions(_activeSpecial);
                if (_activeTier != null)
                    minions = minions.Where(m => m.Tier == _activeTier).ToList();
            }
            else
            {
                minions = _db.SearchMinions(null, null, _activeTier);

                if (_activeRace == "NEUTRAL")
                    minions = minions.Where(m => string.IsNullOrEmpty(m.MinionType)).ToList();
                else if (!string.IsNullOrEmpty(_activeRace))
                    minions = minions.Where(m => m.MinionType == _activeRace).ToList();
            }

            if (!string.IsNullOrEmpty(_activeKeyword))
            {
                var ens = KeywordMap.First(k => k.cn == _activeKeyword).en;
                minions = minions.Where(m => ens.Any(en => m.HasKeyword(en))).ToList();
            }

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
        catch (MissingMethodException ex)
        {
            // 数据 DLL 版本过旧，缺少新编译代码所需的成员
            App.LogCrash("筛选", ex);
            MessageBox.Show("BattlegroundDB 数据版本过旧，与程序不兼容。\n请删除 BattlegroundDB.dll 后重新运行以触发自动更新。",
                "HBTCards", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private MinionVM CreateMinionVM(BgdbCard m)
    {
        var kws = m.Keywords ?? new List<string>();
        bool Has(string k) => kws.Contains(k);

        return new MinionVM
        {
            CardId = m.CardId ?? "",
            GoldenCardId = _db.GetGoldenCardId(m.DbfIdGold) ?? (m.CardId ?? "") + "_G",
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
    //  悬浮大图预览（普通 + 金色）
    // ═══════════════════════════════════════════════

    private void CardArea_MouseMove(object sender, MouseEventArgs e)
    {
        var vm = FindVMUnderMouse(e);
        if (vm == null)
        {
            _previewTimer.Stop();
            CardPreviewPopup.IsOpen = false;
            _pendingPreview = null;
            return;
        }
        if (vm == _pendingPreview && CardPreviewPopup.IsOpen) return;

        _previewTimer.Stop();
        _pendingPreview = vm;
        _previewTimer.Start();
    }

    private void CardArea_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewTimer.Stop();
        _pendingPreview = null;
        CardPreviewPopup.IsOpen = false;
    }

    private void PreviewTimer_Tick(object sender, EventArgs e)
    {
        _previewTimer.Stop();
        var item = _pendingPreview;
        if (item == null) return;

        // 先同步上屏已缓存图，再异步刷新
        NormalCardImage.Source = _renderCache.GetTileOrPlaceholder(item.CardId);
        GoldenCardImage.Source = _renderCache.GetTileOrPlaceholder(item.GoldenCardId + "_triple");
        CardPreviewPopup.IsOpen = true;
        _ = LoadPreviewAsync(item);
    }

    private async Task LoadPreviewAsync(MinionVM item)
    {
        try
        {
            var normal = await _renderCache.GetTileAsync(item.CardId);
            var golden = await _renderCache.GetTileAsync(item.GoldenCardId + "_triple");
            NormalCardImage.Source = normal;
            GoldenCardImage.Source = golden;
        }
        catch { }
    }

    private static MinionVM FindVMUnderMouse(MouseEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement fe && fe.DataContext is MinionVM vm) return vm;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
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
        public string GoldenCardId { get; set; } = "";
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
