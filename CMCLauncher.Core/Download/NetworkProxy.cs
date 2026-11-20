using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 网络代理：根据设置解析代理模式、校验代理地址、加解密代理密码，
// 并创建带“绕过主机规则”的 IWebProxy 实例。
public static class NetworkProxy
{
    // 解析实际生效的代理模式：auto 模式下若未填写代理地址则回退为 system。
    public static string Mode(Settings settings) =>
        settings.ProxyMode == "auto"
            ? string.IsNullOrWhiteSpace(settings.Proxy)
                ? "system"
                : "custom"
            : settings.ProxyMode;

    // 校验并规范化代理地址：仅允许 http/https/socks5 协议，
    // 不允许内嵌凭据、路径、查询或片段；未指定端口时默认 1080。
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

    // 将代理密码加密后存入凭据库（返回保护后的密文）。
    public static string ProtectPassword(string password) =>
        CredentialVault.Protect(new JsonObject { ["password"] = password });

    // 从凭据库读取并解密代理密码；未设置时返回空字符串。
    public static string ReadPassword(Settings settings) =>
        settings.ProxyPasswordProtected == null
            ? ""
            : CredentialVault
                .Read(new Account { Type = "proxy", Secret = settings.ProxyPasswordProtected })
                .S("password");

    // 把“绕过主机”配置文本解析为正则规则数组（支持 * 与 ? 通配符）。
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

    // 按设置创建 IWebProxy：direct 返回 null，system 使用系统代理，
    // custom 则构建带凭据与绕过规则的 HostProxy。
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

    // 包装 WebProxy，在原有绕过逻辑之上叠加自定义的“绕过主机”正则规则。
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
