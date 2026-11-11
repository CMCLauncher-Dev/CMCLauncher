namespace CMCLauncher;

public sealed record GameRelease(string Id, string Type)
{
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

    public static bool IsAprilFoolsVersion(string id) => AprilVersions.Contains(id);

    public string Category =>
        IsAprilFoolsVersion(Id) ? "愚人节版"
        : Type == "release" ? "正式版"
        : "快照版";
}

