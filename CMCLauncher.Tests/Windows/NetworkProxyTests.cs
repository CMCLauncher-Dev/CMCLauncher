using System.Net;
using CMCLauncher;

static class NetworkProxyTests
{
    static void Check(bool value)
    {
        if (!value)
            throw new Exception("Advanced proxy regression");
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
    }
}
