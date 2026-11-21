using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed partial class LauncherCore
{
    // 将下载 URL 映射到镜像地址：
    // NeoForged 官方 Maven 重定向到 ForgeCDN 镜像，其余走 BMCL 镜像。
    internal static Uri GameMirrorUrl(Uri uri)
    {
        // NeoForged 在其官方 ForgeCDN 镜像上发布相同的发布产物。
        if (
            uri.Scheme == "https"
            && uri.IsDefaultPort
            && uri.UserInfo.Length == 0
            && uri.Host.Equals("maven.neoforged.net", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/releases/", StringComparison.Ordinal)
        )
            return new Uri("https://neoforged.forgecdn.net" + uri.PathAndQuery);
        return BmclUrl(uri);
    }

    // 带重试的下载请求包装：
    // 传输层失败（超时/5xx/连接中断）时最多重试 2 次，间隔递增。
    async Task<T> InstallerRequest<T>(
        Func<Task<T>> request,
        string description,
        CancellationToken ct
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await request();
            }
            catch (Exception ex) when (attempt < 2 && IsInstallerTransportFailure(ex))
            {
                DebugLog?.Invoke($"Installer transfer retry {attempt + 1}/2: {description}");
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct);
            }
        }
    }

    // 判断异常是否为可重试的传输层失败（非业务错误）。
    static bool IsInstallerTransportFailure(Exception ex) =>
#if NET8_0_OR_GREATER
        ex is HttpIOException ||
