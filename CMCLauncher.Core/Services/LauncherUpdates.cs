using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// Release tags have historically used CMCL-v1.0.0-release, v1.0.0 and 1.0.0-beta.
public sealed record LauncherVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<LauncherVersion>
{
    // 解析版本号（兼容 CMCL-v1.0.0-release、v1.0.0、1.0.0-beta 等历史标签格式），非法时返回 null。
    public static LauncherVersion? Parse(string value)
    {
        var match = Regex.Match(
            value.Trim(),
            @"^(?:CMCL-)?v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (
            !match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch)
        )
            return null;
        var pre = match.Groups[4].Value.ToLowerInvariant();
        if (pre == "release")
            pre = "";
        if (pre.Split('.').Any(p => p.Length == 0) && pre.Length > 0)
            return null;
        return new(major, minor, patch, pre);
    }

    // 按语义化版本比较：先比主/次/修订号，再比预发布号（正式版大于预发布版）。
    public int CompareTo(LauncherVersion? other)
    {
        if (other is null)
            return 1;
        foreach (
            var result in new[]
            {
                Major.CompareTo(other.Major),
                Minor.CompareTo(other.Minor),
                Patch.CompareTo(other.Patch),
            }
        )
            if (result != 0)
                return result;
        if (PreRelease == other.PreRelease)
            return 0;
        if (PreRelease.Length == 0)
            return 1;
        if (other.PreRelease.Length == 0)
            return -1;
        // Also accept the beta-2 / beta2 spelling used by older release tags.
        var left = Regex.Replace(PreRelease, @"^(alpha|beta|rc)[.-]?(\d+)$", "$1.$2").Split('.');
        var right = Regex
            .Replace(other.PreRelease, @"^(alpha|beta|rc)[.-]?(\d+)$", "$1.$2")
            .Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = System.Numerics.BigInteger.TryParse(
                left[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var l
            );
            var rn = System.Numerics.BigInteger.TryParse(
                right[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var r
            );
            var result =
                ln && rn ? l.CompareTo(r)
                : ln != rn ? (ln ? -1 : 1)
                : string.CompareOrdinal(left[i], right[i]);
            if (result != 0)
                return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (PreRelease.Length == 0 ? "" : "-" + PreRelease);
}

// 一个更新包资产：文件名、下载地址、大小与 SHA-256 校验和。
public sealed record LauncherUpdateAsset(string Name, Uri Url, long Size, string Sha256);

// 一个 GitHub 发布：标签、解析后的版本、是否预发布、发布页地址、说明文本与匹配的更新包资产。
public sealed record LauncherRelease(
    string Tag,
    LauncherVersion Version,
    bool IsBeta,
    Uri Page,
    string Notes,
    LauncherUpdateAsset? Asset
);

// 启动器更新检查与下载：从 GitHub Releases 获取版本列表，
// 校验资产 URL 与 SHA-256，支持镜像加速下载。
public sealed class LauncherUpdates(HttpClient client)
{
    // 当前仓库（更新资产 URL 校验白名单）。
    public const string Repository = "CMCLauncher-Dev/CMCLauncher";
    // 旧仓库（兼容历史发布资产）。
    const string LegacyRepository = "com-in/CMCLauncher";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

    // 当前程序集版本号（取 InformationalVersion 的 + 前部分）。
    public static string CurrentVersion =>
        typeof(LauncherUpdates)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "1.0.1";

    // Updates always validate TLS, even if the game downloader's SSL bypass is enabled.
    public static HttpClient CreateClient(Settings settings)
    {
        var handler = new HttpClientHandler();
        handler.Proxy = NetworkProxy.Create(settings);
        handler.UseProxy = handler.Proxy != null;
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
    }

    // 构造带 User-Agent 与 GitHub API Accept 头的 GET 请求。
    static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CMCLauncher/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    // 校验 URL 是否为官方 GitHub 发布资产下载地址（https、默认端口、无用户信息）。
    public static bool IsAssetUrl(Uri uri) =>
        uri.Scheme == "https"
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && (
            uri.AbsolutePath.StartsWith(
                "/" + Repository + "/releases/download/",
                StringComparison.OrdinalIgnoreCase
            )
            || uri.AbsolutePath.StartsWith(
                "/" + LegacyRepository + "/releases/download/",
                StringComparison.OrdinalIgnoreCase
            )
        );

    // 加速下载前缀（下载镜像）：仅作用于启动器更新包下载。
    // 未配置或地址不属于官方 GitHub 发布资产时原样返回。
    public static Uri AcceleratedAssetUrl(Uri url, string? mirror)
    {
        var prefix = mirror?.Trim().TrimEnd('/');
        if (
            string.IsNullOrEmpty(prefix)
            || !IsAssetUrl(url)
            || !Uri.TryCreate(prefix, UriKind.Absolute, out var mirrorUri)
            || mirrorUri.Scheme is not ("http" or "https")
        )
            return url;
        return new Uri(mirrorUri.AbsoluteUri.TrimEnd('/') + "/" + url.AbsoluteUri);
    }

    // 内置的 GitHub 加速下载前缀候选，下载前会同用户自定义前缀一起做连通性测速。
    static readonly string[] KnownMirrors =
    [
        "https://ghfast.top",
        "https://ghproxy.net",
        "https://gh-proxy.com",
        "https://ghps.cc",
        "https://ghproxy.cc",
    ];

    // 把镜像前缀拼到资产 URL 前。
    static Uri Mirrored(Uri assetUrl, string prefix) =>
        new(prefix.TrimEnd('/') + "/" + assetUrl.AbsoluteUri);

    // 对候选前缀并行做连接 + 首字节测速，返回最快且可达的前缀；全部不可达时返回 null。
    public static async Task<string?> SelectFastestMirrorAsync(
        Uri assetUrl,
        string? custom,
        HttpClient client,
        CancellationToken ct
    )
    {
        if (!IsAssetUrl(assetUrl))
            return null;
        var candidates = new List<string>();
        var normalised = custom?.Trim().TrimEnd('/');
        if (
            !string.IsNullOrEmpty(normalised)
            && Uri.TryCreate(normalised, UriKind.Absolute, out var from)
            && from.Scheme is ("http" or "https")
        )
            candidates.Add(normalised);
        candidates.AddRange(KnownMirrors);

        var best = (Prefix: (string?)null, Ms: long.MaxValue);
        await Task.WhenAll(
            candidates.Select(async prefix =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    using var request = Request(Mirrored(assetUrl, prefix));
                    using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token
                    );
                    if (!response.IsSuccessStatusCode)
                        return;
                    clock.Stop();
                    var ms = clock.ElapsedMilliseconds;
                    lock (candidates)
                    {
                        if (ms < best.Ms)
                            best = (prefix, ms);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch { }
            })
        );
        return best.Prefix;
    }

    // 解析 GitHub Releases API 的 JSON 响应为发布列表；
    // 按 singleFile/desktopRid 匹配当前平台对应的更新包资产（含 SHA-256 校验和）。
    public static IReadOnlyList<LauncherRelease> ParseReleases(
        string json,
        bool singleFile,
        string? desktopRid = null
    )
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<LauncherRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean())
                continue;
            var tag = item.GetProperty("tag_name").GetString() ?? "";
            var version = LauncherVersion.Parse(tag);
            if (version is null)
                continue;
            var beta = item.GetProperty("prerelease").GetBoolean() || version.PreRelease.Length > 0;
            var page = new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag));
            LauncherUpdateAsset? asset = null;
            foreach (var candidate in item.GetProperty("assets").EnumerateArray())
            {
                var name = candidate.GetProperty("name").GetString() ?? "";
                var extension = singleFile ? "exe" : "zip";
                var pattern =
                    desktopRid == null
                        ? @"^CMCLauncher-(?:[0-9A-Za-z.+-]+-)?win-x64\." + extension + "$"
                        : @"^CMCLauncher-Avalonia-(?:[0-9A-Za-z.+-]+-)?"
                            + Regex.Escape(desktopRid)
                            + (desktopRid.StartsWith("win-") ? @"\.zip$" : @"\.tar\.gz$");
                if (
                    desktopRid == null
                    && name.StartsWith("CMCLauncher-Avalonia-", StringComparison.OrdinalIgnoreCase)
                )
                    continue;
                if (!Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    continue;
                if (
                    !Uri.TryCreate(
                        candidate.GetProperty("browser_download_url").GetString(),
                        UriKind.Absolute,
                        out var url
                    ) || !IsAssetUrl(url)
                )
                    continue;
                var size = candidate.GetProperty("size").GetInt64();
                if (size <= 0)
                    continue;
                var digest = candidate.TryGetProperty("digest", out var d)
                    ? d.GetString() ?? ""
                    : "";
                var hash = Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$") ? digest[7..] : "";
                asset = new(name, url, size, hash);
                break;
            }
            releases.Add(
                new(
                    tag,
                    version,
                    beta,
                    page,
                    item.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                    asset
                )
            );
        }
        return releases;
    }

    // 从发布列表中选出应安装的最新版本（高于当前版本，按是否接收预发布过滤）。
    public static LauncherRelease? Select(
        IEnumerable<LauncherRelease> releases,
        string current,
        bool receiveBeta
    )
    {
        var version =
            LauncherVersion.Parse(current)
            ?? throw new InvalidDataException("Cannot read the current launcher version.");
        return releases
            .Where(r => (receiveBeta || !r.IsBeta) && r.Version.CompareTo(version) > 0)
            .OrderByDescending(r => r.Version)
            .ThenBy(r => r.IsBeta)
            .FirstOrDefault();
    }

    // 分页拉取全部发布（最多 10 页，单页响应限制 8 MB）并选出最新版本。
    public async Task<LauncherRelease?> Check(
        string current,
        bool receiveBeta,
        bool singleFile,
        CancellationToken ct,
        string? desktopRid = null
    )
    {
        var releases = new List<LauncherRelease>();
        // Do not rely on /latest or publication order: old releases can be republished.
        for (var page = 1; page <= 10; page++)
        {
            using var request = Request(
                new Uri(
                    $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}"
                )
            );
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, ct)) > 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024)
                    throw new InvalidDataException("Release response is too large.");
                buffer.Write(bytes, 0, read);
            }
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            releases.AddRange(ParseReleases(json, singleFile, desktopRid));
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetArrayLength() < 100)
                return Select(releases, current, receiveBeta);
        }
        throw new InvalidDataException(
            "Too many release pages. Open the release page to check for updates."
        );
    }

    // 下载更新包到目标路径：先写 .partial 临时文件，边下载边计算 SHA-256，
    // 校验大小与哈希通过后原子移动到位。
    public async Task Download(
        LauncherUpdateAsset asset,
        string destination,
        IProgress<double>? progress,
        CancellationToken ct,
        string? mirror = null
    )
    {
        if (
            !IsAssetUrl(asset.Url)
            || asset.Size <= 0
            || !Regex.IsMatch(asset.Sha256, "^[0-9a-fA-F]{64}$")
        )
            throw new InvalidDataException(
                "The update package has no valid SHA-256 digest. Download it from the release page."
            );
        // 下载连通性测速：从用户配置与内置候选里选出最快可达的前缀。
        var fastest = await SelectFastestMirrorAsync(asset.Url, mirror, client, ct);
        if (fastest != null)
            mirror = fastest;
        var temporary = destination + ".partial";
        try
        {
            using var request = Request(AcceleratedAssetUrl(asset.Url, mirror));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (
                var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                )
            )
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var bytes = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(bytes, ct)) > 0)
                {
                    total += read;
                    if (total > asset.Size)
                        throw new InvalidDataException("Unexpected update package size.");
                    await output.WriteAsync(bytes.AsMemory(0, read), ct);
                    hash.AppendData(bytes, 0, read);
                    progress?.Report(total * 100.0 / asset.Size);
                }
                if (
                    total != asset.Size
                    || !Convert
                        .ToHexString(hash.GetHashAndReset())
                        .Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidDataException(
                        "The update package failed SHA-256 verification."
                    );
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
