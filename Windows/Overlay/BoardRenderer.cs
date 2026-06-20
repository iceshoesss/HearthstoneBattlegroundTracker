using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{
/// <summary>
/// 静态工具类：渲染炉石随从阵容到 WPF 面板
/// 供 OpponentBoardPanel 和 ScoreboardPanel 共用
/// </summary>
public static class BoardRenderer
{
    private static readonly FontFamily ChunkfiveFont =
        new FontFamily(new Uri("pack://application:,,,/"), "./Resources/#Chunkfive");

    /// <summary>渲染阵容到指定面板（HDT 风格）</summary>
    /// <returns>缺失图片的 cardId 列表</returns>
    public static List<string> RenderBoardToPanel(
        Panel panel, List<Dictionary<string, object>> boardState,
        CardDatabaseService cardDb, ImageCacheService imgCache256,
        double cardSize = 120)
    {
        cardDb.EnsureLoaded();
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
            var baseCard = cardDb.GetMinion(lookupId);
            var multiplier = golden ? 2 : 1;
            var atkBrush = (baseCard != null && atk > baseCard.Attack * multiplier)
                ? Brushes.LimeGreen : Brushes.White;
            var hpBrush = (baseCard != null && hp > baseCard.Health * multiplier)
                ? Brushes.LimeGreen : Brushes.White;

            var canvas = new Canvas { Width = 256, Height = 256 };

            // 嘲讽
            if (hasTaunt)
                canvas.Children.Add(MinionOverlay(golden ? "taunt_premium" : "taunt", -24, -36, 300, 350));

            // 卡牌肖像
            var portraitBmp = imgCache256.GetTileOrPlaceholder(lookupId);
            if (portraitBmp == imgCache256.Placeholder && !missingCards.Contains(lookupId))
                missingCards.Add(lookupId);
            var portraitRect = new System.Windows.Shapes.Rectangle
            {
                Width = 256, Height = 256,
                Fill = new ImageBrush(portraitBmp),
                Clip = new EllipseGeometry(new Point(128, 128), 87, 120),
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

    /// <summary>异步下载阵容面板缺失卡牌图片，完成后重新渲染</summary>
    public static async System.Threading.Tasks.Task RefreshBoardImagesAsync(
        Panel panel, List<Dictionary<string, object>> boardState,
        List<string> missingCards, CardDatabaseService cardDb, ImageCacheService imgCache256)
    {
        foreach (var cardId in missingCards)
        {
            try { await imgCache256.GetTileAsync(cardId); }
            catch { }
        }
        Application.Current.Dispatcher.Invoke(() => RenderBoardToPanel(panel, boardState, cardDb, imgCache256));
    }

    public static Image MinionOverlay(string name, double left, double top, double width, double height)
    {
        var img = new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/Minion/{name}.png")),
            Width = width, Height = height,
            RenderTransform = new ScaleTransform(1, 1),
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(img, left);
        Canvas.SetTop(img, top);
        return img;
    }

    public static FrameworkElement OutlinedText(string text, double fontSize, Brush fill, double width, double height)
    {
        var grid = new Grid { Width = width, Height = height };
        double[] dx = { -1.5, 1.5, 0, 0 };
        double[] dy = { 0, 0, -1.5, 1.5 };
        for (int i = 0; i < 4; i++)
        {
            grid.Children.Add(new TextBlock
            {
                Text = text, FontSize = fontSize, FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black, FontFamily = ChunkfiveFont,
                Width = width, Height = height, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(dx[i], dy[i], 0, 0),
            });
        }
        grid.Children.Add(new TextBlock
        {
            Text = text, FontSize = fontSize, FontWeight = FontWeights.Bold,
            Foreground = fill, FontFamily = ChunkfiveFont,
            Width = width, Height = height, TextAlignment = TextAlignment.Center,
        });
        return grid;
    }

    /// <summary>格式化属性数值：限制最多4字符，自动缩写万/亿</summary>
    public static (string text, double fontSize, double width, double topOffset) FormatStat(int value)
    {
        if (value < 1000) return (value.ToString(), 45, 75, 0);
        if (value < 10000) return (value.ToString(), 38, 100, 4);
        if (value < 100000) return ($"{value / 10000.0:0.##}万", 38, 120, 4);
        if (value < 1000000) return ($"{value / 10000.0:0.#}万", 38, 120, 4);
        if (value < 10000000) return ($"{value / 10000}万", 38, 120, 4);
        if (value < 100000000) return ($"{value / 10000}万", 38, 120, 4);
        if (value < 1000000000) return ($"{value / 100000000.0:0.##}亿", 38, 120, 4);
        return ($"{value / 100000000.0:0.#}亿", 38, 120, 4);
    }
}
}
