using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 一个 frp 客户端发行版的元数据：版本号、官方下载地址、SHA-256 校验和与文件大小。
// 下载前用 Url/Sha256/Size 三重校验，防止篡改或不完整的包。
public sealed record FrpcRelease(string Version, string Url, string Sha256, long Size);

// 一页 frp 客户端版本列表。Source 标记数据来源（"api" 走 GitHub API，"web" 走
// 官方 releases 网页解析），IsCached 表示该页来自本地缓存而非实时请求。
public sealed record FrpcReleasePage(
    string[] Versions,
    bool HasMore,
    string Source = "api",
    bool IsCached = false
);

// frpc 的配置解析交给官方客户端；工作目录保持为配置所在目录，兼容 includes 和证书相对路径。
// 负责 frp 客户端的下载、安装、校验、启动与停止，所有操作经 gate 串行化以避免并发冲突。
public sealed class FrpcManager(Store store, LauncherCore core) : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    Process? process;
    Task completion = Task.CompletedTask;
    bool disposed;
    public event Action<string>? Output;
    public event Action? Changed;
    // frpc 客户端安装根目录（非 Windows x64 时按平台/架构分子目录）。
    public string Root =>
        Store.SafePath(
            store.Root,
            "tools/frpc"
                + (
                    MinecraftPlatform.OsName == "windows"
                    && MinecraftPlatform.Architecture == "x86_64"
                        ? ""
                        : "/" + MinecraftPlatform.OsName + "-" + MinecraftPlatform.Architecture
                )
        );
    // frpc 进程是否正在运行。
    public bool Running
    {
        get
        {
            try
            {
                return process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
    // 最近一次进程退出码。
    public int? ExitCode { get; private set; }
    // 当前运行使用的配置文件路径。
    public string? ActiveConfig { get; private set; }
    // 当前运行使用的客户端版本号。
    public string? ActiveVersion { get; private set; }

    // 合法版本号格式（允许前导 v）。
    static readonly Regex VersionPattern = new(@"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

    // 规范化版本号（去掉前导 v），非法则抛异常。
    public static string Version(string value) =>
        VersionPattern.IsMatch(value)
            ? value.TrimStart('v')
            : throw new IOException("Invalid Frp Client version (example: 0.71.1).");

    // 当前平台的 frpc 可执行文件名。
    static string ClientName => MinecraftPlatform.OsName == "windows" ? "frpc.exe" : "frpc";

    // 构造当前平台对应的 GitHub 发布资产文件名。
    static string AssetName(string version)
    {
        var os = MinecraftPlatform.OsName switch
        {
            "windows" => "windows",
            "linux" => "linux",
            "osx" => "darwin",
            _ => throw new PlatformNotSupportedException(),
        };
        var arch = MinecraftPlatform.Architecture switch
        {
            "x86_64" => "amd64",
            "aarch64" => "arm64",
            _ => throw new PlatformNotSupportedException("Unsupported Frp Client architecture."),
        };
        return $"frp_{version}_{os}_{arch}." + (os == "windows" ? "zip" : "tar.gz");
    }

    // 指定版本 frpc 可执行文件的完整路径。
    public string Executable(string version) =>
        Store.SafePath(Root, Version(version) + "/" + ClientName);

    // 列出已安装的客户端版本（按版本号降序）。
    public string[] Installed() =>
        Directory.Exists(Root)
            ? Directory
                .EnumerateDirectories(Root)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(v => VersionPattern.IsMatch(v) && File.Exists(Executable(v)))
                .OrderByDescending(v => System.Version.Parse(v))
                .ToArray()
            : [];

    // 删除指定版本的客户端（运行中时禁止删除）。
    public async Task Remove(string version, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Running)
                throw new IOException("Stop Frp Client before deleting a client version.");
            var target = Store.SafePath(Root, Version(version));
            if (!Directory.Exists(target))
                return;
            // Validate every entry before recursive removal, including directory junctions.
            void Validate(string directory)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    Store.SafePath(Root, Path.GetRelativePath(Root, entry));
                    if (Directory.Exists(entry))
                        Validate(entry);
                }
            }
            Validate(target);
            ct.ThrowIfCancellationRequested();
            Directory.Delete(target, true);
        }
        finally
        {
            gate.Release();
        }
    }

    // 查找指定版本（或 latest）的发布元数据；
    // 优先 GitHub API，失败时回退到官方 releases 网页，再失败时读本地缓存。
    public async Task<FrpcRelease> FindRelease(string version, CancellationToken ct)
    {
        _ = AssetName("0.0.0");
        ct.ThrowIfCancellationRequested();
        var latest = string.IsNullOrWhiteSpace(version) || version == "latest";
        var resolved = latest ? "latest" : Version(version);
        try
        {
            var result = await FindApiRelease(version, ct);
            await Cache("release-" + result.Version, result, ct);
            return result;
        }
        catch (Exception ex) when (CanFallback(ex, ct))
        {
            Emit(
                "Frp Client release API unavailable; using official release downloads. "
                    + ex.Message
            );
            try
            {
                if (latest)
                    resolved = (await WebReleases(1, ct)).Versions.First();
                var name = AssetName(resolved);
                var prefix = $"https://github.com/fatedier/frp/releases/download/v{resolved}/";
                var checksums = await core.Http.GetStringAsync(
                    prefix + "frp_sha256_checksums.txt",
                    ct
                );
                var hash = Regex
                    .Match(
                        checksums,
                        @"(?m)^([a-fA-F0-9]{64})\s+\*?" + Regex.Escape(name) + @"\s*$"
                    )
                    .Groups[1]
                    .Value;
                if (hash.Length != 64)
                    throw new IOException("Official SHA-256 checksum is unavailable.");
                using var request = new HttpRequestMessage(HttpMethod.Head, prefix + name);
                using var response = await core.Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );
                response.EnsureSuccessStatusCode();
                var size =
                    response.Content.Headers.ContentLength
                    ?? throw new IOException("Official archive size is unavailable.");
                var result = new FrpcRelease(resolved, prefix + name, hash, size);
                await Cache("release-" + resolved, result, ct);
                return result;
            }
            catch (Exception fallback) when (CanFallback(fallback, ct))
            {
                var cached = ReadCache<FrpcRelease>("release-" + resolved);
                if (
                    cached != null
                    && cached.Version == resolved
                    && OfficialAsset(cached.Url)
                    && !string.IsNullOrEmpty(cached.Sha256)
                    && Regex.IsMatch(cached.Sha256, "^[a-fA-F0-9]{64}$")
                )
                {
                    Emit("Using cached official Frp Client release metadata.");
                    return cached;
                }
                throw new IOException(
                    "GitHub API and official release downloads are unavailable. Check the launcher proxy/network settings.",
                    fallback
                );
            }
        }
    }

    // 通过 GitHub API 获取发布元数据（含 SHA-256 校验和与文件大小）。
    async Task<FrpcRelease> FindApiRelease(string version, CancellationToken ct)
    {
        var endpoint =
            string.IsNullOrWhiteSpace(version) || version == "latest"
                ? "latest"
                : "tags/v" + Version(version);
        var json = JsonNode.Parse(
            await Api("https://api.github.com/repos/fatedier/frp/releases/" + endpoint, ct)
        )!;
        var resolved = Version(json["tag_name"]?.GetValue<string>() ?? "");
        var name = AssetName(resolved);
        var assets =
            json["assets"]?.AsArray() ?? throw new IOException("Frp Client release has no assets.");
        var asset =
            assets.FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)
            ?? throw new IOException("Frp Client package for this platform was not found.");
        var url = asset["browser_download_url"]!.GetValue<string>();
        if (!OfficialAsset(url))
            throw new IOException("Unexpected Frp Client download source.");
        var digest = asset["digest"]?.GetValue<string>() ?? "";
        var hash = digest.StartsWith("sha256:") ? digest[7..] : "";
        if (hash.Length == 0)
        {
            var checksums = assets.FirstOrDefault(a =>
                a?["name"]?.GetValue<string>() == "frp_sha256_checksums.txt"
            );
            var checksumUrl = checksums?["browser_download_url"]?.GetValue<string>() ?? "";
            if (!OfficialAsset(checksumUrl))
                throw new IOException("Official SHA-256 checksums are unavailable.");
            var text = await core.Http.GetStringAsync(checksumUrl, ct);
            hash = Regex
                .Match(text, @"(?m)^([a-fA-F0-9]{64})\s+\*?" + Regex.Escape(name) + @"\s*$")
                .Groups[1]
                .Value;
        }
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
            throw new IOException("Official SHA-256 checksum is invalid.");
        return new(resolved, url, hash, asset["size"]!.GetValue<long>());
    }

    // 分页获取可用版本列表；优先 API，回退到网页解析，再回退到缓存。
    public async Task<FrpcReleasePage> Releases(int page, CancellationToken ct)
    {
        _ = AssetName("0.0.0");
        ct.ThrowIfCancellationRequested();
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        try
        {
            var result = await ApiReleases(page, ct);
            await Cache("page-" + page, result, ct);
            return result;
        }
        catch (Exception ex) when (CanFallback(ex, ct))
        {
            Emit(
                "GitHub release API unavailable; trying the official releases page. " + ex.Message
            );
            try
            {
                var result = await WebReleases(page, ct);
                await Cache("page-" + page, result, ct);
                return result;
            }
            catch (Exception fallback) when (CanFallback(fallback, ct))
            {
                var cached = ReadCache<FrpcReleasePage>("page-" + page);
                if (
                    cached is { Versions: { Length: > 0 }, Source: "api" or "web" }
                    && cached.Versions.All(v => v != null && VersionPattern.IsMatch(v))
                )
                    return cached with { IsCached = true };
                throw new IOException(
                    "GitHub API and official releases page are unavailable; no cached versions exist. Check the launcher proxy/network settings.",
                    fallback
                );
            }
        }
    }

    // 通过 GitHub API 分页获取版本列表（过滤 draft/prerelease，仅保留含当前平台资产的版本）。
    async Task<FrpcReleasePage> ApiReleases(int page, CancellationToken ct)
    {
        var items =
            JsonNode
                .Parse(
                    await Api(
                        $"https://api.github.com/repos/fatedier/frp/releases?per_page=100&page={page}",
                        ct
                    )
                )
                ?.AsArray() ?? throw new IOException("Release list is unavailable.");
        var versions = items
            .Where(item =>
                item != null
                && item["draft"]?.GetValue<bool>() != true
                && item["prerelease"]?.GetValue<bool>() != true
            )
            .Select(item =>
                (
                    Tag: item!["tag_name"]?.GetValue<string>() ?? "",
                    Assets: item["assets"] as JsonArray
                )
            )
            .Where(item =>
                VersionPattern.IsMatch(item.Tag)
                && item.Assets?.Any(a =>
                    a?["name"]?.GetValue<string>() == AssetName(Version(item.Tag))
                ) == true
            )
            .Select(item => Version(item.Tag))
            .Distinct()
            .OrderByDescending(v => System.Version.Parse(v))
            .ToArray();
        return new(versions, items.Count == 100);
    }

    // GitHub API 限流后的重试时间。
    DateTimeOffset apiRetryAfter;

    // 请求 GitHub API，处理 403/429 限流并记录重试时间。
    async Task<string> Api(string url, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < apiRetryAfter)
            throw new HttpRequestException(
                "GitHub API is cooling down until " + apiRetryAfter.ToString("O"),
                null,
                System.Net.HttpStatusCode.TooManyRequests
            );
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CMCLauncher/frpc");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await core.Http.SendAsync(request, ct);
        if ((int)response.StatusCode is 403 or 429)
        {
            var retry =
                response.Headers.RetryAfter?.Date
                ?? DateTimeOffset.UtcNow.Add(
                    response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)
                );
            if (
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                && remaining.Contains("0")
                && response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                && long.TryParse(values.FirstOrDefault(), out var reset)
            )
            {
                try
                {
                    var at = DateTimeOffset.FromUnixTimeSeconds(reset);
                    if (at > retry)
                        retry = at;
                }
                catch (ArgumentOutOfRangeException) { }
            }
            apiRetryAfter = retry;
            Emit(
                $"GitHub API {(int)response.StatusCode}; retry after {retry:O}. Using public releases without retrying the API."
            );
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // 判断异常是否可触发回退逻辑（非取消且为网络/解析类异常）。
    static bool CanFallback(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex
            is HttpRequestException
                or IOException
                or System.Text.Json.JsonException
                or OperationCanceledException
                or InvalidOperationException;

    // 解析官方 releases 网页获取版本列表（API 不可用时的回退方案）。
    async Task<FrpcReleasePage> WebReleases(int page, CancellationToken ct)
    {
        var html = await core.Http.GetStringAsync(
            $"https://github.com/fatedier/frp/releases?page={page}",
            ct
        );
        var versions = Regex
            .Matches(html, @"href=[""']/fatedier/frp/releases/tag/v(\d+\.\d+\.\d+)[""']")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderByDescending(v => System.Version.Parse(v))
            .ToArray();
        if (versions.Length == 0 && page == 1)
            throw new IOException("Official release page contains no recognizable versions.");
        return new(versions, Regex.IsMatch(html, @"rel=[""']next[""']"), "web");
    }

    // 缓存文件路径。
    string CachePath(string key) => Store.SafePath(Root, "cache/" + key + ".json");

    // 将值写入本地缓存（失败仅记录日志，不中断主流程）。
    async Task Cache<T>(string key, T value, CancellationToken ct)
    {
        try
        {
            var path = CachePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await Store.AtomicJson(path, System.Text.Json.JsonSerializer.SerializeToNode(value)!);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Emit("Frp Client cache unavailable: " + ex.GetType().Name);
        }
    }

    // 从本地缓存读取值；不存在或损坏时返回 null。
    T? ReadCache<T>(string key)
        where T : class
    {
        try
        {
            var path = CachePath(key);
            return File.Exists(path) && new FileInfo(path).Length < 4_000_000
                ? System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    // 校验 URL 是否为 GitHub 官方 frp 发布下载地址。
    static bool OfficialAsset(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && uri.Host == "github.com"
        && uri.AbsolutePath.StartsWith(
            "/fatedier/frp/releases/download/",
            StringComparison.Ordinal
        );

    // 下载并安装指定版本的 frpc 客户端（校验 SHA-256 与文件大小，原子移动到位）。
    public async Task Install(FrpcRelease release, CancellationToken ct)
    {
        _ = AssetName("0.0.0");
        await gate.WaitAsync(ct);
        string? stage = null;
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Running)
                throw new IOException("Stop Frp Client before installing a client.");
            if (
                !OfficialAsset(release.Url)
                || !Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$")
                || release.Size is <= 0 or > 100_000_000
            )
                throw new IOException("Invalid Frp Client release metadata.");
            var target = Store.SafePath(Root, Version(release.Version));
            if (File.Exists(Executable(release.Version)))
                return;
            stage = Store.SafePath(Root, ".download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var zip = Path.Combine(
                stage,
                MinecraftPlatform.OsName == "windows" ? "frpc.zip" : "frpc.tar.gz"
            );
            await core.Download(release.Url, zip, release.Sha256, "sha256", release.Size, ct);
            await ExtractClient(zip, Path.Combine(stage, "client"), ct);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target))
                throw new IOException("Incomplete Frp Client installation already exists.");
            Directory.Move(Path.Combine(stage, "client"), target);
            Emit("Installed Frp Client " + release.Version);
        }
        finally
        {
            if (stage != null && Directory.Exists(stage))
                Directory.Delete(stage, true);
            gate.Release();
        }
    }

    // 从发布包中提取 frpc 可执行文件到目标目录；
    // Windows 只提取白名单文件（frpc.exe + LICENSE），非 Windows 校验 ELF/Mach-O 魔数。
    public static async Task ExtractClient(string zip, string destination, CancellationToken ct)
    {
        if (MinecraftPlatform.OsName != "windows")
        {
            var staging = destination + ".unpack";
            try
            {
                await RuntimeArchive.Extract(zip, staging, true, 300_000_000, ct);
                var clients = Directory.GetFiles(staging, "frpc", SearchOption.AllDirectories);
                if (clients.Length != 1)
                    throw new IOException("Invalid Frp Client archive.");
                await using (var input = File.OpenRead(clients[0]))
                {
                    var magic = new byte[4];
                    if (
                        await input.ReadAsync(magic, ct) != 4
                        || !(
                            magic.AsSpan().SequenceEqual(new byte[] { 127, 69, 76, 70 })
                            || magic.AsSpan().SequenceEqual(new byte[] { 207, 250, 237, 254 })
                            || magic.AsSpan().SequenceEqual(new byte[] { 202, 254, 186, 190 })
                        )
                    )
                        throw new IOException("Invalid Frp Client executable.");
                }
                Directory.CreateDirectory(destination);
                var target = Store.SafePath(destination, "frpc");
                File.Move(clients[0], target);
#if NET7_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(
                        target,
                        UnixFileMode.UserRead
                            | UnixFileMode.UserWrite
                            | UnixFileMode.UserExecute
                            | UnixFileMode.GroupRead
                            | UnixFileMode.GroupExecute
                            | UnixFileMode.OtherRead
                            | UnixFileMode.OtherExecute
                    );
#endif
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            return;
        }
        using var archive = ZipFile.OpenRead(zip);
        var executables = archive
            .Entries.Where(e => e.Name.Equals("frpc.exe", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (executables.Length != 1 || executables[0].Length is < 2 or > 150_000_000)
            throw new IOException("Invalid Frp Client executable in archive.");
        // 不按归档路径解压，只提取白名单文件到固定位置，忽略 frps 和其他内容。
        Directory.CreateDirectory(destination);
        foreach (
            var entry in archive.Entries.Where(e =>
                ReferenceEquals(e, executables[0]) || e.Name == "LICENSE"
            )
        )
        {
            if (entry.Length > 150_000_000)
                throw new IOException("Frp Client archive entry is too large.");
            await using var input = entry.Open();
            await using var output = new FileStream(
                Store.SafePath(destination, entry.Name),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true
            );
            await input.CopyToAsync(output, ct);
        }
        var signature = new byte[2];
        await using var exe = File.OpenRead(Store.SafePath(destination, "frpc.exe"));
        if (await exe.ReadAsync(signature, ct) != 2 || signature[0] != 'M' || signature[1] != 'Z')
            throw new IOException("Frp Client is not a Windows executable.");
    }

    // 校验配置文件路径（.ini/.toml 且存在），返回完整路径。
    public static string Config(string path)
    {
        var full = Path.GetFullPath(path);
        if (
            !new[] { ".ini", ".toml" }.Contains(
                Path.GetExtension(full),
                StringComparer.OrdinalIgnoreCase
            ) || !File.Exists(full)
        )
            throw new IOException("Select an existing .ini or .toml configuration.");
        return full;
    }

    // 构造 frpc 进程启动信息（工作目录为配置所在目录，verify 时加 verify 参数）。
    public ProcessStartInfo StartInfo(string version, string config, bool verify = false)
    {
        _ = AssetName("0.0.0");
        var path = Config(config);
        var exe = Executable(version);
        if (!File.Exists(exe))
            throw new IOException("Download Frp Client first.");
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (verify)
            info.ArgumentList.Add("verify");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(path);
        return CMCLauncher.Platform.PlatformCommand.SystemEnvironment(info);
    }

    // 验证配置文件是否合法（15 秒超时）。
    public async Task Verify(string version, string config, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            await VerifyCore(version, config, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    // 执行 frpc verify 子命令，非零退出码视为验证失败。
    async Task VerifyCore(string version, string config, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var child =
            Process.Start(StartInfo(version, config, true))
            ?? throw new IOException("Could not start Frp Client verification.");
        try
        {
            var stdout = Pump(child.StandardOutput);
            var stderr = Pump(child.StandardError);
            await child.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            if (child.ExitCode != 0)
                throw new IOException(
                    "Frp Client configuration verification failed. See Frp Client logs."
                );
            Emit("Configuration verified.");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(true);
                await child.WaitForExitAsync();
            }
        }
    }

    // 启动 frpc 进程（先验证配置，再启动并观察输出）。
    public async Task Start(string version, string config, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Running)
                throw new IOException("Frp Client is already running.");
            await completion;
            await VerifyCore(version, config, ct);
            ct.ThrowIfCancellationRequested();
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            process?.Dispose();
            ExitCode = null;
            process =
                Process.Start(StartInfo(version, config))
                ?? throw new IOException("Could not start Frp Client.");
            ActiveConfig = Config(config);
            ActiveVersion = Version(version);
            completion = Observe(process);
            Changed?.Invoke();
        }
        finally
        {
            gate.Release();
        }
    }

    // 观察子进程：读取 stdout/stderr 并等待退出，更新退出码。
    async Task Observe(Process child)
    {
        var stdout = Pump(child.StandardOutput);
        var stderr = Pump(child.StandardError);
        await child.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        ExitCode = child.ExitCode;
        Emit("Frp Client exited: " + ExitCode);
        Changed?.Invoke();
    }

    // 逐行读取流并触发 Output 事件（单行截断 4096 字符）。
    async Task Pump(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            Emit(line.Length > 4096 ? line[..4096] + "…" : line);
    }

    // 脱敏输出：将 token/secret_key/password 等敏感值替换为 [REDACTED]。
    public static string CleanOutput(string line) =>
        Regex.Replace(
            line,
            @"(?i)((?:auth\.)?token|secret_?key|password)([""']?\s*[:=]\s*[""']?)[^\s""',}]+",
            "$1$2[REDACTED]"
        );

    // 触发 Output 事件（自动脱敏）。
    void Emit(string line) => Output?.Invoke(CleanOutput(line));

    // 停止 frpc 进程并等待其退出。
    public async Task Stop()
    {
        await gate.WaitAsync();
        try
        {
            if (Running)
                process!.Kill(true);
            await completion;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        try
        {
            if (Running)
                process!.Kill(true);
        }
        catch (InvalidOperationException) { }
        // 保持 Process 句柄到异步输出读取结束；不影响窗口关闭。
        _ = completion.ContinueWith(_ => process?.Dispose(), TaskScheduler.Default);
    }
}
