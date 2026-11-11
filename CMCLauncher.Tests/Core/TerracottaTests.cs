using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class TerracottaTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static async Task Rejected(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return;
        }
        throw new Exception("An invalid Terracotta operation succeeded.");
    }

    public static async Task Run(string root)
    {
        Check(
            TerracottaManager.AssetName("0.4.2", "osx", "aarch64")
                == "terracotta-0.4.2-macos-arm64-pkg.tar.gz",
            "macOS release mapping"
        );
        Check(
            TerracottaManager.AssetName("0.4.2", "linux", "x86_64")
                == "terracotta-0.4.2-linux-x86_64-pkg.tar.gz",
            "Linux release mapping"
        );
        await Rejected(() =>
            Task.FromResult(TerracottaManager.AssetName("../escape", "windows", "x86_64"))
        );
        Check(
            new[] { "host-scanning", "host-starting", "guest-connecting", "guest-starting" }.All(
                TerracottaStatus.IsPending
            ),
            "pending room states use indeterminate progress"
        );
        Check(
            !TerracottaStatus.IsPending("host-ok")
                && !TerracottaStatus.IsPending("exception")
                && !TerracottaStatus.HasRoom("waiting")
                && TerracottaStatus.HasRoom("guest-ok"),
            "completed, idle and failed room states do not show waiting progress"
        );
        var name = TerracottaManager.AssetName(
            "0.4.2",
            MinecraftPlatform.OsName,
            MinecraftPlatform.Architecture
        );
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            var executable = name.Replace("-pkg.tar.gz", OperatingSystem.IsWindows() ? ".exe" : "");
            tar.WriteEntry(
                new PaxTarEntry(TarEntryType.RegularFile, executable)
                {
                    DataStream = new MemoryStream("fixture"u8.ToArray()),
                }
            );
        }
        var transport = new ReleaseHandler(name, buffer.ToArray());
        var store = new Store(Path.Combine(root, "terracotta"));
        using var core = new LauncherCore(store, transport);
        using var manager = new TerracottaManager(store, core);
        Check(
            manager.Installed == null && !manager.Connected && transport.Count == 0,
            "no Terracotta automatic download"
        );
        await Rejected(() => manager.Start());
        Check(transport.Count == 0, "starting an absent component must not download it");
        await manager.Install(default);
        var installed = manager.Installed ?? throw new Exception("Terracotta did not install.");
        Check(
            installed.Version == "0.4.2" && installed.Files.Count == 1,
            "verified archive installation"
        );
        transport.BadHash = true;
        await Rejected(() => manager.Install(default));
        Check(
            manager.Installed?.Executable == installed.Executable
                && !Directory.EnumerateDirectories(manager.Root, "install-*").Any(),
            "failed update preserves previous component and removes staging files"
        );
        transport.BadHash = false;
        transport.DownloadCancelled = true;
        await Rejected(() => manager.Install(default));
        Check(
            manager.Installed?.Executable == installed.Executable
                && !Directory.EnumerateDirectories(manager.Root, "install-*").Any(),
            "cancelled download preserves component and removes staging files"
        );
        var manifest = transport.Metadata();
        manifest["assets"]![0]!["browser_download_url"] = "https://example.org/terracotta.tar.gz";
        await Rejected(() =>
            Task.FromResult(
                TerracottaManager.ParseRelease(
                    manifest,
                    MinecraftPlatform.OsName,
                    MinecraftPlatform.Architecture
                )
            )
        );
        await File.WriteAllTextAsync(
            Store.SafePath(manager.Root, installed.Executable),
            "modified"
        );
        await Rejected(() => manager.Start());
        Check(!manager.Connected, "modified component is not executed");
        await File.WriteAllTextAsync(
            Path.Combine(manager.Root, "current.json"),
            "{\"Version\":\"0.4.2\",\"Executable\":\"../escape\",\"Files\":{\"../escape\":\"invalid\"}}"
        );
        Check(manager.Installed == null, "unsafe installation metadata is ignored");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Rejected(() => manager.Install(cancelled.Token));
        }

        await using var server = new LocalServer();
        using var client = new TerracottaManager(store, core);
        await client.Connect(server.Port, "0.4.2", default);
        Check(
            client.Connected && client.State?["state"]?.ToString() == "waiting",
            "loopback handshake accepts actual upstream version syntax"
        );
        server.State =
            "{\"state\":\"host-ok\",\"index\":2,\"profile_index\":1,\"room\":\"U/fixture\",\"profiles\":[{\"name\":\"Host\"}]}";
        await client.Refresh();
        server.State = "{\"state\":\"waiting\",\"index\":1}";
        await client.Refresh();
        Check(
            client.State?["state"]?.ToString() == "host-ok",
            "stale state does not replace current room"
        );
        server.State =
            "{\"state\":\"host-ok\",\"index\":2,\"profile_index\":2,\"profiles\":[{\"name\":\"Host\"},{\"name\":\"Guest\"}]}";
        await client.Refresh();
        Check(
            TerracottaStatus.Members(client.State).Length == 2,
            "member updates with unchanged state index are displayed"
        );
        await Rejected(() => client.JoinRoom("U/other", "Player"));
        Check(
            !server.Paths.Any(p => p.StartsWith("/state/guesting")),
            "cannot replace an active room"
        );
        await client.Leave();
        await client.JoinRoom("U/a+b&c", "A & B");
        Check(
            server.Paths.Any(p => p.Contains("room=U%2Fa%2Bb%26c&player=A%20%26%20B")),
            "invitation and player parameters are escaped"
        );
        await client.Leave();
        await client.CreateRoom("Player");
        await Rejected(() => client.Remove());
        Check(
            client.State?["state"]?.ToString() == "host-scanning",
            "create waits for Minecraft LAN world"
        );
        await client.Stop();
        Check(
            !client.Connected && client.State == null,
            "empty control responses are valid and stopping clears state"
        );
        await Rejected(() => client.Connect(server.Port, "99.0.0", default));
        Check(!client.Connected, "incompatible singleton is not controlled");
        var unrelated = Path.Combine(store.Root, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        await manager.Remove();
        Check(
            !Directory.Exists(manager.Root) && manager.Installed == null && File.Exists(unrelated),
            "removal deletes only the managed component and restores the download state"
        );
        Check(transport.Count == 6, "local room operations do not request public downloads");
        Console.WriteLine(
            "PASS Terracotta explicit download, integrity, rollback, cancellation and loopback room protocol"
        );
    }

    sealed class ReleaseHandler(string asset, byte[] archive) : HttpMessageHandler
    {
        public int Count;
        public bool BadHash;
        public bool DownloadCancelled;

        public JsonNode Metadata() =>
            new JsonObject
            {
                ["tag_name"] = "v0.4.2",
                ["assets"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = asset,
                        ["size"] = archive.Length,
                        ["digest"] =
                            "sha256:"
                            + (
                                BadHash
                                    ? new string('0', 64)
                                    : Convert.ToHexString(SHA256.HashData(archive))
                            ),
                        ["browser_download_url"] =
                            TerracottaManager.SourceUrl + "/releases/download/v0.4.2/" + asset,
                    }
                ),
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Count++;
            var metadata = request.RequestUri!.Host == "api.github.com";
            if (!metadata && DownloadCancelled)
                throw new OperationCanceledException();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = metadata
                        ? new StringContent(Metadata().ToJsonString())
                        : new ByteArrayContent(archive),
                }
            );
        }
    }

    sealed class LocalServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource lifetime = new();
        readonly Task loop;
        long index = 3;
        public string State = "{\"state\":\"waiting\",\"index\":0}";
        public ConcurrentQueue<string> Paths = new();
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public LocalServer()
        {
            listener.Start();
            loop = Serve();
        }

        async Task Serve()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptTcpClientAsync(lifetime.Token);
                    using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var line = await reader.ReadLineAsync(lifetime.Token);
                    var path = line!.Split(' ')[1];
                    Paths.Enqueue(path);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(lifetime.Token))) { }
                    var body = "";
                    if (path == "/meta")
                        body = "{\"version\":\"0.4.2\"}";
                    else if (path == "/state")
                        body = State;
                    else if (path.StartsWith("/state/"))
                    {
                        var state =
                            path.StartsWith("/state/ide") ? "waiting"
                            : path.StartsWith("/state/scanning") ? "host-scanning"
                            : "guest-connecting";
                        State = new JsonObject
                        {
                            ["state"] = state,
                            ["index"] = index++,
                        }.ToJsonString();
                    }
                    var bytes = Encoding.UTF8.GetBytes(body);
                    await stream.WriteAsync(
                        Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"
                        ),
                        lifetime.Token
                    );
                    await stream.WriteAsync(bytes, lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            listener.Stop();
            await loop;
            lifetime.Dispose();
        }
    }
}
