using System.Collections.Generic;

namespace Hearthstone_Deck_Tracker.Hearthstone.Entities
{
    public class Entity
    {
        public int Id { get; set; }
        public string CardId { get; set; } = "";
        public Dictionary<string, int> Tags { get; set; } = new Dictionary<string, int>();
        public bool IsPlayer { get; set; }
    }
}
