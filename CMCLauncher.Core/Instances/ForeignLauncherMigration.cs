using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// 从其它启动器（Modrinth 应用及其 fork、MultiMC/Prism 系）迁移实例。
// 迁移采用“复制”语义：把原实例的玩家内容复制进本启动器的隔离目录，不依赖原启动器。
// 扫描结果中每个候选实例包含名称、游戏版本、加载器（fabric/quilt/forge/neoforge/vanilla）、
// 加载器版本、Minecraft 数据目录路径与描述。
public sealed record ForeignInstance(
    string Name,
    string GameVersion,
    string Loader,
    string? LoaderVersion,
    string MinecraftDirectory,
    string Description
)
{
    public override string ToString() => $"{Name}  ·  {GameVersion} / {Loader}";
}

// 扫描结果：根目录路径、发现的实例列表（按 Minecraft 目录去重）与解析失败的目录数。
public sealed record ForeignScan(string Root, IReadOnlyList<ForeignInstance> Instances, int Failed);

public sealed partial class LauncherCore
{
    // 复制时忽略的运行时目录（可由本启动器重新下载/生成，体积大且版本相关）。
    static readonly string[] IgnoredRuntimeDirs = ["assets", "libraries", "versions", "nmf"];

    // 将 OneSix 组件 UID 映射为加载器标识（fabric/quilt/forge/neoforge/vanilla）。
    static (string Loader, string? Version) ForeignLoader(string name, string? version) => name switch
    {
        "net.fabricmc.fabric-loader" => ("fabric", version),
        "org.quiltmc.quilt-loader" => ("quilt", version),
        "net.minecraftforge" => ("forge", version),
        "net.neoforged" => ("neoforge", version),
        "net.neoforged.forge" => ("neoforge", version),
        _ => ("vanilla", null),
    };

