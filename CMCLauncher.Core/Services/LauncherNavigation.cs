namespace CMCLauncher;

public sealed record NavigationEntry(
    string Title,
    string Glyph,
    string IconPath,
    string WinUIRoute,
    string AvaloniaRoute
);

public sealed record NavigationGroup(
    string Title,
    string Key,
    string Glyph,
    string IconPath,
    IReadOnlyList<NavigationEntry> Entries
);

// One menu definition keeps labels, ordering and destinations aligned across desktop frontends.
public static class LauncherNavigation
{
    public const string HomeIcon =
        "M3,10 L12,3 L21,10 M5,9 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9";
    public const string SettingsIcon =
        "M9,3 L15,3 L16,7 L20,9 L20,15 L16,17 L15,21 L9,21 L8,17 L4,15 L4,9 L8,7 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8";
    public const string DownloadIcon = "M12,3 L12,16 M7,11 L12,16 L17,11 M4,19 L4,21 L20,21 L20,19";
    public const string BoxIcon =
        "M3,7 L12,3 L21,7 L21,18 L12,22 L3,18 Z M3,7 L12,11 L21,7 M12,11 L12,22";
    public const string FileIcon =
        "M5,2 L14,2 L19,7 L19,22 L5,22 Z M14,2 L14,7 L19,7 M8,12 L16,12 M8,16 L16,16";
    public const string MoreIcon =
        "M4,10 A2,2 0 1 0 4,14 A2,2 0 1 0 4,10 M12,10 A2,2 0 1 0 12,14 A2,2 0 1 0 12,10 M20,10 A2,2 0 1 0 20,14 A2,2 0 1 0 20,10";
    public const string TaskIcon =
        "M3,3 L21,3 L21,21 L3,21 Z M5,13 L8,13 L10,7 L13,18 L16,11 L19,11";
    public static IReadOnlyList<NavigationGroup> Groups { get; } =
        Array.AsReadOnly(
            new[]
            {
                new NavigationGroup(
                    "下载",
                    "downloads",
                    "\uE896",
                    DownloadIcon,
                    Array.AsReadOnly(
                        new[]
                        {
                            new NavigationEntry(
                                "原版游戏",
                                "\uE7C1",
                                BoxIcon,
                                "downloads",
                                "downloads"
                            ),
                            new NavigationEntry("Mod", "\uE90F", BoxIcon, "download-mod", "mod"),
                            new NavigationEntry("整合包", "\uE8FD", BoxIcon, "packs", "modpack"),
                            new NavigationEntry(
                                "数据包",
                                "\uE8FD",
                                BoxIcon,
                                "download-datapack",
                                "datapack"
                            ),
                            new NavigationEntry(
                                "资源包",
                                "\uE8B9",
                                FileIcon,
                                "download-resourcepack",
                                "resourcepack"
                            ),
                            new NavigationEntry(
                                "光影包",
                                "\uE706",
                                "M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M5,5 L7,7 M17,17 L19,19 M5,19 L7,17 M17,7 L19,5 M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7",
                                "download-shader",
                                "shader"
                            ),
                            new NavigationEntry(
                                "Maps",
                                "\uE80F",
                                HomeIcon,
                                "download-world",
                                "world"
                            ),
                            new NavigationEntry(
                                "Blueprints",
                                "\uE8A5",
                                FileIcon,
                                "download-blueprint",
                                "blueprints"
                            ),
                        }
                    )
                ),
                new NavigationGroup(
                    "设置",
                    "settings",
                    "\uE713",
                    SettingsIcon,
                    Array.AsReadOnly(
                        new[]
                        {
                            new NavigationEntry(
                                "常规设置",
                                "\uE713",
                                SettingsIcon,
                                "settings",
                                "settings"
                            ),
                            new NavigationEntry(
                                "个性化",
                                "\uE790",
                                "M4,20 L8,19 L20,7 L17,4 L5,16 Z M14,7 L17,10",
                                "personalization",
                                "appearance"
                            ),
                            new NavigationEntry(
                                "Java 管理",
                                "\uE950",
                                "M5,9 L17,9 L17,17 L14,20 L8,20 L5,17 Z M17,10 L21,10 L21,15 L17,15 M8,6 L10,3 M12,6 L14,3 M3,22 L20,22",
                                "java",
                                "java"
                            ),
                            new NavigationEntry(
                                "账号管理",
                                "\uE77B",
                                "M12,3 A4,4 0 1 0 12,11 A4,4 0 1 0 12,3 M4,22 L4,18 C4,12 20,12 20,18 L20,22",
                                "accounts",
                                "accounts"
                            ),
                        }
                    )
                ),
                new NavigationGroup(
                    "更多",
                    "more",
                    "\uE712",
                    MoreIcon,
                    Array.AsReadOnly(
                        new[]
                        {
                            new NavigationEntry(
                                "Toolbox",
                                "\uE912",
                                "M3,7 L21,7 L21,21 L3,21 Z M8,7 L8,3 L16,3 L16,7 M3,12 L21,12 M10,11 L14,11 L14,15 L10,15 Z",
                                "toolbox",
                                "toolbox"
                            ),
                            new NavigationEntry(
                                "System status",
                                "\uE9F5",
                                TaskIcon,
                                "stats",
                                "stats"
                            ),
                            new NavigationEntry(
                                "About",
                                "\uE946",
                                "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,10 L12,18 M12,6 L12,7",
                                "about",
                                "about"
                            ),
                        }
                    )
                ),
            }
        );
}
