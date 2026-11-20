using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// Mod 包导出：把实例中的游戏内容（mods、config、资源包等）打包成
// Modrinth 或 CurseForge 格式的 zip 包，支持把部分文件以引用（reference）
// 而非内嵌方式写入清单。
public static class ModpackExporter
{
    // 可导出的目录白名单。
    public static readonly string[] Folders =
    [
        "mods",
        "config",
        "defaultconfigs",
        "resourcepacks",
        "shaderpacks",
        "kubejs",
        "scripts",
        "tacz",
        "schematics",
        "saves",
        "screenshots",
    ];
    // 可导出的设置文件白名单。
    public static readonly string[] SettingsFiles =
    [
        "options.txt",
        "optionsof.txt",
        "optionsshaders.txt",
        "servers.dat",
    ];

    // 解析并校验相对路径：拒绝路径穿越（..、.、空段）、非白名单内容、
    // 以及符号链接/联接点，返回安全的绝对路径。
    static string Resolve(string root, string relative)
    {
        relative = relative.Replace('\\', '/');
        if (relative.Split('/').Any(part => part is "" or "." or ".."))
            throw new IOException("Unsafe modpack file path.");
        if (!Folders.Contains(relative.Split('/')[0]) && !SettingsFiles.Contains(relative))
            throw new IOException("This file is not exportable game content: " + relative);
        var path = Store.SafePath(root, relative);
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Cannot export symbolic links or junctions.");
        foreach (var part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cannot export symbolic links or junctions: " + relative);
        }
        return path;
    }

    // 递归枚举实例中所有可导出的文件（相对路径），上限 29000 个。
    public static IReadOnlyList<string> List(string root, CancellationToken ct)
    {
        var files = new List<string>();
        void Visit(string relative)
        {
            ct.ThrowIfCancellationRequested();
            var full = Resolve(root, relative);
            if (Directory.Exists(full))
            {
                foreach (
                    var child in Directory
                        .EnumerateFileSystemEntries(full)
                        .Order(StringComparer.OrdinalIgnoreCase)
                )
                    Visit(Path.GetRelativePath(root, child).Replace('\\', '/'));
            }
            else
            {
                if (files.Count >= 29000)
                    throw new IOException("Too many files to export (maximum 29000).");
                files.Add(relative);
            }
        }
        foreach (var relative in Folders.Concat(SettingsFiles))
            if (
                Directory.Exists(Path.Combine(root, relative))
                || File.Exists(Path.Combine(root, relative))
            )
                Visit(relative);
        return files;
    }

    // 导出 Mod 包：根据所选文件生成 Modrinth/CurseForge 清单，
    // 把内嵌文件写入 overrides/ 目录，引用文件写入清单的 files 数组，
    // 最终打包为 zip（先写临时文件再原子移动）。
    public static async Task Export(
        string root,
        Instance instance,
        string name,
        string version,
        IEnumerable<string> selectedFiles,
        string output,
        CancellationToken ct,
        Action<string>? progress = null,
        bool overwrite = false,
        string format = "modrinth",
        bool includeResources = true,
        Func<string, CancellationToken, Task<JsonNode?>>? resolveReference = null
    )
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(version)
            || string.IsNullOrWhiteSpace(instance.GameVersion)
        )
            throw new IOException("Modpack name, version and Minecraft version are required.");
        if (format is not ("modrinth" or "curseforge"))
            throw new IOException("Unknown modpack format.");
        var dependencies = new JsonObject { ["minecraft"] = instance.GameVersion };
        if (instance.Loader != "vanilla")
        {
            var key = instance.Loader switch
            {
                "fabric" => "fabric-loader",
                "forge" => "forge",
                "neoforge" => "neoforge",
                _ => throw new IOException("Unsupported modpack loader: " + instance.Loader),
            };
            if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
                throw new IOException("The instance loader version is missing.");
            dependencies[key] = instance.LoaderVersion;
        }
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = version.Trim(),
            ["name"] = name.Trim(),
            ["summary"] = instance.Description,
            ["files"] = new JsonArray(),
            ["dependencies"] = dependencies,
        };
        var files = selectedFiles.Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length > 29000)
            throw new IOException("Too many files to export.");
        var bundled = new List<string>();
        var references = new JsonArray();
        foreach (var relative in files)
        {
            ct.ThrowIfCancellationRequested();
            Resolve(root, relative);
            if (
                !includeResources
                && relative.Split('/')[0] is "mods" or "resourcepacks" or "shaderpacks" or "tacz"
            )
            {
                var reference =
                    resolveReference == null ? null : await resolveReference(relative, ct);
                if (reference == null)
                    bundled.Add(relative);
                else
                {
                    if (format == "modrinth")
                        reference["path"] = relative;
                    references.Add(reference);
                }
            }
            else
                bundled.Add(relative);
        }
        manifest["files"] = references;
        var indexName = "modrinth.index.json";
        if (format == "curseforge")
        {
            var loaders = new JsonArray();
            if (instance.Loader != "vanilla")
                loaders.Add(
                    new JsonObject
                    {
                        ["id"] = instance.Loader + "-" + instance.LoaderVersion,
                        ["primary"] = true,
                    }
                );
            manifest = new JsonObject
            {
                ["manifestType"] = "minecraftModpack",
                ["manifestVersion"] = 1,
                ["name"] = name.Trim(),
                ["version"] = version.Trim(),
                ["author"] = "",
                ["minecraft"] = new JsonObject
                {
                    ["version"] = instance.GameVersion,
                    ["modLoaders"] = loaders,
                },
                ["files"] = references.DeepClone(),
                ["overrides"] = "overrides",
            };
            indexName = "manifest.json";
        }
        output = Path.GetFullPath(output);
        var sourceRoot =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (output.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Save the modpack outside the instance directory.");
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (
                var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                )
            )
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
                await using (var index = zip.CreateEntry(indexName).Open())
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
                    await index.WriteAsync(bytes, ct);
                }
                foreach (var relative in bundled)
                {
                    ct.ThrowIfCancellationRequested();
                    var full = Resolve(root, relative);
                    progress?.Invoke(relative);
                    await using var input = new FileStream(
                        full,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        true
                    );
                    await using var entry = zip.CreateEntry(
                            "overrides/" + relative.Replace('\\', '/'),
                            CompressionLevel.Optimal
                        )
                        .Open();
                    await input.CopyToAsync(entry, ct);
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
