using System.Text.Json.Nodes;

namespace CMCLauncher;

// 与共享的 Minecraft 目录布局互操作，而不迁移其它启动器的文件：
// 定位版本根目录、解析各启动器（HMCL/PCL）的实例隔离设置与元数据。
public static class LauncherInstanceFormat
{
    // 定位结果：版本根目录 + 可选的单个版本 ID（null 表示整个根目录）。
    public sealed record Selection(string Root, string? VersionId);

    // 实例元数据：描述、图标路径与收藏标记。
    public sealed record Metadata(string Description, string? IconPath, bool Favorite);

    // 定位版本根目录，支持多种输入形式：
    // 含 versions/ 的根目录、versions/ 目录本身、单个版本文件夹（versions/<id>）、
    // 含 .minecraft/versions 的目录，或空目录（可作为未来安装的目标）。
    public static Selection Locate(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        if (Directory.Exists(Path.Combine(path, "versions")))
            return new(path, null);
        var parent = Path.GetDirectoryName(path);
        if (
            Path.GetFileName(path).Equals("versions", StringComparison.OrdinalIgnoreCase)
            && parent != null
        )
            return new(parent, null);
        if (
            parent != null
            && Path.GetFileName(parent).Equals("versions", StringComparison.OrdinalIgnoreCase)
        )
        {
            var id = Path.GetFileName(path);
            if (File.Exists(Path.Combine(path, id + ".json")))
                return new(Path.GetDirectoryName(parent)!, id);
        }
        var minecraft = Path.Combine(path, ".minecraft");
        if (Directory.Exists(Path.Combine(minecraft, "versions")))
            return new(minecraft, null);
        // 空目录仍可作为未来安装的目标位置被选中。
        return new(path, null);
    }

    // 安全读取启动器元数据文件（最大 1MB，失败时通过 diagnostic 报告并返回 null）。
    static string? Read(string root, string relative, Action<string>? diagnostic = null)
    {
        try
        {
            var file = Store.SafePath(root, relative);
            if (!File.Exists(file))
                return null;
            if (new FileInfo(file).Length > 1024 * 1024)
                throw new InvalidDataException("Launcher metadata exceeds 1 MB.");
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostic?.Invoke($"Launcher metadata ignored: {relative}; {ex.Message}");
            return null;
        }
    }

