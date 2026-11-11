using System.IO.Compression;
using System.Text.Json.Nodes;
using CMCLauncher;

static class ModpackExportTests
{
    public static async Task Run(string temporary)
    {
        var root = Path.Combine(temporary, "export-instance");
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        await File.WriteAllTextAsync(Path.Combine(root, "mods", "sample.jar"), "mod bytes");
        await File.WriteAllTextAsync(Path.Combine(root, "config", "settings.toml"), "enabled=true");
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "private account data");
        await File.WriteAllTextAsync(Path.Combine(root, "servers.dat"), "private server list");
        var files = ModpackExporter.List(root, default);
        if (files.Contains("settings.json") || !files.Contains("mods/sample.jar"))
            throw new Exception("Export discovery includes launcher data or misses game content");
        var missingHandler = new MissingFileHandler();
        using (
            var core = new LauncherCore(
                new Store(Path.Combine(temporary, "export-reference-test")),
                missingHandler
            )
        )
        {
            async Task CheckUntracked(string project)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, ".craft-mods-native.json"),
                    new JsonArray(
                        new JsonObject { ["file"] = "sample.jar", ["project"] = project }
                    ).ToJsonString()
                );
                if (
                    await ModpackExportReferences.Resolve(
                        core,
                        root,
                        "mods/sample.jar",
                        "modrinth",
                        default
                    ) != null
                )
                    throw new Exception("Unexpected remote reference");
                if (missingHandler.Calls != 0)
                    throw new Exception("Unknown or CurseForge mod queried Modrinth");
            }
            await CheckUntracked("");
            await CheckUntracked("curseforge:7");
            await File.WriteAllTextAsync(
                Path.Combine(root, ".craft-mods-native.json"),
                new JsonArray(
                    new JsonObject { ["file"] = "sample.jar", ["project"] = "AbCd1234" }
                ).ToJsonString()
            );
            if (
                await ModpackExportReferences.Resolve(
                    core,
                    root,
                    "mods/sample.jar",
                    "modrinth",
                    default
                ) != null
                || missingHandler.Calls != 1
            )
                throw new Exception("HTTP 404 did not fall back to bundled content");
        }
        var instance = new Instance
        {
            GameVersion = "1.21.1",
            Loader = "neoforge",
            LoaderVersion = "21.1.80",
        };
        var target = Path.Combine(temporary, "test.mrpack");
        await ModpackExporter.Export(
            root,
            instance,
            "Example pack",
            "1.2.0",
            files.Where(f => f != "servers.dat"),
            target,
            default
        );
        using (var zip = ZipFile.OpenRead(target))
        {
            using var reader = new StreamReader(zip.GetEntry("modrinth.index.json")!.Open());
            var manifest = JsonNode.Parse(await reader.ReadToEndAsync())!;
            if (
                manifest["dependencies"]!["neoforge"]!.ToString() != "21.1.80"
                || manifest["dependencies"]!["minecraft"]!.ToString() != "1.21.1"
                || manifest["name"]!.ToString() != "Example pack"
            )
                throw new Exception("Export loses loader metadata");
            if (
                zip.GetEntry("overrides/servers.dat") != null
                || zip.GetEntry("overrides/settings.json") != null
            )
                throw new Exception("Unselected private content exported");
            using var mod = new StreamReader(zip.GetEntry("overrides/mods/sample.jar")!.Open());
            if (await mod.ReadToEndAsync() != "mod bytes")
                throw new Exception("Export changed file content");
        }
        var parsed = ModrinthService.ReadPack(target, "fallback");
        if (
            parsed.Manifest["dependencies"]!["neoforge"]!.ToString() != "21.1.80"
            || !parsed.ContentPrefixes.Contains("overrides/")
        )
            throw new Exception("Export cannot be read by the existing modpack importer");
        foreach (var format in new[] { "modrinth", "curseforge" })
        {
            foreach (var bundle in new[] { true, false })
            {
                var artifact = Path.Combine(temporary, format + "-" + bundle + ".zip");
                var calls = 0;
                Task<JsonNode?> Reference(string relative, CancellationToken ct)
                {
                    calls++;
                    return Task.FromResult<JsonNode?>(
                        format == "curseforge"
                            ? new JsonObject
                            {
                                ["projectID"] = 123,
                                ["fileID"] = 456,
                                ["required"] = true,
                            }
                            : new JsonObject
                            {
                                ["hashes"] = new JsonObject
                                {
                                    ["sha512"] = new string('a', 128),
                                    ["sha1"] = new string('b', 40),
                                },
                                ["downloads"] = new JsonArray(
                                    "https://cdn.modrinth.com/data/test/versions/test/sample.jar"
                                ),
                                ["fileSize"] = 9,
                            }
                    );
                }
                await ModpackExporter.Export(
                    root,
                    instance,
                    "Export test",
                    "2.0",
                    ["mods/sample.jar", "config/settings.toml"],
                    artifact,
                    default,
                    format: format,
                    includeResources: bundle,
                    resolveReference: Reference
                );
                using var exported = ZipFile.OpenRead(artifact);
                if (
                    (exported.GetEntry("overrides/mods/sample.jar") != null) != bundle
                    || calls != (bundle ? 0 : 1)
                    || exported.GetEntry("overrides/config/settings.toml") == null
                )
                    throw new Exception("Resource inclusion does not match export selection");
                var imported = ModrinthService.ReadPack(artifact, "fallback");
                if (
                    imported.Manifest["files"]!.AsArray().Count != (bundle ? 0 : 1)
                    || imported.Manifest["dependencies"]!["neoforge"]!.ToString()
                        != instance.LoaderVersion
                )
                    throw new Exception(
                        "Manifest references would duplicate bundled downloads or lose loader metadata"
                    );
            }
        }
        foreach (var format in new[] { "modrinth", "curseforge" })
        {
            var fallback = Path.Combine(temporary, format + "-fallback.zip");
            await ModpackExporter.Export(
                root,
                instance,
                "Fallback pack",
                "1",
                ["mods/sample.jar", "config/settings.toml"],
                fallback,
                default,
                format: format,
                includeResources: false,
                resolveReference: (_, _) => Task.FromResult<JsonNode?>(null)
            );
            using var zip = ZipFile.OpenRead(fallback);
            if (
                zip.GetEntry("overrides/mods/sample.jar") == null
                || ModrinthService.ReadPack(fallback, "fallback").Manifest["files"]!.AsArray().Count
                    != 0
            )
                throw new Exception("Unmatched resource was lost or still scheduled for download");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "mods", "local.jar"), "local mod");
        var mixed = Path.Combine(temporary, "mixed.mrpack");
        await ModpackExporter.Export(
            root,
            instance,
            "Mixed",
            "1",
            ["mods/sample.jar", "mods/local.jar"],
            mixed,
            default,
            includeResources: false,
            resolveReference: (file, _) =>
                Task.FromResult<JsonNode?>(
                    file == "mods/local.jar"
                        ? null
                        : new JsonObject
                        {
                            ["downloads"] = new JsonArray("https://cdn.modrinth.com/known.jar"),
                            ["fileSize"] = 9,
                            ["hashes"] = new JsonObject { ["sha1"] = new string('a', 40) },
                        }
                )
        );
        using (var zip = ZipFile.OpenRead(mixed))
        {
            if (
                zip.GetEntry("overrides/mods/local.jar") == null
                || zip.GetEntry("overrides/mods/sample.jar") != null
                || ModrinthService.ReadPack(mixed, "mixed").Manifest["files"]!.AsArray().Count != 1
            )
                throw new Exception("Mixed export lost or duplicated a resource");
        }
        var original = await File.ReadAllBytesAsync(target);
        async Task Reject(IEnumerable<string> selected, CancellationToken ct)
        {
            try
            {
                await ModpackExporter.Export(
                    root,
                    instance,
                    "Example",
                    "1",
                    selected,
                    target,
                    ct,
                    overwrite: true
                );
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                if (!(await File.ReadAllBytesAsync(target)).SequenceEqual(original))
                    throw new Exception("Failed export overwrote destination");
                return;
            }
            throw new Exception("Unsafe export succeeded");
        }
        await Reject(["settings.json"], default);
        await Reject(["mods/../../settings.json"], default);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Reject(files, cancel.Token);
        if (Directory.EnumerateFiles(temporary, "*.part").Any())
            throw new Exception("Export left temporary files");
    }

    sealed class MissingFileHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
