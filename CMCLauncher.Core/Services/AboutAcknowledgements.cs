namespace CMCLauncher;

public sealed record Acknowledgement(string Name, string Url, string Image);

public static class AboutAcknowledgements
{
    public static IReadOnlyList<Acknowledgement> Contributors { get; } =
    [
        new("Ctoy", "https://github.com/com-in", "ctoy.png"),
        new("Fuzhu", "https://github.com/fuzhulike", "fuzhu.png"),
    ];

    public static IReadOnlyList<Acknowledgement> Projects { get; } =
    [
        new(".NET", "https://github.com/dotnet/runtime", "dotnet.png"),
        new("WinUI 3", "https://github.com/microsoft/microsoft-ui-xaml", "winui.png"),
        new("Avalonia", "https://github.com/AvaloniaUI/Avalonia", "avalonia.png"),
        new("AngleSharp", "https://github.com/AngleSharp/AngleSharp", "anglesharp.png"),
        new("Markdig", "https://github.com/xoofx/markdig", "frp.png"),
        new("frp", "https://github.com/fatedier/frp", "frp.png"),
        new("Terracotta · © burningtnt", TerracottaManager.SourceUrl, "terracotta.png"),
        new("BMCLAPI · bangbang93", "https://bmclapidoc.bangbang93.com/", "bmclapi.png"),
    ];
}
