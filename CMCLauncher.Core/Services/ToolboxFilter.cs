namespace CMCLauncher;

public static class ToolboxFilter
{
    public static IReadOnlyList<string> Categories { get; } =
        Array.AsReadOnly(
            new[] { "All", "Network", "Creation tools", "World tools", "Instance tools" }
        );

    public static int Category(string route) =>
        route switch
        {
            "frpc" or "terracotta" => 1,
            "minecraft-pfp" or "pfp" or "datapack-generator" or "command-generator" => 2,
            "seed-map" => 3,
            "instance-transfer" => 4,
            _ => 0,
        };

    public static string[] SearchTerms(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool Matches(
        int selectedCategory,
        int category,
        string[] terms,
        string searchableText
    ) =>
        (selectedCategory <= 0 || selectedCategory == category)
        && terms.All(term =>
            searchableText.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        );
}
