using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class InstallerDependencyTests
{
    static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));

    static JsonObject Library(string name, byte[] bytes) =>
        new()
        {
            ["name"] = "fixture:" + name + ":1",
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["path"] = name + ".jar",
                    ["url"] = "https://fixture.invalid/" + name,
                    ["sha1"] = Hash(bytes),
                    ["size"] = bytes.Length,
                },
            },
        };

    static string Installer(string root, JsonArray libraries, JsonArray? version = null)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid() + ".jar");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("install_profile.json").Open()))
            writer.Write(
                new JsonObject
                {
                    ["json"] = "/version.json",
                    ["libraries"] = libraries,
                }.ToJsonString()
            );
        using (var writer = new StreamWriter(zip.CreateEntry("version.json").Open()))
            writer.Write(new JsonObject { ["libraries"] = version ?? [] }.ToJsonString());
        return path;
    }

    sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => send(request, ct);
    }

    static HttpResponseMessage Response(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var store = new Store(Path.Combine(root, "store"));
        var bytes = Encoding.UTF8.GetBytes("verified fixture");
        var requests = 0;
        using var core = new LauncherCore(
            store,
            new Handler(
                (_, _) =>
                {
                    Interlocked.Increment(ref requests);
                    return Task.FromResult(Response(bytes));
                }
            )
        );
        var installer = Installer(
            root,
            [Library("one", bytes), Library("two", bytes)],
            [Library("one", bytes)]
        );
        var vanilla = new JsonObject { ["libraries"] = new JsonArray(Library("three", bytes)) };
        var storage = Path.Combine(root, "libraries-cache");
        await core.PrefetchInstallerLibraries(installer, storage, vanilla, default);
        Check(
            requests == 3
                && File.ReadAllBytes(Path.Combine(storage, "libraries", "one.jar"))
                    .SequenceEqual(bytes),
            "Installer dependencies must use the launcher transport and deduplicate profile/version entries"
        );
        await core.PrefetchInstallerLibraries(installer, storage, vanilla, default);
        Check(requests == 3, "Installer prefetch must reuse verified cached artifacts");
        File.WriteAllText(Path.Combine(storage, "libraries", "one.jar"), "damaged");
        await core.PrefetchInstallerLibraries(installer, storage, vanilla, default);
        Check(
            requests == 4
                && File.ReadAllBytes(Path.Combine(storage, "libraries", "one.jar"))
                    .SequenceEqual(bytes),
            "Damaged installer artifacts must be repaired"
        );
        var unsafeLibrary = Library("unsafe", bytes);
        unsafeLibrary["downloads"]!["artifact"]!["path"] = "../outside.jar";
        try
        {
            await core.PrefetchInstallerLibraries(
                Installer(root, [unsafeLibrary]),
                Path.Combine(root, "unsafe"),
                new JsonObject(),
                default
            );
            throw new Exception("Traversal accepted");
        }
        catch (Exception ex) when (ex.Message != "Traversal accepted") { }
        Check(
            requests == 4 && !File.Exists(Path.Combine(root, "outside.jar")),
            "Invalid installer paths must fail before HTTP requests"
        );

        var tries = 0;
        using var retry = new LauncherCore(
            store,
            new Handler(
                (_, _) =>
                {
                    if (Interlocked.Increment(ref tries) < 3)
                        throw new HttpRequestException("fixture TLS EOF");
                    return Task.FromResult(Response(Encoding.ASCII.GetBytes(Hash(bytes))));
                }
            )
        );
        Check(
            await retry.InstallerChecksum("https://fixture.invalid/checksum", default)
                == Hash(bytes)
                && tries == 3,
            "Installer checksum must retry transient transport failures within a finite bound"
        );
        using var oversized = new LauncherCore(
            store,
            new Handler((_, _) => Task.FromResult(Response(new byte[513])))
        );
        try
        {
            await oversized.InstallerChecksum("https://fixture.invalid/checksum", default);
            throw new Exception("Oversized checksum accepted");
        }
        catch (InvalidDataException) { }

        var corruptRoot = Path.Combine(root, "corrupt");
        using var corrupt = new LauncherCore(
            store,
            new Handler(
                (_, _) => Task.FromResult(Response(Encoding.UTF8.GetBytes("corrupt fixture!")))
            )
        );
        try
        {
            await corrupt.PrefetchInstallerLibraries(
                installer,
                corruptRoot,
                new JsonObject(),
                default
            );
            throw new Exception("Bad hash accepted");
        }
        catch (IOException) { }
        Check(
            !File.Exists(Path.Combine(corruptRoot, "libraries", "one.jar"))
                && !Directory
                    .EnumerateFiles(corruptRoot, "*.part", SearchOption.AllDirectories)
                    .Any(),
            "Rejected installer artifacts must not leave final or temporary files"
        );
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new LauncherCore(
            store,
            new Handler(
                async (_, ct) =>
                {
                    began.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                    return Response(bytes);
                }
            )
        );
        var pending = cancelled.PrefetchInstallerLibraries(
            installer,
            Path.Combine(root, "cancelled"),
            new JsonObject(),
            cancellation.Token
        );
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            await pending;
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { }
        Check(
            LauncherCore
                .GameMirrorUrl(new Uri("https://maven.neoforged.net/releases/net/test.jar"))
                .AbsoluteUri == "https://neoforged.forgecdn.net/releases/net/test.jar",
            "NeoForge releases must have a verified official CDN fallback"
        );
        var privateUri = new Uri("https://maven.neoforged.net:8443/releases/file");
        Check(
            LauncherCore.GameMirrorUrl(privateUri) == privateUri,
            "Custom ports must not be redirected"
        );
        Check(
            LauncherCore
                .InstallerProxyArguments(new Settings { ProxyMode = "system" })
                .Contains("-Djava.net.useSystemProxies=true"),
            "Installer must follow system proxy mode"
        );
        var manual = LauncherCore.InstallerProxyArguments(
            new Settings { ProxyMode = "custom", Proxy = "http://127.0.0.1:7890" }
        );
        Check(
            manual.Contains("-Dhttps.proxyHost=127.0.0.1")
                && manual.Contains("-Dhttps.proxyPort=7890"),
            "Installer must honor manual HTTP proxies"
        );
        Check(
            LauncherCore
                .InstallerProxyArguments(new Settings { ProxyMode = "direct" })
                .Contains("-Djava.net.useSystemProxies=false"),
            "Direct mode must not silently inherit system proxy settings"
        );
        Check(
            LauncherCore
                .InstallerProxyArguments(
                    new Settings { ProxyMode = "custom", Proxy = "socks5://127.0.0.1:1080" }
                )
                .Contains("-DsocksProxyVersion=5"),
            "Installer must support SOCKS5 proxies"
        );
        try
        {
            LauncherCore.InstallerProxyArguments(
                new Settings
                {
                    ProxyMode = "custom",
                    Proxy = "http://127.0.0.1:7890",
                    ProxyUsername = "sensitive-user",
                }
            );
            throw new Exception("Authenticated proxy was silently bypassed");
        }
        catch (InvalidOperationException) { }
        Console.WriteLine(
            "PASS installer dependency transport, cache, hashes, retries, cancellation and scoped proxy settings"
        );
    }
}
