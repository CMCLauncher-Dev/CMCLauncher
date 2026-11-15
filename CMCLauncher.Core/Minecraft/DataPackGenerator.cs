using System.Text;

namespace CMCLauncher.Core.Minecraft.DataPacks;

public static partial class DataPackGenerator
{
    // 数据包格式独立于资源包格式；只匹配明确列出的正式版，不猜测快照或未来版本。
    static readonly IReadOnlyDictionary<string, (int Major, int Minor)> Formats = BuildFormats();

    public static IReadOnlyList<string> SupportedGameVersions { get; } =
        Array.AsReadOnly(Formats.Keys.ToArray());

    static IReadOnlyDictionary<string, (int Major, int Minor)> BuildFormats()
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        void Add(int major, params string[] versions)
        {
            foreach (var version in versions)
                result.Add(version, (major, 0));
        }
        Add(4, "1.13", "1.13.1", "1.13.2", "1.14", "1.14.1", "1.14.2", "1.14.3", "1.14.4");
        Add(5, "1.15", "1.15.1", "1.15.2", "1.16", "1.16.1");
        Add(6, "1.16.2", "1.16.3", "1.16.4", "1.16.5");
        Add(7, "1.17", "1.17.1");
        Add(8, "1.18", "1.18.1");
        Add(9, "1.18.2");
        Add(10, "1.19", "1.19.1", "1.19.2");
        Add(12, "1.19.3");
        Add(13, "1.19.4");
        Add(15, "1.20", "1.20.1");
        Add(18, "1.20.2");
        Add(26, "1.20.3", "1.20.4");
        Add(41, "1.20.5", "1.20.6");
        Add(48, "1.21", "1.21.1");
        Add(57, "1.21.2", "1.21.3");
        Add(61, "1.21.4");
        Add(71, "1.21.5");
        Add(80, "1.21.6");
        Add(81, "1.21.7", "1.21.8");
        Add(88, "1.21.9", "1.21.10");
        result.Add("1.21.11", (94, 1));
        result.Add("26.1", (101, 1));
        return result;
    }

    public static (int Major, int Minor) GetDataPackFormat(string gameVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        return Formats.TryGetValue(gameVersion, out var format)
            ? format
            : throw new ArgumentException(
                "Unsupported Minecraft data pack version: " + gameVersion,
                nameof(gameVersion)
            );
    }

    /// <summary>按所选 Minecraft 正式版生成数据包元数据，成功后返回目录。</summary>
    public static string CreateDataPack(
        string parentDirectory,
        string packName,
        string gameVersion,
        string description = "",
        string packNamespace = "minecraft",
        CancellationToken cancellationToken = default
    ) =>
        CreateDataPack(
            parentDirectory,
            new DataPackOptions(packName, gameVersion, description, packNamespace),
            cancellationToken
        );

    static void Validate(DataPackOptions options)
    {
        var (packName, gameVersion, description, packNamespace) = options;
        GetDataPackFormat(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(packName);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(packNamespace);
        // 使用各平台都能保存的单个目录名，不把用户输入当作相对路径。
        if (
            Encoding.UTF8.GetByteCount(packName) > 255
            || packName is "." or ".."
            || packName.EndsWith('.')
            || packName.EndsWith(' ')
            || packName.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
        )
            throw new ArgumentException("Invalid data pack directory name.", nameof(packName));
        if (IsReservedDirectoryName(packName))
            throw new ArgumentException("Reserved directory name.", nameof(packName));
        if (
            packNamespace.Any(c =>
                !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')
            ) || packNamespace is "." or ".."
        )
            throw new ArgumentException("Invalid data pack namespace.", nameof(packNamespace));
        if (packNamespace.Length > 255 || IsReservedDirectoryName(packNamespace))
            throw new ArgumentException(
                "Invalid data pack namespace directory.",
                nameof(packNamespace)
            );
    }

    static bool IsReservedDirectoryName(string value)
    {
        var name = value.Split('.')[0].TrimEnd(' ');
        return new[] { "con", "prn", "aux", "nul", "conin$", "conout$" }.Contains(
                name,
                StringComparer.OrdinalIgnoreCase
            )
            || name.Length == 4
                && (
                    name.StartsWith("com", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)
                )
                && (name[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }
}
