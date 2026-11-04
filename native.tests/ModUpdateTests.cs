using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class ModUpdateTests
{
    sealed class Handler(byte[] jar) : HttpMessageHandler
    {
        public bool BadHash;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonObject Version(string id, string date) =>
                new()
                {
                    ["id"] = id,
                    ["project_id"] = "fixture",
                    ["version_number"] = id,
                    ["date_published"] = date,
                    ["version_type"] = "release",
                    ["game_versions"] = new JsonArray("1.21.1"),
                    ["loaders"] = new JsonArray("fabric"),
                    ["files"] = new JsonArray(
                        new JsonObject
                        {
                            ["primary"] = true,
                            ["filename"] = "updated.jar",
                            ["url"] = "https://cdn.modrinth.com/fixture.jar",
                            ["size"] = jar.Length,
                            ["hashes"] = new JsonObject
                            {
                                ["sha512"] = BadHash
                                    ? new string('0', 128)
                                    : Convert.ToHexString(SHA512.HashData(jar)).ToLowerInvariant(),
                            },
                        }
                    ),
                };
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri.Host == "cdn.modrinth.com")
                response.Content = new ByteArrayContent(jar);
            else
                response.Content = new StringContent(
                    (
                        path.Contains("version_file/") ? (JsonNode)Version("old", "2025-01-01")
                        : path.EndsWith("/project/fixture/version")
                            ? new JsonArray(Version("new", "2026-01-01"))
                        : Version("new", "2026-01-01")
                    ).ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                );
            return Task.FromResult(response);
        }
    }

    public static async Task Run(string root)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            writer.Write("{\"id\":\"fixture\",\"version\":\"2\",\"name\":\"Fixture\"}");
        var handler = new Handler(bytes.ToArray());
        var store = new Store(root);
        using var core = new LauncherCore(store, handler);
        var mods = new ModrinthService(core, store);
        var instance = new Instance
        {
            GameVersion = "1.21.1",
            Loader = "fabric",
            Directory = Path.Combine(root, "game"),
        };
        var folder = Path.Combine(instance.Directory, "mods");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "old.jar");
        await File.WriteAllTextAsync(old, "old-file");
        var found = await mods.FindModUpdate(instance, old, default);
        if (found?.Version != "new")
            throw new Exception("Compatible update lookup failed");
        handler.BadHash = true;
        try
        {
            await mods.UpdateMod(instance, "old.jar", "fixture", "new", default);
            throw new Exception("Bad hash accepted");
        }
        catch (Exception ex) when (ex.Message.Contains("哈希")) { }
        if (
            await File.ReadAllTextAsync(old) != "old-file"
            || File.Exists(Path.Combine(folder, "updated.jar"))
        )
            throw new Exception("Failed update modified installed files");
        handler.BadHash = false;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try
            {
                await mods.UpdateMod(instance, "old.jar", "fixture", "new", canceled.Token);
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
        }
        if (!File.Exists(old))
            throw new Exception("Cancellation removed the installed mod");
        await mods.UpdateMod(instance, "old.jar", "fixture", "new", default);
        if (File.Exists(old) || !File.Exists(Path.Combine(folder, "updated.jar")))
            throw new Exception("Update did not replace root mod");
        if (
            !Directory
                .GetFiles(
                    Path.Combine(instance.Directory, "mod-backups"),
                    "old.jar",
                    SearchOption.AllDirectories
                )
                .Any()
        )
            throw new Exception("Old mod backup missing");
    }
}
