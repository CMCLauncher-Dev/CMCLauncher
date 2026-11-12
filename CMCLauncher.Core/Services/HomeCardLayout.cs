namespace CMCLauncher;

public static class HomeCardLayout
{
    public static bool Move(
        List<HomeCardPlacement> cards,
        string id,
        string? target = null,
        bool after = false
    )
    {
        if (id is not ("account" or "game" or "performance" or "note" or "news") || id == target)
            return false;
        var source = cards.FindIndex(c => c.Id == id);
        if (target != null && !cards.Any(c => c.Id == target))
            return false;
        var card = source >= 0 ? cards[source] : new HomeCardPlacement { Id = id, Width = 1 };
        var before = cards.Select(c => c.Id).ToArray();
        if (source >= 0)
            cards.RemoveAt(source);
        var destination =
            target == null ? cards.Count : cards.FindIndex(c => c.Id == target) + (after ? 1 : 0);
        cards.Insert(destination, card);
        return !before.SequenceEqual(cards.Select(c => c.Id));
    }
}
