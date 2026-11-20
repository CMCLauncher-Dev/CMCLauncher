namespace CMCLauncher;

// 游戏版本发布记录：包含版本 Id 与类型（release/snapshot 等），
// 并提供愚人节版本的识别与中文分类展示。
public sealed record GameRelease(string Id, string Type)
{
    // 历年愚人节（4 月 1 日）发布的特殊版本列表（不区分大小写）。
    static readonly HashSet<string> AprilVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "2.0",
        "15w14a",
        "1.RV-Pre1",
        "3D Shareware v1.34",
        "20w14infinite",
        "22w13oneblockatatime",
        "23w13a_or_b",
        "24w14potato",
        "25w14craftmine",
        "26w14a",
    };

    // 判断是否为愚人节版本。
    public static bool IsAprilFoolsVersion(string id) => AprilVersions.Contains(id);

    // 版本的中文分类：愚人节版 / 正式版 / 快照版。
    public string Category =>
        IsAprilFoolsVersion(Id) ? "愚人节版"
        : Type == "release" ? "正式版"
        : "快照版";
}
