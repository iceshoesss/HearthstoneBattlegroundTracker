using System.Collections.Generic;

namespace Hearthstone_Deck_Tracker.Utility.Battlegrounds
{
    public class BattlegroundsLastGames
    {
        public List<GameItem> Games { get; set; } = new List<GameItem>();

        public class GameItem
        {
            public string StartTime { get; set; } = "";
            public string EndTime { get; set; } = "";
            public string Hero { get; set; } = "";
            public int Rating { get; set; }
            public int RatingAfter { get; set; }
            public int Placement { get; set; }
            public bool FriendlyGame { get; set; }
            public string Player { get; set; } = "";
            public bool Duos { get; set; }
        }
    }
}
