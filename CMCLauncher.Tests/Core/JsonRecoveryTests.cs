using System.Net;
using System.Security.Cryptography;
using System.Text;
using CMCLauncher;

static class JsonRecoveryTests
{
    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        const string broken = """{"settings":""";
        await File.WriteAllTextAsync(settings, broken);
        var store = new Store(root);
        if (
            store.RecoveredSettingsPath is not { } backup
            || await File.ReadAllTextAsync(backup) != broken
            || !store.Config.Settings.MinecraftDirectorySetupPending
            || new Store(root).RecoveredSettingsPath != null
        )
            throw new Exception("Corrupt settings were not isolated and replaced.");

        var records = Path.Combine(root, ".craft-mods-native.json");
        foreach (var content in new[] { broken, "null", "{}", "[null]" })
        {
            await File.WriteAllTextAsync(records, content);
            if ((await ModrinthService.ReadModRecords(records, default)).Count != 0)
                throw new Exception("Malformed mod records should be treated as empty.");
        }
        await File.WriteAllTextAsync(records, """[{"file":"sample.jar","project":"ABCDEFGH"}]""");
        if ((await ModrinthService.ReadModRecords(records, default)).Count != 1)
            throw new Exception("Valid mod records were lost.");

        const string version = "1.21.1";
        const string indexJson = """{"objects":{}}""";
        var digest = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(indexJson)));
        var versionJson = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                id = version,
                libraries = Array.Empty<object>(),
                assetIndex = new
                {
                    id = "test-index",
                    url = "https://example.test/index.json",
                    sha1 = digest,
                    size = indexJson.Length,
                },
            }
        );
        var handlers = new MetadataHandler(versionJson, indexJson);
        using var core = new LauncherCore(store, handlers);
        var vanillaPath = Store.SafePath(store.Library, $"versions/{version}/{version}.json");
        var fabricPath = Store.SafePath(store.Library, $"profiles/fabric-{version}-0.16.0.json");
        var assetPath = Store.SafePath(store.Library, "assets/indexes/test-index.json");
        foreach (var path in new[] { vanillaPath, fabricPath, assetPath })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, broken);
        }

        var instance = new Instance
        {
            Name = "Recovery",
            GameVersion = version,
            Loader = "fabric",
            LoaderVersion = "0.16.0",
        };
        try
        {
            await core.VersionFor(instance, default);
            throw new Exception("Invalid metadata from the server was accepted.");
        }
        catch (InvalidDataException)
        {
            if (await File.ReadAllTextAsync(vanillaPath) != broken)
                throw new Exception("A failed download replaced the existing cache.");
        }

        handlers.ReturnBrokenVersion = false;
        await core.Resolve(version, store.Library, default);
        await core.DownloadGame(instance, default);
        var calls = handlers.Calls;
        await core.DownloadGame(instance, default);
        if (
            handlers.Calls != calls
            || await File.ReadAllTextAsync(vanillaPath) != versionJson
            || await File.ReadAllTextAsync(fabricPath) != """{"id":"fabric","libraries":[]}"""
            || await File.ReadAllTextAsync(assetPath) != indexJson
        )
            throw new Exception("Repaired metadata was not persisted and reused.");

        await CheckHiddenImports(Path.Combine(root, "hidden-imports"));

        Console.WriteLine(
            "PASS corrupted settings, mod records and vanilla, Fabric and asset metadata cache recovery"
        );
    }

    static async Task CheckHiddenImports(string root)
    {
        var store = new Store(root);
        var imported = new Instance
        {
            Name = "Imported",
            ImportRoot = store.Library,
            VersionId = "Imported",
        };
        var managed = new Instance { Name = "Managed", GameVersion = "1.21.1" };
        store.Config.Instances.AddRange([imported, managed]);
        await store.Save();
        var hidden = new Store(root);
        if (
            hidden.Config.Instances.Single().Id != managed.Id
            || hidden.NewlyHiddenImportedInstances.Single().Id != imported.Id
            || new Store(root).NewlyHiddenImportedInstances.Count != 0
        )
            throw new Exception(
                "Unavailable imported instances were not hidden from the active list."
            );

        var profile = Store.SafePath(store.Library, "versions/Imported/Imported.json");
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        await File.WriteAllTextAsync(profile, """{"id":"Imported"}""");
        var restored = new Store(root);
        if (restored.Config.Instances.All(x => x.Id != imported.Id))
            throw new Exception("A repaired imported instance did not return to the active list.");

        await File.WriteAllTextAsync(profile, "{");
        hidden = new Store(root);
        if (hidden.NewlyHiddenImportedInstances.Single().Id != imported.Id)
            throw new Exception("A malformed local profile was not detected.");
        await hidden.ForgetHiddenImportedInstances();
        if (
            new Store(root).Config.HiddenImportedInstances.Count != 0
            || await File.ReadAllTextAsync(profile) != "{"
        )
            throw new Exception("Removing unavailable records did not preserve their game files.");
        Console.WriteLine(
            "PASS unavailable imported instances are hidden, restored or forgotten without deleting game files"
        );
    }

    sealed class MetadataHandler(string version, string index) : HttpMessageHandler
    {
        public bool ReturnBrokenVersion { get; set; } = true;
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            var uri = request.RequestUri!;
            var body = uri.AbsolutePath switch
            {
                "/mc/game/version_manifest_v2.json" =>
                    """{"versions":[{"id":"1.21.1","url":"https://example.test/version.json"}]}""",
                "/version.json" => ReturnBrokenVersion ? "{" : version,
                "/index.json" => index,
                "/v2/versions/loader/1.21.1/0.16.0/profile/json" =>
                    """{"id":"fabric","libraries":[]}""",
                _ => throw new InvalidOperationException("Unexpected metadata request: " + uri),
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }
    }
}