    // 安全读取并解析 JSON 对象（失败时通过 diagnostic 报告并返回 null）。
    static JsonObject? Json(string root, string relative, Action<string>? diagnostic = null)
    {
        var text = Read(root, relative, diagnostic);
        if (text == null)
            return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException ex)
        {
            diagnostic?.Invoke($"Launcher metadata ignored: {relative}; {ex.Message}");
            return null;
        }
    }

    // 解析 PCL 的 Setup.ini（INI 格式键值对，忽略注释行 ; # 与节标记 [ ]）。
    static Dictionary<string, string> Pcl(string version, Action<string>? diagnostic = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = Read(version, "PCL/Setup.ini", diagnostic);
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' or '[')
                continue;
            var separator = line.IndexOfAny([':', '=']);
            if (separator <= 0)
                continue;
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    // 宽松解析布尔值（true/false/1/0），无法识别时返回 null。
    static bool? Boolean(string? value) =>
        bool.TryParse(value, out var result) ? result
        : value == "1" ? true
        : value == "0" ? false
        : null;

    // 解析实例的游戏数据目录（隔离模式判定）：
    // 显式的每实例隔离设置优先于基于内容的旧版启发式。
    // 优先级：modpack.cfg（ModPack 隔离）→ HMCL 当前设置（.hmcl/config/instance-game-settings.json）
    // → HMCL 旧版文件（hmclversion.cfg）→ PCL 设置（Setup.ini 的 VersionArgumentIndie/V2）。
    // 返回 null 表示无法判定（调用方使用默认目录）。
    public static string? GameDirectory(string root, string id, Action<string>? diagnostic = null)
    {
        root = Path.GetFullPath(root);
        var version = Store.SafePath(root, "versions/" + id);
        if (File.Exists(Store.SafePath(version, "modpack.cfg")))
            return version;
        var current = Json(version, ".hmcl/config/instance-game-settings.json", diagnostic);
        if (
            current?["overrideProperties"] is JsonArray overrides
            && overrides.Any(value => value?.ToString() == "runningDirectory")
        )
        {
            var custom = current.S("runningDirectory").Trim();
            return custom.Length == 0 ? version : CustomDirectory(custom, root);
        }
        var hmcl = Json(version, "hmclversion.cfg", diagnostic);
        if (hmcl != null && Boolean(hmcl.S("usesGlobal")) != true)
        {
            var mode = hmcl.S("gameDirType", "0");
            if (mode is "0" or "ROOT_FOLDER")
                return root;
            if (mode is "1" or "VERSION_FOLDER")
                return version;
            if (mode is "2" or "CUSTOM" && hmcl.S("gameDir").Trim() is { Length: > 0 } directory)
                return CustomDirectory(directory, root);
        }
        var pcl = Pcl(version, diagnostic);
        if (Boolean(pcl.GetValueOrDefault("VersionArgumentIndieV2")) is bool isolated)
            return isolated ? version : root;
        return pcl.GetValueOrDefault("VersionArgumentIndie") switch
        {
            "1" => version,
            "2" => root,
            _ => null,
        };
    }

    // 解析自定义游戏目录路径：
    // 便携版启动器通常运行在 .minecraft 旁边，相对路径以 .minecraft 的父目录为基准；
    // 绝对路径保持绝对。
    static string? CustomDirectory(string value, string root)
    {
        var basis = Path.GetFileName(Path.TrimEndingDirectorySeparator(root))
            .Equals(".minecraft", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!
            : root;
        try
        {
            return Path.GetFullPath(value, basis);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    // 读取实例元数据（描述/图标/收藏）：
    // 描述与收藏来自 PCL 的 Setup.ini（CustomInfo / IsStar），
    // 图标优先使用 PCL 自定义 Logo（LogoCustom + Logo），回退到 icon.png 或 PCL/Logo.png。
    public static Metadata ReadMetadata(string root, string id, Action<string>? diagnostic = null)
    {
        var version = Store.SafePath(root, "versions/" + id);
        var pcl = Pcl(version, diagnostic);
        string? icon = null;
        var logo = pcl.GetValueOrDefault("Logo");
        if (
            Boolean(pcl.GetValueOrDefault("LogoCustom")) == true
            && !string.IsNullOrWhiteSpace(logo)
        )
        {
            try
            {
                var candidate = Path.IsPathRooted(logo)
                    ? Path.GetFullPath(logo)
                    : Store.SafePath(version, logo);
                if (
                    File.Exists(candidate)
                    && Path.GetExtension(candidate).ToLowerInvariant()
                        is ".png"
                            or ".jpg"
                            or ".jpeg"
                )
                    icon = candidate;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostic?.Invoke("Launcher icon ignored: " + ex.Message);
            }
        }
        foreach (var relative in new[] { "icon.png", "PCL/Logo.png" })
        {
            var candidate = Store.SafePath(version, relative);
            if (icon == null && File.Exists(candidate))
                icon = candidate;
        }
        return new(
            pcl.GetValueOrDefault("CustomInfo", ""),
            icon,
            Boolean(pcl.GetValueOrDefault("IsStar")) == true
        );
    }

    // 写入实例隔离元数据（使其它启动器也识别本实例为隔离模式）：
    // PCL Setup.ini（VersionArgumentIndie:1 + VersionArgumentIndieV2:True）、
    // HMCL 旧版 hmclversion.cfg（usesGlobal:false + gameDirType:1）、
    // HMCL 当前 .hmcl/config/instance-game-settings.json（overrideProperties 含 runningDirectory）。
    // 仅在文件不存在时创建，不覆盖已有设置。
    public static async Task WriteIsolationMetadata(string version)
    {
        var pcl = Store.SafePath(version, "PCL/Setup.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(pcl)!);
        if (!File.Exists(pcl))
            await File.WriteAllTextAsync(
                pcl,
                "VersionArgumentIndie:1\nVersionArgumentIndieV2:True\n"
            );
        var legacyHmcl = Store.SafePath(version, "hmclversion.cfg");
        var newLegacy = !File.Exists(legacyHmcl);
        if (newLegacy)
            await Store.AtomicJson(
                legacyHmcl,
                new JsonObject { ["usesGlobal"] = false, ["gameDirType"] = 1 }
            );
        var hmcl = Store.SafePath(version, ".hmcl/config/instance-game-settings.json");
        var legacy = Json(version, "hmclversion.cfg");
        if (
            !File.Exists(hmcl)
            && (
                newLegacy
                || legacy?.Int("gameDirType", -1) == 1 && Boolean(legacy.S("usesGlobal")) != true
            )
        )
            await Store.AtomicJson(
                hmcl,
                new JsonObject
                {
                    ["$schema"] = "https://schemas.glavo.site/hmcl/instance-game-settings/1.0.1",
                    ["overrideProperties"] = new JsonArray("runningDirectory"),
                    ["runningDirectory"] = "",
                }
            );
    }
}
