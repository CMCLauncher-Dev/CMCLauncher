using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public sealed partial class LauncherCore
{
    static string LibraryCoordinateKey(JsonNode lib)
    {
        var coordinate = lib.S("name").Split('@');
        var parts = coordinate[0].Split(':');
        return string.Join(':', parts.Take(2))
            + ":" + (parts.Length > 3 ? parts[3] : "")
            + "@" + (coordinate.Length > 1 ? coordinate[1] : "jar");
    }

    static string NativeClassifier(JsonNode lib) =>
        lib["natives"]?.S(MinecraftPlatform.OsName)
            .Replace("${arch}", Environment.Is64BitProcess ? "64" : "32") ?? "";

    // Select the platform first. Some manifests list the same coordinate for
    // multiple OSes, then repeat it to declare a separate native archive.
    internal static IReadOnlyList<JsonNode> EffectiveLibraries(JsonNode version) =>
        version["libraries"].Items()
            .Where(lib => Allowed(lib["rules"]))
            .DistinctBy(lib => (LibraryCoordinateKey(lib), NativeClassifier(lib)))
            .ToArray();

    // 预下载与启动共用同一套库准备逻辑：
    // 相同的顺序、平台规则与 natives 排除项。
    // 下载全部 classpath 库与当前平台的 natives 库，
    // 将 natives 解压到 natives 目录，返回去重后的 classpath 文件列表。
    async Task<List<string>> PrepareLibraries(
        JsonNode version,
        string storage,
        string? natives,
        CancellationToken ct,
        Action<int, int>? report = null,
        Action<string>? transferFile = null
    )
    {
        IReadOnlyList<JsonNode> libraries = transferFile == null
            ? EffectiveLibraries(version)
            : version["libraries"].Items().ToArray();
        // classpath[index] 记录第 index 个库的 classpath 文件路径；
        // nativeFiles[index] 记录第 index 个库的 natives 文件与解压排除项。
        var classpath = new string?[libraries.Count];
        var nativeFiles = new (string File, string[] Exclusions)?[libraries.Count];
        var artifactKeys = new HashSet<string>(StringComparer.Ordinal);
        var downloads = libraries.Select((lib, index) =>
        {
            var key = LibraryCoordinateKey(lib);
            var classifier = NativeClassifier(lib);
            var artifact = lib["downloads"]?["artifact"];
            var includeArtifact = (artifact != null || lib["downloads"] == null) && (transferFile != null || artifactKeys.Add(key));
            var native = classifier.Length > 0 ? lib["downloads"]?["classifiers"]?[classifier] : null;
            return (lib, index, includeArtifact, artifact, native);
        }).ToArray();
        var done = 0;
        await DownloadMany(
            downloads,
            async (item, token) =>
            {
                var (lib, index, includeArtifact, artifact, native) = item;
                // 下载 classpath 构件：优先使用 downloads.artifact，
                // 否则由 Maven 坐标推导路径与 URL。
                if (includeArtifact)
                {
                    var relative = artifact?.S("path") ?? Maven(lib.S("name"));
                    var file = Store.SafePath(storage, "libraries/" + relative);
                    await Download(
                        artifact?.S("url")
                            ?? lib.S("url", "https://libraries.minecraft.net/").TrimEnd('/')
                                + "/"
                                + relative,
                        file,
                        artifact?.S("sha1"),
                        size: artifact?["size"]?.GetValue<long>(),
                        ct: token
                    );
                    classpath[index] = file;
                    transferFile?.Invoke(file);
                }
                // 下载当前平台的 natives 库（按操作系统名选择 classifier，
                // 并将 ${arch} 替换为 64/32）。
                if (transferFile != null)
                {
                    var classifiers = lib["downloads"]?["classifiers"] as JsonObject;
                    if (classifiers != null && lib["natives"] is JsonObject platformNatives)
                    {
                        var names = platformNatives.SelectMany(entry =>
                        {
                            var name = entry.Value?.ToString() ?? "";
                            return new[] { name.Replace("${arch}", "64"), name.Replace("${arch}", "32") };
                        }).Distinct();
                        foreach (var name in names)
                        {
                            if (classifiers[name] is not JsonObject archive)
                                continue;
                            var file = Store.SafePath(storage, "libraries/" + archive.S("path"));
                            await Download(archive.S("url"), file, archive.S("sha1"), size: archive.Int("size"), ct: token);
                            transferFile(file);
                        }
                    }
                }
                else if (native != null)
                {
                    var file = Store.SafePath(storage, "libraries/" + native.S("path"));
                    await Download(
                        native.S("url"),
                        file,
                        native.S("sha1"),
                        size: native.Int("size"),
                        ct: token
                    );
                    nativeFiles[index] = (
                        file,
                        new[] { "META-INF/" }
                            .Concat(
                                lib["extract"]?["exclude"].Items().Select(n => n.ToString()) ?? []
                            )
                            .Distinct()
                            .ToArray()
                    );
                }
                report?.Invoke(Interlocked.Increment(ref done), libraries.Count);
            },
            ct
        );
        // 将各 natives 库（ZIP）解压到 natives 目录，
        // 跳过 META-INF/ 及版本 JSON 中声明的排除项。
        foreach (var entry in nativeFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (entry is not { } native)
                continue;
            using var zip = ZipFile.OpenRead(native.File);
            await Extract(zip, natives!, exclusions: native.Exclusions, ct: ct);
        }
        // 返回去重后的 classpath 文件列表。
        return classpath
            .Where(p => p != null)
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
