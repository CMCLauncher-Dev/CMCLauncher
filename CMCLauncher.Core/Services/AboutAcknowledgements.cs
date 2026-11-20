namespace CMCLauncher;

// 致谢条目：名称、链接、图标文件名。
public sealed record Acknowledgement(string Name, string Url, string Image);

// “关于”页面的致谢数据：贡献者与所依赖的开源项目。
public static class AboutAcknowledgements
{
    // 项目贡献者列表。
    public static IReadOnlyList<Acknowledgement> Contributors { get; } =
    [
        new("Ctoy", "https://github.com/com-in", "ctoy.png"),
        new("Fuzhu", "https://github.com/fuzhulike", "fuzhu.png"),
    ];

    // 依赖的开源项目列表。
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
