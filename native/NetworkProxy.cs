using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public static class NetworkProxy
{
    public static string Mode(Settings settings) =>
        settings.ProxyMode == "auto"
            ? string.IsNullOrWhiteSpace(settings.Proxy)
                ? "system"
                : "custom"
            : settings.ProxyMode;

    public static Uri Address(string value)
    {
        if (
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "socks5")
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/"
        )
            throw new InvalidOperationException(
                "Enter an HTTP, HTTPS, or SOCKS5 proxy address without embedded credentials or a path."
            );
        return uri.Port < 0 ? new UriBuilder(uri) { Port = 1080 }.Uri : uri;
    }

    public static string ProtectPassword(string password) =>
        CredentialVault.Protect(new JsonObject { ["password"] = password });

    public static string ReadPassword(Settings settings) =>
        settings.ProxyPasswordProtected == null
            ? ""
            : CredentialVault
                .Read(new Account { Type = "proxy", Secret = settings.ProxyPasswordProtected })
                .S("password");

    static Regex[] BypassRules(string value)
    {
        if (value.Length > 8000)
            throw new InvalidOperationException("Too many proxy bypass rules.");
        return value
            .Split(
                [';', ',', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Select(host =>
            {
                if (host.Length > 255 || !Regex.IsMatch(host, @"^[A-Za-z0-9_*?.:\[\]-]+$"))
                    throw new InvalidOperationException(
                        "Use host names or wildcard patterns such as *.example.com in proxy bypass rules."
                    );
                return new Regex(
                    "^" + Regex.Escape(host).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                );
            })
            .ToArray();
    }

    public static IWebProxy? Create(Settings settings)
    {
        switch (Mode(settings))
        {
            case "direct":
                return null;
            case "system":
                return HttpClient.DefaultProxy;
            case "custom":
                break;
            default:
                throw new InvalidOperationException("Unknown proxy mode.");
        }
        var proxy = new WebProxy(Address(settings.Proxy))
        {
            BypassProxyOnLocal = settings.ProxyBypassLocal,
        };
        if (settings.ProxyUsername.Length > 0)
            proxy.Credentials = new NetworkCredential(
                settings.ProxyUsername,
                ReadPassword(settings)
            );
        return new HostProxy(proxy, BypassRules(settings.ProxyBypassHosts));
    }

    sealed class HostProxy(WebProxy proxy, Regex[] rules) : IWebProxy
    {
        public ICredentials? Credentials
        {
            get => proxy.Credentials;
            set => proxy.Credentials = value;
        }

        public bool IsBypassed(Uri host) =>
            proxy.IsBypassed(host) || rules.Any(rule => rule.IsMatch(host.Host));

        public Uri? GetProxy(Uri destination) =>
            IsBypassed(destination) ? destination : proxy.GetProxy(destination);
    }
}
