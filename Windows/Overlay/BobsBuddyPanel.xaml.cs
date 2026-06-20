using System.Windows;
using System.Windows.Controls;

namespace HBT.Windows
{
public partial class BobsBuddyPanel : UserControl
{
    public BobsBuddyPanel()
    {
        InitializeComponent();
    }

    public void ShowResult(float winRate, float tieRate, float lossRate,
        int playerDmgMin, int playerDmgMax, float playerDmgAvg,
        int opponentDmgMin, int opponentDmgMax, float opponentDmgAvg)
    {
        Visibility = Visibility.Visible;
        WinRateText.Text = $"{winRate * 100:F0}%";
        TieRateText.Text = $"{tieRate * 100:F0}%";
        LossRateText.Text = $"{lossRate * 100:F0}%";

        PlayerDamageText.Text = playerDmgMin == playerDmgMax
            ? $"{playerDmgMin}"
            : $"{playerDmgMin}~{playerDmgMax} (avg {playerDmgAvg:F1})";

        OpponentDamageText.Text = opponentDmgMin == opponentDmgMax
            ? $"{opponentDmgMin}"
            : $"{opponentDmgMin}~{opponentDmgMax} (avg {opponentDmgAvg:F1})";
    }
}
}
