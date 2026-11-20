using System.Text.Json.Nodes;

namespace CMCLauncher;

// 本地实例扫描结果：根目录路径、发现的实例列表与解析失败的版本数。
public sealed record LocalInstanceScan(string Root, IReadOnlyList<Instance> Instances, int Failed);

public sealed partial class LauncherCore
{
    // 扫描本地 Minecraft 版本目录（只读，不触发下载、不修改源文件）：
    // 定位版本根目录 → 枚举 versions/ 下的版本文件夹（或指定单个版本）→
    // 解析版本 JSON（支持 inheritsFrom 继承链，最多 10 层防循环）→
    // 识别加载器并构建 Instance 记录。
    // 已导入的实例（ImportRoot + VersionId 或 Directory 匹配）会被跳过。
    public async Task<LocalInstanceScan> ScanLocalInstances(
        string path,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    )
    {
        var selection = LauncherInstanceFormat.Locate(path);
        var root = selection.Root;
        var versions = Path.Combine(root, "versions");
        var folders =
            selection.VersionId != null
                ? new[] { Store.SafePath(root, "versions/" + selection.VersionId) }
            : Directory.Exists(versions) ? Directory.GetDirectories(versions)
            : [];
        var pending = new List<Instance>();
        var failed = 0;
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(folder);
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (
                store.Config.Instances.Any(i =>
                    string.Equals(
                        i.ImportRoot == null ? null : Path.GetFullPath(i.ImportRoot),
                        root,
                        StringComparison.OrdinalIgnoreCase
                    ) && string.Equals(i.VersionId, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(i.Directory, folder, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            try
            {
                if (!File.Exists(Store.SafePath(root, $"versions/{id}/{id}.json")))
                    continue;
                // 递归解析版本继承链（inheritsFrom），seen 集合防止循环引用（上限 10 层）。
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                JsonNode? baseVersion = null;
                async Task<JsonNode> Read(string versionId)
                {
                    if (!seen.Add(versionId) || seen.Count > 10)
                        throw new InvalidDataException("Version inheritance cycle");
                    var file = Store.SafePath(root, $"versions/{versionId}/{versionId}.json");
                    var profile = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
                    if (profile is not JsonObject)
                        throw new InvalidDataException("Invalid local version profile.");
                    if (profile.S("inheritsFrom") is { Length: > 0 } parent)
                        return Merge(await Read(parent), profile);
                    baseVersion = profile;
                    return profile;
                }
                // 解析继承链得到完整版本配置，读取元数据（描述/图标/收藏），识别加载器。
                var resolved = await Read(id);
                var metadata = LauncherInstanceFormat.ReadMetadata(root, id, diagnostic);
                var detected = IdentifyLoader(resolved);
                var instance = new Instance
                {
                    Name = id,
                    Description = metadata.Description,
                    IconPath = metadata.IconPath,
                    Favorite = metadata.Favorite,
                    VersionId = id,
                    GameVersion =
                        resolved["clientVersion"]?.ToString()
                        ?? resolved["jar"]?.ToString()
                        ?? baseVersion!.S("id"),
                    Loader = detected?.Loader ?? "vanilla",
                    LoaderVersion = detected?.Version,
                    ImportRoot = root,
                    Directory = root,
                };
                // 用版本配置中的 libraries 等字段补充实例元数据（如加载器版本）。
                UpdateImportedMetadata(instance, resolved);
                pending.Add(instance);
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                failed++;
                diagnostic?.Invoke($"Local version skipped: {id}; {ex.Message}");
            }
        }
        return new(root, pending, failed);
    }
}
