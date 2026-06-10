namespace Hearthstone_Deck_Tracker.Hearthstone
{
    public class Card
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Cost { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public string Text { get; set; } = "";
        public string CardClass { get; set; } = "";
        public string Rarity { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
