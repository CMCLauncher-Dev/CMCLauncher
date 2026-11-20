using System.Text.RegularExpressions;

namespace CMCLauncher;

// 单个投影文件信息：相对路径、大小与最后修改时间（UTC）。
public sealed record SchematicFile(string RelativePath, long Size, DateTime ModifiedUtc);

// 投影（Schematic）文件管理：
// 所有路径都相对于实例的实际游戏目录；不移动文件、不转换投影格式。
// 仅当实例安装了支持投影的 Mod（Create / Litematica 等）时才提供 schematics 目录。
public static class SchematicManager
{
    // 支持的投影文件扩展名。
    public static readonly string[] Extensions = [".litematic", ".schematic", ".schem", ".nbt"];

    // 确保实例的 schematics 目录可用：
    // 原版（vanilla）实例返回 null；扫描 mods 目录中的 JAR，
    // 若存在支持投影的 Mod 则返回（并按需创建）schematics 目录，否则返回 null。
    public static string? EnsureDirectory(Store store, Instance instance, bool create = true)
    {
        if (
            string.IsNullOrWhiteSpace(instance.Loader)
            || instance.Loader.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        var game = store.GameDirectory(instance);
        var directory = Store.SafePath(game, "schematics");
        if (Directory.Exists(directory))
            return directory;
        var mods = Store.SafePath(game, "mods");
        if (!Directory.Exists(mods))
            return null;
        foreach (var file in Directory.EnumerateFiles(mods))
        {
            if (!Path.GetExtension(file).Equals(".jar", StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                continue;
            HashSet<string> ids;
            try
            {
                ids = ContentManager.ModIds(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!ids.Any(SupportsSchematics))
                continue;
            if (create)
                Directory.CreateDirectory(directory);
            return directory;
        }
        return null;
    }

    // 判断 Mod ID 是否为支持投影的 Mod（Create 或 Litematica 的各加载器变体）。
    static bool SupportsSchematics(string id)
    {
        var normalized = id.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normalized == "create"
            || Regex.IsMatch(
                normalized,
                @"^(?:(?:re|neo)?(?:lite|forge|sche)matica)(?:forge|neoforge|fabric|port)?$"
            );
    }

    // 判断文件扩展名是否为受支持的投影格式。
    public static bool IsSupported(string file) =>
        Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    // 递归列出目录下全部投影文件（跳过符号链接与 .import- 暂存目录），
    // 可选地收集子目录相对路径到 folders，结果按相对路径排序。
    public static List<SchematicFile> List(string directory, List<string>? folders = null)
    {
        var result = new List<SchematicFile>();
        void Visit(string relative)
        {
            var current =
                relative.Length == 0
                    ? Path.GetDirectoryName(Store.SafePath(directory, ".root-check"))!
                    : Store.SafePath(directory, relative);
            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (
                    !IsSupported(file)
                    || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)
                )
                    continue;
                var path = Path.GetRelativePath(directory, file);
                var info = new FileInfo(Store.SafePath(directory, path));
                result.Add(new(path, info.Length, info.LastWriteTimeUtc));
            }
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (
                    File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)
                    || Path.GetFileName(child).StartsWith(".import-")
                )
                    continue;
                var path = Path.GetRelativePath(directory, child);
                folders?.Add(path);
                Visit(path);
            }
        }
        if (Directory.Exists(directory))
            Visit("");
        return result.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // 校验相对路径指向的投影文件存在且格式受支持，返回完整路径。
    public static string ExistingFile(string directory, string relative)
    {
        var path = Store.SafePath(directory, relative);
        if (!IsSupported(path) || !File.Exists(path))
            throw new IOException("Schematic file not found.");
        return path;
    }

    // 批量导入投影文件：
    // 先校验全部源文件（格式、重名）→ 复制到 .import- 暂存目录 →
    // 逐个移动到目标目录；失败时回滚已移动的文件并清理暂存目录。
    public static async Task Import(
        string directory,
        IReadOnlyList<string> sources,
        CancellationToken ct
    )
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (!IsSupported(source) || !File.Exists(source))
                throw new IOException("Unsupported schematic file.");
            var name = Path.GetFileName(source);
            if (
                !names.Add(name)
                || File.Exists(Store.SafePath(directory, name))
                || Directory.Exists(Store.SafePath(directory, name))
            )
                throw new IOException("A schematic with this name already exists: " + name);
        }
        var stage = Store.SafePath(directory, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var committed = new List<string>();
        try
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                var target = Store.SafePath(stage, Path.GetFileName(source));
                await using var input = File.OpenRead(source);
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                );
                await input.CopyToAsync(output, ct);
            }
            foreach (var name in names)
            {
                ct.ThrowIfCancellationRequested();
                var target = Store.SafePath(directory, name);
                File.Move(Store.SafePath(stage, name), target);
                committed.Add(target);
            }
        }
        catch
        {
            foreach (var file in committed)
                File.Delete(file);
            throw;
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }

    // 重命名投影文件（保留所在子目录与扩展名），校验新名称合法性。
    public static void Rename(string directory, string relative, string name)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || name != Path.GetFileName(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        )
            throw new IOException("Invalid schematic name.");
        var source = ExistingFile(directory, relative);
        var target = Store.SafePath(
            directory,
            Path.Combine(
                Path.GetDirectoryName(relative) ?? "",
                name.Trim() + Path.GetExtension(source)
            )
        );
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return;
        File.Move(source, target);
    }

    // 导出投影文件到指定路径：先写临时文件再原子移动，失败时清理临时文件。
    public static async Task Export(
        string directory,
        string relative,
        string target,
        CancellationToken ct
    )
    {
        var source = ExistingFile(directory, relative);
        if (
            string.Equals(
                Path.GetFullPath(source),
                Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new IOException("Choose a different export path.");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (
                var output = new FileStream(
                    temp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                )
            )
                await input.CopyToAsync(output, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
