using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed partial class LauncherCore
{
    internal static Uri GameMirrorUrl(Uri uri)
    {
        // NeoForged publishes the same release artifacts on its official ForgeCDN mirror.
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

    static bool IsInstallerTransportFailure(Exception ex) =>
        ex is HttpIOException
        || ex is IOException { InnerException: System.Net.Sockets.SocketException }
        || ex is HttpRequestException request
            && (
                request.StatusCode == null
                || request.StatusCode
                    is HttpStatusCode.RequestTimeout
                        or HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout
            );

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
        _ = NetworkProxy.Create(settings); // Reuse hostname-pattern validation before constructing JVM options.
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

    // The Java installer has a separate HTTP stack. Populate its verified Maven cache through
    // the launcher's proxy/mirror pipeline before starting Java, without weakening Java TLS.
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
            // Missing/empty URLs identify legacy, embedded or processor-generated artifacts.
            // Those remain under the official installer's control instead of guessing download URLs.
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
