using System.Net;
using System.Text;
using CMCLauncher;

static class NetworkProxyTests
{
    static void Check(bool value, string message = "Advanced proxy regression")
    {
        if (!value)
            throw new Exception(message);
    }

    static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("Unsafe proxy configuration accepted");
    }

    static HttpMessageHandler? ReachableProxyHandler(HttpClient client)
    {
        HttpMessageHandler? current = null;
        for (
            var type = typeof(HttpClient);
            type != null;
            type = type.BaseType
        )
        {
            var field = type.GetField(
                "_handler",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            if (field is null)
                continue;
            current = (HttpMessageHandler?)field.GetValue(client);
            break;
        }
        if (current == null)
            throw new Exception("HttpClient handler chain is not reachable");
        while (current != null)
        {
            if (current.GetType().Name == "ReachableProxyHandler")
                return current;
            current = (current as System.Net.Http.DelegatingHandler)?.InnerHandler;
        }
        return null;
    }

    public static async Task Run(string root)
    {
        var settings = new Settings();
        Check(NetworkProxy.Mode(settings) == "system");
        settings.Proxy = "http://127.0.0.1:7890";
        Check(NetworkProxy.Mode(settings) == "custom");
        settings.ProxyMode = "direct";
        Check(NetworkProxy.Create(settings) == null);
        settings.ProxyMode = "system";
        Check(ReferenceEquals(NetworkProxy.Create(settings), HttpClient.DefaultProxy));
        settings.ProxyMode = "custom";
        settings.ProxyUsername = "fixture-user";
        settings.ProxyPasswordProtected = NetworkProxy.ProtectPassword("fixture-password");
        settings.ProxyBypassHosts = "*.example.com; 192.168.*; localhost";
        var proxy = NetworkProxy.Create(settings)!;
        var target = new Uri("https://outside.test/");
        Check(proxy.GetProxy(target)!.Port == 7890 && !proxy.IsBypassed(target));
        Check(
            proxy.IsBypassed(new("https://cdn.example.com/"))
                && proxy.IsBypassed(new("http://192.168.1.2/"))
        );
        Check(!proxy.IsBypassed(new("https://cdn.example.com.evil.test/")));
        var credential = proxy.Credentials!.GetCredential(new Uri(settings.Proxy), "Basic");
        Check(credential!.UserName == "fixture-user" && credential.Password == "fixture-password");
        Check(NetworkProxy.Address("socks5://127.0.0.1").Port == 1080);
        foreach (
            var url in new[]
            {
                "http://user:password@proxy.test:1234",
                "ftp://proxy.test/",
                "http://proxy.test/?secret=bad",
                "http://proxy.test/path",
            }
        )
            Reject(() => NetworkProxy.Address(url));
        settings.ProxyBypassHosts = "https://bad.test";
        Reject(() => NetworkProxy.Create(settings));
        settings.ProxyBypassHosts = "";
        var store = new Store(root);
        store.Config.Settings = settings;
        await store.Save();
        Check(!File.ReadAllText(Path.Combine(root, "settings.json")).Contains("fixture-password"));
        Check(NetworkProxy.ReadPassword(new Store(root).Config.Settings) == "fixture-password");
        using var closed = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        settings.Proxy = "http://127.0.0.1:" + port;
        settings.ProxyDirectFallback = false;
        using var core = new LauncherCore(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await core.Json("https://outside.test/", timeout.Token);
            throw new Exception("Strict proxy silently fell back to direct");
        }
        catch (HttpRequestException ex)
        {
            Check(ex.Message.Contains("fallback is disabled"));
        }

        // Issue #40 回归：非 Socket 异常（255 字符主机名触发 ArgumentOutOfRangeException）
        // 按“不可达 → 直连回退”处理，而不是穿透探测逻辑打断请求。
        var longHost = new string('a', 255);
        NetworkProxy.Address("http://" + longHost + ":9");
        using var nonSocketOrigin = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        nonSocketOrigin.Start();
        var nonSocketPort = ((IPEndPoint)nonSocketOrigin.LocalEndpoint).Port;
        async Task ServeNonSocket()
        {
            using var client = await nonSocketOrigin.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            await stream.ReadAsync(buffer, 0, buffer.Length);
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\ndirect"
                )
            );
        }

        var nonSocketStore = new Store(Path.Combine(root, "proxy-non-socket"));
        nonSocketStore.Config.Settings.ProxyMode = "custom";
        nonSocketStore.Config.Settings.Proxy = "http://" + longHost + ":9";
        nonSocketStore.Config.Settings.ProxyDirectFallback = true;
        try
        {
            using var nonSocketCore = new LauncherCore(nonSocketStore);
            using var nonSocketTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serveNonSocketDirect = ServeNonSocket();
            using var nonSocketResponse = await nonSocketCore.Http.GetAsync(
                "http://127.0.0.1:" + nonSocketPort,
                nonSocketTimeout.Token
            );
            await serveNonSocketDirect;
            Check(
                await nonSocketResponse.Content.ReadAsStringAsync() == "direct",
                "non-socket probe exceptions fall back to direct"
            );
            var probeHandler = ReachableProxyHandler(nonSocketCore.Http)
                ?? throw new Exception("ReachableProxyHandler not present");
            var cacheField =
                probeHandler
                    .GetType()
                    .GetField(
                        "proxyReachability",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )
                ?? throw new Exception("proxyReachability field missing");
            var cache =
                (Dictionary<string, (bool Reachable, DateTime Expires)>)cacheField.GetValue(
                    probeHandler
                )!;
            var probeKey = cache.Keys.Single();
            foreach (var key in cache.Keys.ToArray())
                cache[key] = (false, DateTime.UtcNow.AddSeconds(-1));
            var serveRefresh = ServeNonSocket();
            using (var refreshResponse = await nonSocketCore.Http.GetAsync(
                "http://127.0.0.1:" + nonSocketPort,
                nonSocketTimeout.Token
            ))
            {
                await serveRefresh;
                Check(
                    cache.Count == 1 && cache.ContainsKey(probeKey),
                    "stale reachability entries are pruned before probing"
                );
            }
            for (var i = 0; i < 70; i++)
                cache["http://overflow-" + i + ".test:9/"] = (
                    false,
                    DateTime.UtcNow.AddSeconds(60)
                );
            cache[probeKey] = (false, DateTime.UtcNow.AddSeconds(-1));
            var serveOverflow = ServeNonSocket();
            using (var overflowResponse = await nonSocketCore.Http.GetAsync(
                "http://127.0.0.1:" + nonSocketPort,
                nonSocketTimeout.Token
            ))
            {
                await serveOverflow;
                Check(
                    cache.Count == 1 && !cache[probeKey].Reachable,
                    "reachability cache is cleared when it exceeds its cap"
                );
            }
            var probeLockField =
                probeHandler
                    .GetType()
                    .GetField(
                        "probeLock",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )
                ?? throw new Exception("probeLock field missing");
            var probeLock =
                (System.Threading.SemaphoreSlim)probeLockField.GetValue(probeHandler)!;
            probeHandler.Dispose();
            var disposed = false;
            try
            {
                probeLock.Wait(0);
            }
            catch (ObjectDisposedException)
            {
                disposed = true;
            }
            Check(disposed, "probeLock is disposed with the probe handler");
        }
        finally
        {
            nonSocketOrigin.Stop();
        }
    }
}