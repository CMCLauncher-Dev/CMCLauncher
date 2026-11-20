using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// Java 运行时管理：发现、匹配、注册、下载
// 候选路径来源：PATH 环境变量 → JAVA_HOME → 注册表 → 预置扫描目录 → 用户注册列表
public static partial class JavaManager
{
    public static string JavaExecutable =>
        MinecraftPlatform.OsName == "windows" ? "java.exe" : "java";

    internal static StringComparer PathComparer =>
        MinecraftPlatform.OsName == "windows"
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    static StringComparison PathComparison =>
        MinecraftPlatform.OsName == "windows"
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    // 判断是否为 macOS 系统自带的 Java 占位符（stub），它只是安装器入口而非真实 JDK。
    internal static bool IsMacSystemStub(string path) =>
        MinecraftPlatform.OsName == "osx"
        && (
            path is "/usr/bin/java" or "/bin/java"
            || path.StartsWith(
                "/System/Library/Frameworks/JavaVM.framework/",
                StringComparison.Ordinal
            )
        );

    // 判断候选 Java 的 CPU 架构是否与当前进程架构兼容。
    internal static bool CompatibleArchitecture(string architecture) =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => architecture is "amd64" or "x86_64",
            System.Runtime.InteropServices.Architecture.Arm64 => architecture
                is "aarch64"
                    or "arm64",
            System.Runtime.InteropServices.Architecture.X86 => architecture is "x86" or "i386",
            _ => false,
        };

    // CVE-2026-114938 (Java4Shell)：OpenJDK Base64 解码器远程代码执行漏洞。
    // 仅影响 x86-64 上启用 AVX-512 VBMI 加速的 17/21/25/27 版本线（Java 8/11 不受影响）。
    // 官方建议的临时缓解参数，跨架构通用：禁用硬件加速的 Base64 编解码 intrinsic。
    public const string Java4ShellMitigationArgument = "-XX:-UseBASE64Intrinsics";

    // 是否受 Java4Shell 影响：仅 x86-64 且版本线在 17/21/25/27 且落后于官方修复版本。
    public static bool AffectedByJava4Shell(JavaInstallation java)
    {
        if (java.Architecture is not ("amd64" or "x86_64"))
            return false;
        if (!TryJavaVersion(java.Version, out var current))
            return false;
        // Java 17 的受影响代码路径自 17.0.6 起引入；21/25/27 从首个发布即受影响。
        var affectedFrom = current.Major switch
        {
            17 => new Version(17, 0, 6),
            21 => new Version(21, 0, 0),
            25 => new Version(25, 0, 0),
            27 => new Version(27, 0, 0),
            _ => null,
        };
        var fixedVersion = current.Major switch
        {
            17 => new Version(17, 0, 21, 1),
            21 => new Version(21, 0, 13, 1),
            25 => new Version(25, 0, 5, 1),
            27 => new Version(27, 0, 1, 1),
            _ => null,
        };
        return affectedFrom != null && fixedVersion != null && current >= affectedFrom && current < fixedVersion;
    }

    // 从属性字符串中解析版本；丢弃 '+build'、'-ea' 与旧版 '_' 后缀。
    static bool TryJavaVersion(string text, out Version version)
    {
        var end = text.IndexOfAny(new[] { '+', '-', '_' });
        if (end >= 0)
            text = text[..end];
        return Version.TryParse(text, out version!);
    }

    // 根据游戏版本推断所需 Java 大版本号
    public static int RequiredMajor(string gameVersion, int declared = 0)
    {
        if (declared > 0)
            return declared;
        if (Version.TryParse(gameVersion, out var version))
            return version >= new Version(1, 20, 5) ? 21
                : version >= new Version(1, 18) ? 17
                : version >= new Version(1, 17) ? 16
                : 8;
        return 8;
    }

    // 从候选列表中匹配第一个符合版本和架构要求的 Java
    public static async Task<string> Match(Store store, int required, CancellationToken ct)
    {
        async IAsyncEnumerable<string> LaunchCandidates()
        {
            foreach (var registered in store.Config.Settings.JavaInstallations)
                yield return registered.Path;
            // 已注册的运行时若已匹配则避免重复扫描文件系统。
            foreach (
                var discovered in await Task.Run(() => Candidates(store, null, ct).ToList(), ct)
            )
                yield return discovered;
        }
        // 优先精确匹配所需大版本；没有时接受更高的兼容大版本（Java 向后兼容）。
        var seen = new HashSet<string>(PathComparer);
        string? newer = null;
        var newerMajor = int.MaxValue;
        await foreach (var path in LaunchCandidates())
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(path))
                continue;
            if (store.Config.Settings.IgnoredJavaPaths.Contains(path, PathComparer))
                continue;
            try
            {
                var java = await Inspect(path, ct);
                if (!CompatibleArchitecture(java.Architecture))
                    continue;
                if (java.Major == required)
                    return java.Path;
                if (java.Major > required && java.Major < newerMajor)
                {
                    newerMajor = java.Major;
                    newer = java.Path;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }
        if (newer != null)
            return newer;
        throw new Exception(
            $"此游戏需要 Java {required} 或更高版本，未找到匹配环境。请到设置 → Java 管理导入、扫描或下载 Java {required}。"
        );
    }

    // 标准化 Java 路径 — 展开环境变量、处理目录/javaw 等变体
    public static string Normalize(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (
            MinecraftPlatform.OsName == "osx"
            && File.Exists(Path.Combine(path, "Contents", "Home", "bin", "java"))
        )
            path = Path.Combine(path, "Contents", "Home");
        if (Directory.Exists(path))
            path = Path.Combine(
                path,
                Directory.Exists(Path.Combine(path, "bin"))
                    ? "bin/" + JavaExecutable
                    : JavaExecutable
            );
        if (Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Path.GetDirectoryName(path)!, "java.exe");
        return Path.GetFullPath(path);
    }

    // 从 java -XshowSettings 输出中解析版本、架构、供应商等属性
    public static JavaInstallation Parse(string path, string output)
    {
        string Property(string key) =>
            Regex
                .Match(output, @"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*(.+)$")
                .Groups[1]
                .Value.Trim();
        var version = Property("java.version");
        if (version.Length == 0)
            version = Regex.Match(output, "version\\s+\"([^\"]+)\"").Groups[1].Value;
        var major = Regex.Match(version, @"^(?:1\.)?(\d+)");
        if (!major.Success)
            throw new Exception("无法识别 Java 版本：" + path);
        return new JavaInstallation
        {
            Path = path,
            Version = version,
            Major = int.Parse(major.Groups[1].Value),
            Vendor = Property("java.vendor") is { Length: > 0 } vendor ? vendor : "未知提供商",
            Architecture = Property("os.arch"),
        };
    }

    // 启动 java -XshowSettings 并解析输出，返回完整 Java 安装信息
    public static async Task<JavaInstallation> Inspect(string path, CancellationToken ct)
    {
        path = Normalize(path);
        if (IsMacSystemStub(path))
            throw new IOException(
                "Select a JDK's Contents/Home/bin/java, not the macOS system Java placeholder."
            );
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到 Java 可执行文件", path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process =
            Process.Start(
                CMCLauncher.Platform.PlatformCommand.SystemEnvironment(
                    new ProcessStartInfo(path, "-XshowSettings:properties -version")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    }
                )
            ) ?? throw new Exception("无法启动 Java");
        // 同时读取两个管道，避免其中一个写满后让 Java 等不到退出。
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new Exception("Java 检查失败");
            return Parse(path, await stderr + await stdout);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(true);
        }
    }

    // 将 Java 安装信息注册到设置中（去重、从忽略列表移除）
    public static void Register(Settings settings, JavaInstallation java)
    {
        var previous = settings.JavaInstallations.FirstOrDefault(j => PathComparer.Equals(j.Path, java.Path));
        if (previous != null && java.ManagedRoot.Length == 0)
        {
            java.ManagedRoot = previous.ManagedRoot;
            java.ManagedHash = previous.ManagedHash;
        }
        settings.IgnoredJavaPaths.RemoveAll(p => string.Equals(p, java.Path, PathComparison));
        settings.JavaInstallations.RemoveAll(j => string.Equals(j.Path, java.Path, PathComparison));
        settings.JavaInstallations.Add(java);
    }

    // 从注册列表移除 Java，加入忽略列表避免重新扫描回来
    public static void Remove(Settings settings, JavaInstallation java)
    {
        settings.JavaInstallations.RemoveAll(j => string.Equals(j.Path, java.Path, PathComparison));
        if (!settings.IgnoredJavaPaths.Contains(java.Path, PathComparer))
            settings.IgnoredJavaPaths.Add(java.Path);
        if (string.Equals(settings.JavaPath, java.Path, PathComparison))
            settings.JavaPath = "";
    }

    // 扫描本机所有可能存在的 Java 路径：PATH → JAVA_HOME → 注册表 → 预置目录
    public static IEnumerable<string> Candidates(Store store, string? folder, CancellationToken ct)
    {
        var found = new HashSet<string>(PathComparer);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var full = Normalize(path);
                if (!IsMacSystemStub(full) && File.Exists(full))
                    found.Add(full);
            }
            catch { }
        }
        foreach (
            var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(
                Path.PathSeparator
            )
        )
            if (!string.IsNullOrWhiteSpace(entry))
                Add(Path.Combine(entry.Trim().Trim('"'), JavaExecutable));
        Add(Environment.GetEnvironmentVariable("JAVA_HOME"));
        Add(store.Config.Settings.JavaPath);
        foreach (var java in store.Config.Settings.JavaInstallations)
            Add(java.Path);
        foreach (var path in LauncherPlatform.Current.JavaHomes())
            Add(path);
        var roots = new List<string>();
        if (folder != null)
            roots.Add(folder);
        else
        {
            roots.Add(Path.Combine(store.Root, "runtimes"));
            roots.Add(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".jdks"
                )
            );
            var data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            roots.Add(
                MinecraftPlatform.OsName switch
                {
                    "osx" => Path.Combine(data, "minecraft", "runtime"),
                    "windows" => Path.Combine(data, ".minecraft", "runtime"),
                    _ => Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".minecraft",
                        "runtime"
                    ),
                }
            );
            if (MinecraftPlatform.OsName == "windows")
            {
                var programFiles = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                );
                foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu" })
                    if (!string.IsNullOrWhiteSpace(programFiles))
                        roots.Add(Path.Combine(programFiles, vendor));
            }
        }
        foreach (var root in roots)
        {
            // 限制深度和访问数量，也跳过联接，避免扫描目录无限扩散。
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            int visited = 0;
            while (queue.Count > 0 && visited++ < 20000)
            {
                ct.ThrowIfCancellationRequested();
                var (path, depth) = queue.Dequeue();
                try
                {
                    if (
                        !Directory.Exists(path)
                        || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                    )
                        continue;
                    Add(path);
                    if (depth < 7)
                        foreach (var child in Directory.EnumerateDirectories(path))
                            queue.Enqueue((child, depth + 1));
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        return found;
    }

    // Java 8 JDK 归档同时包含 bin/java 与 jre/bin/java，优先选择外层 JDK，
    // 它还提供加载器安装所需的编译器与工具。
    internal static string? DownloadedJavaExecutable(string root) =>
        Directory
            .EnumerateFiles(root, JavaExecutable, SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin")
            .OrderBy(path =>
                Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Length
            )
            .ThenBy(path => path, PathComparer)
            .FirstOrDefault();

    // 从 Adoptium API 下载指定大版本的 Temurin JDK 并安装到 runtimes 目录
    public static Task<JavaInstallation> Download(
        LauncherCore core,
        Store store,
        int major,
        CancellationToken ct,
        Action<string> stage,
        string brand = "temurin",
        string? installParent = null
    ) => Install(core, store, major, ct, stage, brand, installParent, null);

    static async Task<JavaInstallation> Install(
        LauncherCore core,
        Store store,
        int major,
        CancellationToken ct,
        Action<string> stage,
        string brand,
        string? installParent,
        JavaInstallation? current
    )
    {
        var os = MinecraftPlatform.OsName == "osx" ? "mac" : MinecraftPlatform.OsName;
        var arch = MinecraftPlatform.Architecture switch
        {
            "x86_64" => "x64",
            "aarch64" => "aarch64",
            _ => throw new PlatformNotSupportedException("Unsupported Java architecture."),
        };
        var gzip = os != "windows";
        stage($"Java {major} · {brand}");
        string hash;
        string url;
        var algorithm = "sha256";
        if (brand == "corretto" && os == "windows" && arch == "x64")
        {
            var file = $"amazon-corretto-{major}-x64-windows-jdk.zip";
            url = $"https://corretto.aws/downloads/latest/{file}";
            hash = (await core.Http.GetStringAsync(
                $"https://corretto.aws/downloads/latest_sha256/{file}", ct
            )).Trim();
        }
        else if (brand == "temurin")
        {
            var assets = await core.Json(
                $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={arch}&image_type=jdk&os={os}&vendor=eclipse",
                ct
            );
            var package = assets.Items().FirstOrDefault()?["binary"]?["package"]
                ?? throw new IOException("没有适用的 Java 下载包");
            hash = package.S("checksum");
            url = package.S("link");
        }
        else
            (url, hash, _, algorithm) = await ResolveAdditionalJava(core, major, brand, ct);
        if (!Regex.IsMatch(hash, algorithm == "sha1" ? "^[a-fA-F0-9]{40}$" : "^[a-fA-F0-9]{64}$")
            || !Uri.TryCreate(url, UriKind.Absolute, out var downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new Exception("Java 下载包缺少有效校验值");
        hash = hash.ToLowerInvariant();
        var original = current == null ? null : ManagedLocation(store, current)?.Directory
            ?? throw new InvalidOperationException("This Java installation is not managed by CMCLauncher.");
        if (current != null && (PathComparer.Equals(current.ManagedHash, hash)
            || (current.ManagedHash.Length == 0
                && PathComparer.Equals(Path.GetFileName(original), $"{brand}-{major}-{hash[..12]}"))))
            return current;
        var root = original == null
            ? Path.GetFullPath(installParent ?? Path.Combine(store.Root, "runtimes"))
            : Path.GetDirectoryName(original)!;
        Directory.CreateDirectory(root);
        var destination = original ?? Path.Combine(root, $"{brand}-{major}-{hash[..12]}");
        if (original == null && Directory.Exists(destination))
        {
            if (installParent != null)
                throw new IOException("Java installation folder already exists: " + destination);
            var existing = DownloadedJavaExecutable(destination);
            if (existing != null)
            {
                var detected = await Inspect(existing, ct);
                detected.ManagedRoot = destination;
                detected.ManagedHash = hash;
                return detected;
            }
            throw new Exception("Java 目标目录已存在但不完整，请检查：" + destination);
        }
        // 解压和版本检查都在临时目录完成，通过后才改名为正式安装目录。
        var archive = Path.Combine(root, Guid.NewGuid() + (gzip ? ".tar.gz" : ".zip"));
        var staging = Path.Combine(root, ".install-" + Guid.NewGuid());
        try
        {
            stage($"下载 Java {major} · {brand}");
            await core.Download(
                url,
                archive,
                hash,
                algorithm,
                ct: ct
            );
            stage("解压并检查 Java");
            await RuntimeArchive.Extract(archive, staging, gzip, 2L * 1024 * 1024 * 1024, ct);
            var executable =
                DownloadedJavaExecutable(staging)
                ?? throw new IOException(
                    "The Java archive does not contain a Java executable."
                );
            var inspected = await Inspect(executable, ct);
            if (inspected.Major != major || !CompatibleArchitecture(inspected.Architecture))
                throw new Exception("下载的 Java 版本不匹配");
            if (current != null)
            {
                var previous = ReleaseVersion(current.Version);
                var next = ReleaseVersion(inspected.Version);
                if (previous == null || next == null || next < previous)
                    return current;
            }
            ct.ThrowIfCancellationRequested();
            if (original == null)
                Directory.Move(staging, destination);
            else
            {
                var backup = Path.Combine(root, ".java-backup-" + Guid.NewGuid());
                Directory.Move(destination, backup);
                try
                {
                    Directory.Move(staging, destination);
                }
                catch
                {
                    Directory.Move(backup, destination);
                    throw;
                }
                try
                {
                    Directory.Delete(backup, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    stage("旧 Java 文件未能清理：" + backup);
                }
            }
            inspected.Path = Path.Combine(destination, Path.GetRelativePath(staging, executable));
            inspected.ManagedRoot = destination;
            inspected.ManagedHash = hash;
            return inspected;
        }
        finally
        {
            if (File.Exists(archive))
                File.Delete(archive);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    // 返回由 CMCLauncher 管理的 Java 的品牌（temurin/corretto 等），非托管安装返回 null。
    public static string? ManagedBrand(Store store, JavaInstallation java) =>
        ManagedLocation(store, java)?.Brand;

    // 解析托管安装目录：路径必须位于 runtimes 下、目录名符合 brand-major-hash12 格式、
    // 且品牌受支持，否则视为非托管安装。
    static (string Brand, string Directory)? ManagedLocation(Store store, JavaInstallation java)
    {
        if (!Path.IsPathFullyQualified(java.Path))
            return null;
        var root = Path.GetFullPath(Path.Combine(store.Root, "runtimes"));
        var relative = Path.GetRelativePath(root, java.Path);
        var destination = Path.IsPathFullyQualified(java.ManagedRoot)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(java.ManagedRoot))
            : Path.Combine(root, relative.Split(Path.DirectorySeparatorChar)[0]);
        var folder = Path.GetFileName(destination);
        var match = Regex.Match(folder, @"^([a-z]+)-([0-9]+)-[0-9a-fA-F]{12}$");
        return match.Success
            && Path.GetFullPath(java.Path).StartsWith(destination + Path.DirectorySeparatorChar, PathComparison)
            && Directory.Exists(destination)
            && !File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)
            && match.Groups[2].Value == java.Major.ToString()
            && DownloadBrands(java.Major).Any(brand => brand.Id == match.Groups[1].Value)
            ? (match.Groups[1].Value, destination) : null;
    }

    // 将发行版版本字符串（如 17.0.12_8）解析为可比较的 Version。
    static Version? ReleaseVersion(string value) =>
        TryJavaVersion(value.Replace('_', '.'), out var parsed) ? parsed : null;

    // 根据供应商名称推断可自动更新的下载品牌；不支持的品牌返回 null。
    public static string? UpdateBrand(Store store, JavaInstallation java)
    {
        if (ManagedBrand(store, java) is { } managed)
            return managed;
        if (!CompatibleArchitecture(java.Architecture))
            return null;
        var brand = java.Vendor.Trim().ToLowerInvariant() switch
        {
            "eclipse adoptium" or "adoptopenjdk" => "temurin",
            "amazon.com inc." or "amazon.com, inc." => "corretto",
            "microsoft" or "microsoft corporation" => "microsoft",
            "azul systems, inc." or "azul systems inc." or "azul systems" => "zulu",
            "bellsoft" or "bellsoft ltd." => "liberica",
            "sap se" or "sap" => "sapmachine",
            _ => null,
        };
        return DownloadBrands(java.Major).Any(option => option.Id == brand) ? brand : null;
    }

    // 将现有 Java 安装更新到同大版本的最新发行版：
    // 下载并校验新版本，仅当版本确实更新时才替换安装并更新设置。
    public static async Task<bool> UpdateJava(
        LauncherCore core, Store store, JavaInstallation current, CancellationToken ct,
        Action<string> stage
    )
    {
        var managed = ManagedBrand(store, current) != null;
        var brand = UpdateBrand(store, current)
            ?? throw new InvalidOperationException("This Java distribution does not support automatic updates.");
        var latest = await Install(core, store, current.Major, ct, stage, brand, null, managed ? current : null);
        if (ReferenceEquals(current, latest))
            return false;
        if (!managed)
        {
            var previous = ReleaseVersion(current.Version);
            var next = ReleaseVersion(latest.Version);
            if (previous == null || next == null || next <= previous)
                return false;
            ct.ThrowIfCancellationRequested();
        }
        var settings = store.Config.Settings;
        var wasSelected = PathComparer.Equals(settings.JavaPath, current.Path);
        if (!PathComparer.Equals(current.Path, latest.Path))
            Remove(settings, current);
        Register(settings, latest);
        if (wasSelected)
            settings.JavaPath = latest.Path;
        await store.Save();
        return true;
    }
}
