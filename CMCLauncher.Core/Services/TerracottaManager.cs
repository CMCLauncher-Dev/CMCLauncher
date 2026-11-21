using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CMCLauncher.Platform;

namespace CMCLauncher;

// 一个 Terracotta 发行版：版本号、资产文件名、下载地址、SHA256 哈希与大小。
public sealed record TerracottaRelease(
    string Version,
    string Asset,
    string Url,
    string Hash,
    long Size
);

// 一次已安装的 Terracotta：版本号、可执行文件相对路径、以及全部文件的 SHA256 哈希表。
public sealed record TerracottaInstallation(
    string Version,
    string Executable,
    Dictionary<string, string> Files
);

// Terracotta remains a separate, unmodified upstream executable. Only an explicit
// download action contacts GitHub; switching pages never installs or starts it.
public sealed class TerracottaManager(Store store, LauncherCore core) : IDisposable
{
    public const string SourceUrl = "https://github.com/burningtnt/Terracotta";
    readonly SemaphoreSlim operations = new(1, 1);
    readonly SemaphoreSlim requests = new(1, 1);
    readonly SemaphoreSlim wake = new(0, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly HttpClient local = new(
        new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 1024 * 1024,
    };
    int port;
    long index = -1;
    long profileIndex = -1;
    int connection;
    bool disposed;
    bool active;
    public bool Active
    {
        get => active;
        set
        {
            if (active == value)
                return;
            active = value;
            if (value && wake.CurrentCount == 0)
                wake.Release();
        }
    }
    public bool Connected => port != 0;
    public JsonNode? State { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    public event Action<string>? Output;
    public string Root =>
        Store.SafePath(
            store.Root,
            $"tools/terracotta/{MinecraftPlatform.OsName}-{MinecraftPlatform.Architecture}"
        );
    string Current => Store.SafePath(Root, "current.json");
    public TerracottaInstallation? Installed
    {
        get
        {
            try
            {
                var value = JsonSerializer.Deserialize(
                    File.ReadAllText(Current),
                    TerracottaJsonContext.Default.TerracottaInstallation
                );
                return
                    value != null
                    && value.Files.Count > 0
                    && File.Exists(Store.SafePath(Root, value.Executable))
                    ? value
                    : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }
    }

    // 按版本号、操作系统与架构拼出官方资产文件名（terracotta-{ver}-{os}-{arch}-pkg.tar.gz）。
    public static string AssetName(string version, string os, string arch)
    {
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new IOException("Invalid Terracotta version.");
        os = os switch
        {
            "windows" => "windows",
            "linux" => "linux",
            "osx" => "macos",
            _ => throw new PlatformNotSupportedException(),
        };
        arch = arch switch
        {
            "x86_64" => "x86_64",
            "aarch64" => "arm64",
            _ => throw new PlatformNotSupportedException(),
        };
        return $"terracotta-{version}-{os}-{arch}-pkg.tar.gz";
    }

    // 解析 GitHub 最新发行版 JSON，挑出匹配当前平台的资产并校验其 URL、哈希与大小。
    public static TerracottaRelease ParseRelease(JsonNode data, string os, string arch)
    {
        var version = (data["tag_name"]?.GetValue<string>() ?? "").TrimStart('v');
        var name = AssetName(version, os, arch);
        var asset =
            data["assets"]?.AsArray().FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)
            ?? throw new IOException("No Terracotta download is available for this platform.");
        var url = asset["browser_download_url"]?.GetValue<string>() ?? "";
        var hash = asset["digest"]?.GetValue<string>() ?? "";
        var size = asset["size"]?.GetValue<long>() ?? 0;
        if (
            url != $"{SourceUrl}/releases/download/v{version}/{name}"
            || !Regex.IsMatch(hash, "^sha256:[a-fA-F0-9]{64}$")
            || size is <= 0 or > 100 * 1024 * 1024
        )
            throw new IOException(
                "The official Terracotta download has no valid checksum or size."
            );
        return new(version, name, url, hash[7..], size);
    }

    // 下载并安装 Terracotta：拉取最新发行版、校验哈希、解包、
    // 计算全部文件哈希并原子写入 current.json；失败时清理临时目录。
    public async Task Install(CancellationToken ct)
    {
        await operations.WaitAsync(ct);
        string? stage = null;
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Connected)
                throw new IOException("Stop Terracotta before updating it.");
            var release = ParseRelease(
                await core.Json(
                    "https://api.github.com/repos/burningtnt/Terracotta/releases/latest",
                    ct
                ),
                MinecraftPlatform.OsName,
                MinecraftPlatform.Architecture
            );
            Directory.CreateDirectory(Root);
            stage = Store.SafePath(Root, "install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var archive = Path.Combine(stage, "package.tar.gz");
            await core.Download(release.Url, archive, release.Hash, "sha256", release.Size, ct);
            var files = Path.Combine(stage, "files");
            await RuntimeArchive.Extract(archive, files, true, 300 * 1024 * 1024, ct);
            var executableName = release.Asset.Replace(
                "-pkg.tar.gz",
                MinecraftPlatform.OsName == "windows" ? ".exe" : ""
            );
            var executable =
                Directory
                    .EnumerateFiles(files, executableName, SearchOption.AllDirectories)
                    .SingleOrDefault()
                ?? throw new IOException("The Terracotta archive does not contain its executable.");
            var folder = "client-" + Guid.NewGuid().ToString("N");
            var hashes = new Dictionary<string, string>();
            foreach (
                var file in Directory.EnumerateFileSystemEntries(
                    files,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Links are not supported in Terracotta packages.");
                if (Directory.Exists(file))
                    continue;
                using var input = File.OpenRead(file);
                hashes.Add(
                    folder + "/" + Path.GetRelativePath(files, file).Replace('\\', '/'),
                    Convert.ToHexString(await StreamHash.ComputeAsync(SHA256.Create(), input, ct))
                );
            }
            var relativeExe =
                folder + "/" + Path.GetRelativePath(files, executable).Replace('\\', '/');
#if NET7_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
#endif
            ct.ThrowIfCancellationRequested();
            var destination = Store.SafePath(Root, folder);
            Directory.Move(files, destination);
            try
            {
                await Store.AtomicJson(
                    Current,
                    JsonSerializer.SerializeToNode(
                        new TerracottaInstallation(release.Version, relativeExe, hashes),
                        TerracottaJsonContext.Default.TerracottaInstallation
                    )!
                );
            }
            catch
            {
                Directory.Delete(destination, true);
                throw;
            }
            Output?.Invoke("Terracotta " + release.Version + " downloaded and verified.");
            Changed?.Invoke();
        }
        finally
        {
            if (stage != null && Directory.Exists(stage))
                Directory.Delete(stage, true);
            operations.Release();
        }
    }

    // 卸载 Terracotta：校验目录内无符号链接后递归删除组件目录。
    public async Task Remove(CancellationToken ct = default)
    {
        await operations.WaitAsync(ct);
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Connected)
                throw new IOException("Stop Terracotta before removing the component.");
            if (!Directory.Exists(Root))
                return;
            // Only the managed component directory is removed. Reject junctions
            // before recursive deletion; unrelated system applications stay intact.
            void Validate(string directory)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    _ = Store.SafePath(Root, Path.GetRelativePath(Root, entry));
                    if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                        throw new IOException(
                            "Cannot remove a component directory containing links."
                        );
                    if (Directory.Exists(entry))
                        Validate(entry);
                }
            }
            Validate(Root);
            ct.ThrowIfCancellationRequested();
            Directory.Delete(Root, true);
            State = null;
            Error = null;
            Output?.Invoke("Terracotta component removed.");
            Changed?.Invoke();
        }
        finally
        {
            operations.Release();
        }
    }

    // 校验已安装文件与记录的哈希一致，防止组件被篡改。
    async Task<TerracottaInstallation> Verify(CancellationToken ct)
    {
        var installation = Installed ?? throw new IOException("Download Terracotta first.");
        if (!installation.Files.ContainsKey(installation.Executable))
            throw new IOException("Invalid Terracotta installation.");
        foreach (var (relative, hash) in installation.Files)
        {
            using var input = File.OpenRead(Store.SafePath(Root, relative));
            if (
                !Convert
                    .ToHexString(await StreamHash.ComputeAsync(SHA256.Create(), input, ct))
                    .Equals(hash, StringComparison.OrdinalIgnoreCase)
            )
                throw new IOException(
                    "Terracotta files have changed. Download the component again."
                );
        }
        return installation;
    }

    // macOS needs the upstream application installed to receive local-network
    // permission. The native Installer owns privilege elevation and consent.
    public async Task InstallMacApplication(CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException();
        var installation = await Verify(ct);
        var package =
            installation.Files.Keys.SingleOrDefault(p =>
                p.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new IOException("The official package does not contain a macOS installer.");
        var start = new ProcessStartInfo("open") { UseShellExecute = false };
        start.ArgumentList.Add(Store.SafePath(Root, package));
        Process.Start(PlatformCommand.SystemEnvironment(start))?.Dispose();
    }

    // 启动 Terracotta 本地服务：校验安装、启动进程、等待其写出端口文件，
    // 连接本地 API 并启动后台轮询。
    public async Task Start(CancellationToken ct = default)
    {
        await operations.WaitAsync(ct);
        string? session = null;
        try
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
            if (Connected)
                return;
            var installation = await Verify(ct);
            if (OperatingSystem.IsMacOS() && !Directory.Exists("/Applications/terracotta.app"))
                throw new IOException(
                    "Install the Terracotta macOS application before starting it."
                );
            session = Store.SafePath(Root, "session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session);
            var ready = Path.Combine(session, "port.json");
            var start = new ProcessStartInfo(Store.SafePath(Root, installation.Executable))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(
                    Store.SafePath(Root, installation.Executable)
                )!,
            };
            start.ArgumentList.Add("--hmcl");
            start.ArgumentList.Add(ready);
            using var process =
                Process.Start(PlatformCommand.SystemEnvironment(start))
                ?? throw new IOException("Could not start Terracotta.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            // The Windows bootstrap exits after spawning the server, so its exit
            // is not a reliable indication of the local API's lifetime.
            while (!File.Exists(ready))
                await Task.Delay(150, timeout.Token);
            var candidate =
                JsonNode
                    .Parse(await File.ReadAllTextAsync(ready, timeout.Token))
                    ?["port"]?.GetValue<int>() ?? 0;
            if (candidate is < 1 or > 65535)
                throw new IOException("Terracotta returned an invalid local port.");
            await Connect(candidate, installation.Version, timeout.Token);
            Output?.Invoke("Terracotta local service connected.");
            _ = Poll(++connection);
        }
        finally
        {
            if (session != null && Directory.Exists(session))
                Directory.Delete(session, true);
            operations.Release();
        }
    }

    // 连接本地 API：校验端口与运行版本，刷新状态；失败时复位端口。
    internal async Task Connect(int candidate, string version, CancellationToken ct)
    {
        if (candidate is < 1 or > 65535)
            throw new IOException("Terracotta returned an invalid local port.");
        port = candidate;
        try
        {
            var meta = await Query("meta", ct);
            if (meta["version"]?.GetValue<string>().TrimStart('v') != version)
                throw new IOException(
                    "The running Terracotta version does not match the downloaded component."
                );
            index = -1;
            profileIndex = -1;
            await Refresh(ct);
        }
        catch
        {
            port = 0;
            throw;
        }
    }

    // 向本地 API 发起一次查询（串行化），返回解析后的 JSON。
    async Task<JsonNode> Query(string path, CancellationToken ct)
    {
        await requests.WaitAsync(ct);
        try
        {
            if (!Connected)
                throw new IOException("Start Terracotta first.");
            using var response = await local.GetAsync($"http://127.0.0.1:{port}/{path}", ct);
            if (!response.IsSuccessStatusCode)
                throw new IOException(
                    "Terracotta rejected the operation (HTTP " + (int)response.StatusCode + ")."
                );
            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body)
                ? new JsonObject()
                : JsonNode.Parse(body)
                    ?? throw new IOException("Terracotta returned an empty response.");
        }
        finally
        {
            requests.Release();
        }
    }

    // 刷新房间状态：只接受比当前更新的状态（按 index/profile_index 比较），
    // 避免旧响应覆盖新状态。
    public async Task Refresh(CancellationToken ct = default)
    {
        var revision = connection;
        var value = await Query("state", ct);
        var next =
            value["index"]?.GetValue<long>() ?? throw new IOException("Invalid Terracotta state.");
        var profiles = value["profile_index"]?.GetValue<long>() ?? -1;
        // Both polling and a user action can return a state; older responses
        // must never replace a newer room state.
        lock (requests)
        {
            if (!Connected || revision != connection)
                return;
            if (next < index || (next == index && profiles <= profileIndex))
                return;
            index = next;
            profileIndex = profiles;
            State = value;
            Error = null;
        }
        Changed?.Invoke();
    }

    // 校验并 URL 编码房间码/玩家名（长度 1..512，无控制字符）。
    public static string Parameter(string value)
    {
        value = value.Trim();
        if (value.Length is 0 or > 512 || value.Any(char.IsControl))
            throw new IOException("Enter a valid room code or player name.");
        return Uri.EscapeDataString(value);
    }

    public async Task CreateRoom(string player, CancellationToken ct = default) =>
        await Room("scanning?player=" + Parameter(player), ct);

    public async Task JoinRoom(string room, string player, CancellationToken ct = default) =>
        await Room("guesting?room=" + Parameter(room) + "&player=" + Parameter(player), ct);

    // 创建/加入房间：要求当前处于等待状态，执行动作后刷新。
    async Task Room(string action, CancellationToken ct)
    {
        await operations.WaitAsync(ct);
        try
        {
            await Refresh(ct);
            if (State?["state"]?.GetValue<string>() != "waiting")
                throw new IOException("Leave the current room before creating or joining another.");
            await Query("state/" + action, ct);
            await Refresh(ct);
        }
        finally
        {
            operations.Release();
        }
    }

    // 离开当前房间；连接已断开时直接复位。
    public async Task Leave(CancellationToken ct = default)
    {
        await operations.WaitAsync(ct);
        try
        {
            if (!Connected)
            {
                Disconnect();
                return;
            }
            await Query("state/ide", ct);
            await Refresh(ct);
        }
        catch (HttpRequestException)
        {
            Disconnect("Terracotta disconnected. Start it again to reconnect.");
            throw;
        }
        finally
        {
            operations.Release();
        }
    }

    // 停止本地服务（请求 panic 后断开）；连接已断开时直接复位。
    public async Task Stop(CancellationToken ct = default)
    {
        await operations.WaitAsync(ct);
        try
        {
            if (!Connected)
            {
                Disconnect();
                return;
            }
            try
            {
                await Query("panic?peaceful=true", ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
            Disconnect();
        }
        finally
        {
            operations.Release();
        }
    }

    // 断开连接并复位内部状态（端口、连接序号、状态）。
    void Disconnect(string? error = null)
    {
        lock (requests)
        {
            port = 0;
            connection++;
            index = -1;
            profileIndex = -1;
            State = null;
            Error = error;
        }
        Changed?.Invoke();
    }

    // 后台轮询房间状态：Active 时 500ms、否则 15s 一次；
    // 连续 5 次失败判定为断开。
    async Task Poll(int revision)
    {
        var failures = 0;
        try
        {
            while (!lifetime.IsCancellationRequested && Connected && revision == connection)
            {
                await wake.WaitAsync(
                    TimeSpan.FromMilliseconds(Active ? 500 : 15000),
                    lifetime.Token
                );
                if (!Connected || revision != connection)
                    break;
                try
                {
                    await Refresh(lifetime.Token);
                    failures = 0;
                }
                catch (Exception ex)
                    when (ex
                            is HttpRequestException
                                or IOException
                                or JsonException
                                or TaskCanceledException
                        && !lifetime.IsCancellationRequested
                    )
                {
                    if (revision != connection)
                        break;
                    if (++failures < 5)
                        continue;
                    Disconnect("Terracotta disconnected. Start it again to reconnect.");
                    Output?.Invoke(Error!);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        // The local service may also be used by another launcher. Disposing a
        // page or this client must not terminate its room.
        local.Dispose();
    }
}

[JsonSerializable(typeof(TerracottaInstallation))]
internal partial class TerracottaJsonContext : JsonSerializerContext { }
