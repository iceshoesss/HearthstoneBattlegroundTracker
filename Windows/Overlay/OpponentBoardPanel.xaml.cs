using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BattlegroundDB;
using HBT.Services;

namespace HBT.Windows
{
public partial class OpponentBoardPanel : UserControl
{
    private readonly CardDatabaseService _cardDb;
    private readonly ImageCacheService _imgCache256;

    public OpponentBoardPanel(CardDatabaseService cardDb, ImageCacheService imgCache256)
    {
        _cardDb = cardDb;
        _imgCache256 = imgCache256;
        InitializeComponent();
    }

    public void ShowHistory(string opponentTag, string heroName, int placement)
    {
        Visibility = Visibility.Visible;
        InfoText.Text = $"{opponentTag}\n英雄: {heroName}\n排名: {placement}";
    }

    public void ShowBoard(string heroCardId, List<Dictionary<string, object>> boardState,
        int turnsAgo = 0, string headToHeadText = "")
    {
        if (string.IsNullOrEmpty(heroCardId))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        BoardPanel.Children.Clear();
        HeadToHeadText.Text = headToHeadText;

        if (boardState == null)
        {
            InfoText.Text = "";
            BoardPanel.Children.Add(new TextBlock
            {
                Text = "还没有遇到过该对手", FontSize = 16, Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Visibility = Visibility.Visible;
            return;
        }

        if (boardState.Count == 0)
        {
            InfoText.Text = turnsAgo > 0 ? $"{turnsAgo}回合前" : "";
            BoardPanel.Children.Add(new TextBlock
            {
                Text = "场上无随从", FontSize = 16, Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Visibility = Visibility.Visible;
            return;
        }

        InfoText.Text = turnsAgo > 0 ? $"{turnsAgo}回合前" : "";
        var missing = BoardRenderer.RenderBoardToPanel(BoardPanel, boardState, _cardDb, _imgCache256);
        Visibility = Visibility.Visible;

        if (missing.Count > 0)
            _ = BoardRenderer.RefreshBoardImagesAsync(BoardPanel, boardState, missing, _cardDb, _imgCache256);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }
}
}
