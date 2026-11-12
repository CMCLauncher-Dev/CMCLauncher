using CMCLauncher;

static class HomeCardLayoutTests
{
    public static async Task Run(string root)
    {
        var account = new HomeCardPlacement { Id = "account", Height = 2 };
        var game = new HomeCardPlacement
        {
            Id = "game",
            Width = 1,
            Height = 3,
        };
        var note = new HomeCardPlacement { Id = "note", Width = 1 };
        var cards = new List<HomeCardPlacement> { account, game, note };
        void Check(bool value)
        {
            if (!value)
                throw new Exception("Home card placement regression");
        }
        Check(HomeCardLayout.Move(cards, "note", "account") && cards[0] == note);
        Check(HomeCardLayout.Move(cards, "note", "game", true) && cards[^1] == note);
        Check(!HomeCardLayout.Move(cards, "note"));
        Check(HomeCardLayout.Move(cards, "performance", "account", true));
        var added = cards[1];
        Check(added.Id == "performance" && added.Width == 1);
        Check(HomeCardLayout.Move(cards, "performance", "game", true));
        Check(
            cards.Count == 4
                && cards.Count(c => c.Id == "performance") == 1
                && ReferenceEquals(cards[2], added)
        );
        Check(account.Height == 2 && game.Width == 1 && game.Height == 3);
        var before = cards.ToArray();
        Check(
            !HomeCardLayout.Move(cards, "unknown")
                && !HomeCardLayout.Move(cards, "game", "missing")
                && !HomeCardLayout.Move(cards, "game", "game")
        );
        Check(cards.SequenceEqual(before));
        var store = new Store(Path.Combine(root, "home-layout"));
        store.Config.Settings.HomeCards = cards;
        await store.Save();
        var restored = new Store(store.Root).Config.Settings.HomeCards;
        Check(
            restored.Select(c => c.Id).SequenceEqual(cards.Select(c => c.Id))
                && restored.Single(c => c.Id == "game").Height == 3
        );
        Console.WriteLine(
            "PASS home card drag insertion, addition, duplicate prevention, size preservation and persistence"
        );
    }
}