#endif
        ex is IOException { InnerException: System.Net.Sockets.SocketException }
        || ex is HttpRequestException request
            && (
                request.StatusCode == null
                || request.StatusCode
                    is HttpStatusCode.RequestTimeout
                        or HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout
            );

    // 为 Java 安装器生成 JVM 代理参数：
    // 支持系统代理与自定义 HTTP/SOCKS5 代理（不支持认证代理），
    // 并设置连接/读取超时与绕过列表。
    internal static IReadOnlyList<string> InstallerProxyArguments(Settings settings)
    {
        var mode = NetworkProxy.Mode(settings);
        var options = new List<string>
        {
            "-Dhttp.proxyHost=",
            "-Dhttps.proxyHost=",
            "-DsocksProxyHost=",
            "-Dsun.net.client.defaultConnectTimeout=15000",
            "-Dsun.net.client.defaultReadTimeout=60000",
            "-Djava.net.useSystemProxies=" + (mode == "system" ? "true" : "false"),
        };
        if (mode != "custom")
            return options;
        var address = NetworkProxy.Address(settings.Proxy);
        if (address.Scheme == "https" || !string.IsNullOrEmpty(settings.ProxyUsername))
            throw new InvalidOperationException(
                "Java installer networking requires an unauthenticated HTTP or SOCKS5 proxy. Use a local proxy for installation."
            );
        _ = NetworkProxy.Create(settings); // 在构造 JVM 参数前复用主机名模式校验。
        var bypass = string.Join(
            "|",
            settings.ProxyBypassHosts.Split(
                [';', ',', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
        if (settings.ProxyBypassLocal)
            bypass = "localhost|127.*|[::1]" + (bypass.Length > 0 ? "|" + bypass : "");
        options.Add("-Dhttp.nonProxyHosts=" + bypass);
        options.Add("-DsocksNonProxyHosts=" + bypass);
        if (address.Scheme == "socks5")
        {
            options.Add("-DsocksProxyHost=" + address.DnsSafeHost);
            options.Add("-DsocksProxyPort=" + address.Port);
            options.Add("-DsocksProxyVersion=5");
        }
        else
            foreach (var protocol in new[] { "http", "https" })
            {
                options.Add("-D" + protocol + ".proxyHost=" + address.DnsSafeHost);
                options.Add("-D" + protocol + ".proxyPort=" + address.Port);
            }
        return options;
    }

    // 下载 .sha1 校验文件并提取 SHA-1 哈希值，响应体限制 512 字节。
    internal async Task<string> InstallerChecksum(string url, CancellationToken ct) =>
        await InstallerRequest(
            async () =>
            {
                using var response = await http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var bytes = new byte[513];
                var length = 0;
                int count;
                while (
                    length < bytes.Length
                    && (count = await stream.ReadAsync(bytes.AsMemory(length), ct)) != 0
                )
                    length += count;
                if (length > 512)
                    throw new InvalidDataException("The installer checksum response is too large.");
                var fields = Encoding
                    .UTF8.GetString(bytes, 0, length)
                    .Trim('﻿')
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 0 || !Regex.IsMatch(fields[0], "^[a-fA-F0-9]{40}$"))
                    throw new InvalidDataException(
                        "The installer does not provide a valid SHA-1 checksum."
                    );
                return fields[0];
            },
            "installer checksum",
            ct
        );

    // Java 安装器使用独立的 HTTP 栈。在启动 Java 之前，通过启动器的代理/镜像
    // 流水线填充其已校验的 Maven 缓存，同时不削弱 Java 的 TLS 校验。
    internal async Task PrefetchInstallerLibraries(
        string installer,
        string storage,
        JsonNode vanilla,
        CancellationToken ct
    )
    {
        var documents = new List<JsonNode> { vanilla };
        using (var zip = ZipFile.OpenRead(installer))
        {
            async Task<JsonNode?> Read(string name)
            {
                var entry = zip.GetEntry(name.TrimStart('/'));
                if (entry == null)
                    return null;
                if (entry.Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("Installer metadata is too large.");
                var bytes = new byte[(int)entry.Length];
                await using var source = entry.Open();
                await source.ReadExactlyAsync(bytes, ct);
                if (source.ReadByte() != -1)
                    throw new InvalidDataException("Installer metadata length is invalid.");
                return JsonNode.Parse(bytes)
                    ?? throw new InvalidDataException("Installer metadata is empty.");
            }
            var profile =
                await Read("install_profile.json")
                ?? throw new InvalidDataException("The installer is missing install_profile.json.");
            documents.Add(profile);
            if (profile["versionInfo"] is JsonObject legacy)
                documents.Add(legacy);
            if (await Read(profile.S("json", "version.json")) is { } version)
                documents.Add(version);
        }
        var dependencies = new Dictionary<
            string,
            (string Url, string Hash, long? Size, string Name)
        >(JavaManager.PathComparer);
        foreach (
            var library in documents
                .SelectMany(d => d["libraries"].Items())
                .Where(l => Allowed(l["rules"]))
        )
        {
            ct.ThrowIfCancellationRequested();
            var artifact = library["downloads"]?["artifact"];
            // 缺失/为空的 URL 标识的是遗留、内嵌或由处理器生成的产物。
            // 这些产物仍由官方安装器自行处理，而不是猜测下载 URL。
            if (artifact == null || string.IsNullOrWhiteSpace(artifact.S("url")))
                continue;
            var path = Store.SafePath(storage, "libraries/" + artifact.S("path"));
            var hash = artifact.S("sha1");
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$"))
                throw new InvalidDataException(
                    "Installer dependency has no valid SHA-1: " + library.S("name")
                );
            if (
                dependencies.TryGetValue(path, out var previous)
                && !previous.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidDataException(
                    "Installer dependencies disagree about a file hash: " + library.S("name")
                );
            long? length = null;
            if (artifact["size"] != null)
            {
                if (!long.TryParse(artifact.S("size"), out var declared) || declared < 0)
                    throw new InvalidDataException(
                        "Installer dependency has an invalid size: " + library.S("name")
                    );
                length = declared;
            }
            dependencies[path] = (artifact.S("url"), hash, length, library.S("name"));
            if (dependencies.Count > 4096)
                throw new InvalidDataException("Too many installer dependencies.");
        }
        var done = 0;
        await DownloadMany(
            dependencies,
            async (dependency, token) =>
            {
                var (url, hash, size, name) = dependency.Value;
                try
                {
                    await InstallerRequest(
                        async () =>
                        {
                            await Download(url, dependency.Key, hash, size: size, ct: token);
                            return true;
                        },
                        name,
                        token
                    );
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new IOException("Installer dependency download failed: " + name, ex);
                }
                Status?.Invoke(
                    $"Installer dependencies {Interlocked.Increment(ref done)}/{dependencies.Count}"
                );
            },
            ct
        );
    }
}
