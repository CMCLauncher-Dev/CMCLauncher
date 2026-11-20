namespace CMCLauncher;

// 首页卡片布局：管理卡片（account/game/performance/note/news）的排序。
public static class HomeCardLayout
{
    // 把指定卡片移动到目标卡片之前/之后（target 为 null 时移到末尾）；
    // 卡片不存在时按默认宽度 1 新建。返回布局是否发生变化。
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