    // 安全读取文本文件（不存在或读取失败时返回空字符串）。
    static string FileText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "";
        }
    }

    // 安全读取 JSON 对象（不存在、为空或解析失败时返回 null）。
    static JsonObject? FileJson(string path)
    {
        var text = FileText(path);
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // 解析 MultiMC / Prism / PolyMC 及所有共享 OneSix 布局的 fork 实例：
    // 优先从 mmc-pack.json 的 components 数组读取游戏版本与加载器，
    // 缺失时回退到 instance.cfg 的键值对（mcVersion / IntendedGameVersion / OverrideGameType）。
    // Minecraft 数据目录优先使用实例内的 minecraft/ 子目录，否则使用实例目录本身。
    ForeignInstance ScanMmcInstance(string folder)
    {
        var pack = FileJson(Store.SafePath(folder, "mmc-pack.json"));
        var cfg = FileText(Store.SafePath(folder, "instance.cfg"));
        string? game = null, loader = null, loaderVersion = null;
        if (pack?["components"] is JsonArray components)
        {
            foreach (var component in components)
            {
                var uid = component?.S("uid");
                var enne = component?.S("version");
                if (uid == "net.minecraft")
                    game = component?.S("version");
                else if (uid is not null)
                {
                    var (detected, version) = ForeignLoader(uid, enne);
                    if (detected != "vanilla")
                    {
                        loader = detected;
                        loaderVersion = version;
                    }
                }
            }
        }
        string Option(string key)
        {
            foreach (var raw in cfg.Split('\n'))
            {
                var line = raw.Trim();
                var index = line.IndexOf('=');
                if (index <= 0 || line[..index].Trim() != key)
                    continue;
                return line[(index + 1)..].Trim();
            }
            return "";
        }
        game ??= Option("mcVersion");
        if (game.Length == 0)
            game = Option("IntendedGameVersion");
        loader ??= Option("OverrideGameType") is { Length: > 0 } o && o != "Vanilla" ? "forge" : null;
        var minecraft = Directory.Exists(Store.SafePath(folder, "minecraft"))
            ? Store.SafePath(folder, "minecraft")
            : folder;
        var name = Option("name") is { Length: > 0 } custom
            ? custom
            : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new ForeignInstance(
            name,
            game ?? "unknown",
            loader ?? "vanilla",
            loaderVersion,
            minecraft,
            Option("notes")
        );
    }

    // 解析 Modrinth 应用（Theseus 及 fork）实例：
    // 从 profiles/<name>/<name>.mrpack 内的 modrinth.index.json 读取描述与依赖
    // （minecraft 版本 + fabric-loader/quilt-loader/forge/neoforge 版本）。
    // 无法识别游戏版本与加载器时返回 null（视为非实例目录）。
    static ForeignInstance? ScanModrinthInstance(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var minecraft = Directory.Exists(Store.SafePath(folder, ".minecraft"))
            ? Store.SafePath(folder, ".minecraft")
            : folder;
        string? game = null, loader = null, loaderVersion = null, description = "";
        var mrpack = File.Exists(Store.SafePath(folder, name + ".mrpack"))
            ? Store.SafePath(folder, name + ".mrpack")
            : null;
        if (mrpack != null)
        {
            try
            {
                using var archive = ZipFile.OpenRead(mrpack);
                var entry = archive.GetEntry("modrinth.index.json");
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    JsonObject? index = null;
                    try
                    {
                        index = JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                    }
                    if (index != null)
                    {
                        description = index.S("description") ?? "";
                        if (index["dependencies"] is JsonObject dependencies)
                        {
                            game = dependencies.S("minecraft");
                            foreach (var key in new[] { "fabric-loader", "quilt-loader", "forge", "neoforge" })
                            {
                                var value = dependencies.S(key);
                                if (value.Length == 0)
                                    continue;
                                (loader, loaderVersion) = key switch
                                {
                                    "fabric-loader" => ("fabric", value),
                                    "quilt-loader" => ("quilt", value),
                                    "forge" => ("forge", value),
                                    _ => ("neoforge", value),
                                };
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new ForeignInstance(name, game ?? "unknown", loader ?? "vanilla", loaderVersion, minecraft, description);
            }
        }
        if (game == null && loader == null)
            return null;
        return new ForeignInstance(name, game ?? "unknown", loader ?? "vanilla", loaderVersion, minecraft, description);
    }

    // 扫描指定根目录下的外部启动器实例：
    // 依次检查 instances/（MultiMC 系）与 profiles/（Modrinth 系）目录，
    // 支持根目录直接包含或位于 .minecraft/ 子目录内。
    // 解析失败的目录计入 Failed 并通过 diagnostic 回调报告，不中断整体扫描。
    // 结果按 Minecraft 数据目录去重（同一目录只保留首个候选）。
    public ForeignScan ScanForeignInstances(
        string path,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    )
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var instances = new List<ForeignInstance>();
        var failed = 0;
        foreach (var style in new[] { "instances", "profiles" })
        {
            var root = Directory.Exists(Path.Combine(path, style))
                ? Path.Combine(path, style)
                : null;
            root ??= Directory.Exists(Path.Combine(path, ".minecraft", style))
                ? Path.Combine(path, ".minecraft", style)
                : null;
            if (root == null || !Directory.Exists(root))
                continue;
            foreach (var folder in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var candidate =
                        style == "instances"
                            ? Directory.Exists(Path.Combine(folder, "mmc-pack.json"))
                                ? ScanMmcInstance(folder)
                                : File.Exists(Path.Combine(folder, "instance.cfg"))
                                    ? ScanMmcInstance(folder)
                                    : null
                            : ScanModrinthInstance(folder);
                    if (candidate != null)
                        instances.Add(candidate);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed++;
                    diagnostic?.Invoke($"Foreign instance skipped: {folder}; {ex.Message}");
                }
            }
        }
        return new(
            path,
            instances
                .GroupBy(instance => instance.MinecraftDirectory)
                .Select(group => group.First())
                .ToArray(),
            failed
        );
    }

    // 递归复制目录：跳过符号链接/重解析点与运行时目录（assets/libraries/versions/nmf），
    // 文件覆盖复制。
    static void CopyForeignDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            var destinationEntry = Path.Combine(destination, name);
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (IgnoredRuntimeDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                CopyForeignDirectory(entry, destinationEntry);
            }
            else
            {
                File.Copy(entry, destinationEntry, true);
            }
        }
    }

    // 复制式迁移：把原实例玩家内容复制进隔离目录并登记为本启动器的本地实例。
    // 复制在后台线程执行，避免大目录迁移时冻结调用线程（UI）。
    // 返回成功迁移的实例列表与跳过原因列表（目录不存在或复制失败）。
    public Task<(List<Instance> Migrated, List<string> Skipped)> MigrateForeignInstances(
        IReadOnlyList<ForeignInstance> candidates,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    ) => Task.Run(() => MigrateSynchronous(candidates, ct, diagnostic), ct);

    // 同步迁移核心逻辑：
    // 逐个候选实例创建本地 Instance 记录 → 准备隔离目录 → 复制玩家内容。
    // 单个实例失败不影响其余实例（记录跳过原因后继续）。
    (List<Instance> Migrated, List<string> Skipped) MigrateSynchronous(
        IReadOnlyList<ForeignInstance> candidates,
        CancellationToken ct,
        Action<string>? diagnostic = null
    )
    {
        var migrated = new List<Instance>();
        var skipped = new List<string>();
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(candidate.MinecraftDirectory))
                {
                    skipped.Add($"{candidate.Name}: 实例目录不存在");
                    continue;
                }
                var instance = new Instance
                {
                    Name = candidate.Name,
                    Description = candidate.Description,
                    GameVersion = candidate.GameVersion,
                    Loader = candidate.Loader,
                    LoaderVersion = candidate.LoaderVersion,
                };
                store.PrepareIsolatedInstance(instance);
                CopyForeignDirectory(candidate.MinecraftDirectory, instance.Directory!);
                migrated.Add(instance);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                skipped.Add($"{candidate.Name}: {ex.Message}");
                diagnostic?.Invoke($"Foreign migration skipped: {candidate.Name}; {ex.Message}");
            }
        }
        return (migrated, skipped);
    }
}
