using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// Java 运行时管理：发现、匹配、注册、下载
// 候选路径来源：PATH 环境变量 → JAVA_HOME → 注册表 → 预置扫描目录 → 用户注册列表
public static class JavaManager
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

    internal static bool IsMacSystemStub(string path) =>
        MinecraftPlatform.OsName == "osx"
        && (
            path is "/usr/bin/java" or "/bin/java"
            || path.StartsWith(
                "/System/Library/Frameworks/JavaVM.framework/",
                StringComparison.Ordinal
            )
        );

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
            // Avoid rescanning the filesystem when a registered runtime already matches.
            foreach (
                var discovered in await Task.Run(() => Candidates(store, null, ct).ToList(), ct)
            )
                yield return discovered;
        }
        var seen = new HashSet<string>(PathComparer);
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
                if (java.Major == required && CompatibleArchitecture(java.Architecture))
                    return java.Path;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }
        throw new Exception(
            $"此游戏需要 Java {required}，未找到匹配环境。请到设置 → Java 管理导入、扫描或下载 Java {required}。"
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

    // Java 8 JDK archives contain both bin/java and jre/bin/java. Prefer the outer JDK,
    // which also supplies the compiler and tools required by loader installers.
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
    public static async Task<JavaInstallation> Download(
        LauncherCore core,
        Store store,
        int major,
        CancellationToken ct,
        Action<string> stage
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
        stage("获取 Temurin Java " + major + " 下载信息");
        var assets = await core.Json(
            $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={arch}&image_type=jdk&os={os}&vendor=eclipse",
            ct
        );
        var package =
            assets.Items().FirstOrDefault()?["binary"]?["package"] ?? throw new Exception(
                "没有适用的 Java 下载包"
            );
        var hash = package.S("checksum");
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
            throw new Exception("Java 下载包缺少有效校验值");
        var root = Path.Combine(store.Root, "runtimes");
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, $"temurin-{major}-{hash[..12]}");
        if (Directory.Exists(destination))
        {
            var existing = DownloadedJavaExecutable(destination);
            if (existing != null)
                return await Inspect(existing, ct);
            throw new Exception("Java 目标目录已存在但不完整，请检查：" + destination);
        }
        // 解压和版本检查都在临时目录完成，通过后才改名为正式安装目录。
        var archive = Path.Combine(root, Guid.NewGuid() + (gzip ? ".tar.gz" : ".zip"));
        var staging = Path.Combine(root, ".install-" + Guid.NewGuid());
        try
        {
            stage("下载 Temurin Java " + major);
            await core.Download(
                package.S("link"),
                archive,
                hash,
                "sha256",
                long.TryParse(package.S("size"), out var size) ? size : null,
                ct
            );
            stage("解压并检查 Java");
            await RuntimeArchive.Extract(archive, staging, gzip, 2L * 1024 * 1024 * 1024, ct);
            var executable =
                DownloadedJavaExecutable(staging)
                ?? throw new IOException(
                    "The Java archive does not contain a Java executable."
                );
            var inspected = await Inspect(executable, ct);
            if (inspected.Major != major)
                throw new Exception("下载的 Java 版本不匹配");
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            inspected.Path = Path.Combine(destination, Path.GetRelativePath(staging, executable));
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
}
