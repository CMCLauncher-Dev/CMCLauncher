namespace CMCLauncher;

// JavaManager 的部分类：负责 Java 安装目录的识别、迁移与删除前的安全校验。
public static partial class JavaManager
{
    // 识别可安全删除的 Java 安装目录：
    // 校验可执行文件位于 <home>/bin 下、存在版本标记文件（release 或 lib/rt.jar），
    // 且目录不包含其他已注册安装或启动器数据，否则抛出异常。
    public static string JavaDirectoryForDeletion(Store store, JavaInstallation java)
    {
        if (!Path.IsPathFullyQualified(java.Path) || !File.Exists(java.Path))
            throw new IOException("Cannot identify the Java installation folder.");
        var executable = Path.GetFullPath(java.Path);
        var bin = Path.GetDirectoryName(executable);
        var home = Path.GetDirectoryName(bin);
        if (
            home == null
            || !PathComparer.Equals(Path.GetFileName(bin), "bin")
            || !PathComparer.Equals(Path.GetFileName(executable), JavaExecutable)
            || !Directory.Exists(Path.Combine(home, "lib"))
        )
            throw new IOException("Cannot identify the Java installation folder.");

        var marker = Path.Combine(home, "release");
        if (!File.Exists(marker) && java.Major is > 0 and <= 8)
            marker = Path.Combine(home, "lib", "rt.jar");
        if (!File.Exists(marker))
            throw new IOException("Cannot identify the Java installation folder.");
        RejectLinks(executable);
        RejectLinks(marker);
        RejectLinks(Path.Combine(home, "lib"));
        var directory = ManagedLocation(store, java)?.Directory ?? home;
        var protectedPaths = store
            .Config.Instances.SelectMany(instance =>
                new[] { store.GameDirectory(instance), store.StorageDirectory(instance) }
            )
            .Append(store.Root)
            .Append(store.MinecraftRoot);
        if (
            PathComparer.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(directory)!)
            )
            || protectedPaths.Any(path => Within(Path.GetFullPath(path), directory))
            || store.Config.Settings.JavaInstallations.Any(other =>
                !ReferenceEquals(other, java)
                && Path.IsPathFullyQualified(other.Path)
                && Within(Path.GetFullPath(other.Path), directory)
            )
        )
            throw new IOException(
                "The folder also contains another registered installation or launcher data."
            );
        return directory;
    }

    // 将 Java 安装迁移到目标父目录：
    // 先复制到临时暂存目录并校验版本一致性，再原子性地移动到目标位置，
    // 最后更新配置中的路径并（对托管安装）删除旧目录。
    // 返回 true 表示旧目录已被删除，false 表示保留（未托管或删除失败）。
    public static async Task<bool> MigrateJava(
        Store store,
        JavaInstallation java,
        string targetParent,
        CancellationToken ct
    )
    {
        var managed = ManagedLocation(store, java);
        var executable = Path.GetFullPath(java.Path);
        var bin = Path.GetDirectoryName(executable);
        var source =
            managed?.Directory
            ?? (
                PathComparer.Equals(Path.GetFileName(bin), "bin")
                    ? Path.GetDirectoryName(bin)
                    : null
            );
        if (
            source == null
            || !File.Exists(executable)
            || (managed == null && !Directory.Exists(Path.Combine(source, "lib")))
        )
            throw new IOException("Cannot identify the Java installation folder.");

        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetParent));
        var sourceName = Path.GetFileName(source);
        var useSelectedDirectory = PathComparer.Equals(Path.GetFileName(parent), sourceName);
        var destination = useSelectedDirectory ? parent : Path.Combine(parent, sourceName);
        var stageParent = Path.GetDirectoryName(destination)
            ?? throw new IOException("Choose a separate Java installation folder.");
        RejectLinks(source);
        RejectLinks(parent);
        if (
            !Directory.Exists(parent)
            || Within(destination, source)
            || Within(source, destination)
            || (useSelectedDirectory
                ? Directory.EnumerateFileSystemEntries(destination).Any()
                : (File.Exists(destination) || Directory.Exists(destination)))
        )
            throw new IOException(
                "Choose a separate folder without an installation of the same name."
            );

        var settings = store.Config.Settings;
        if (
            managed != null
            && settings.JavaInstallations.Any(other =>
                !ReferenceEquals(other, java) && Within(Path.GetFullPath(other.Path), source)
            )
        )
            throw new IOException("The Java folder contains another registered installation.");

        var stage = Path.Combine(stageParent, ".cmcl-java-" + Guid.NewGuid().ToString("N"));
        var published = false;
        var committed = false;
        try
        {
            Directory.CreateDirectory(stage);
            await Task.Run(() => CopyJavaTree(source, stage, ct), ct);
            var relativeExecutable = Path.GetRelativePath(source, executable);
            var inspected = await Inspect(Store.SafePath(stage, relativeExecutable), ct);
            if (
                inspected.Major != java.Major
                || inspected.Version != java.Version
                || inspected.Architecture != java.Architecture
            )
                throw new IOException("The migrated Java does not match the original.");
            ct.ThrowIfCancellationRequested();
            if (useSelectedDirectory)
            {
                RejectLinks(destination);
                Directory.Delete(destination, recursive: false);
            }
            try
            {
                Directory.Move(stage, destination);
            }
            catch
            {
                if (useSelectedDirectory && !(File.Exists(destination) || Directory.Exists(destination)))
                    Directory.CreateDirectory(destination);
                throw;
            }
            published = true;
            var previousPath = java.Path;
            var previousRoot = java.ManagedRoot;
            var previousDefault = settings.JavaPath;
            java.Path = Store.SafePath(destination, relativeExecutable);
            if (managed != null)
                java.ManagedRoot = destination;
            if (PathComparer.Equals(previousDefault, previousPath))
                settings.JavaPath = java.Path;
            try
            {
                await store.Save();
                committed = true;
            }
            catch
            {
                java.Path = previousPath;
                java.ManagedRoot = previousRoot;
                settings.JavaPath = previousDefault;
                throw;
            }
            if (managed == null)
                return false;
            try
            {
                RejectLinks(source);
                Directory.Delete(source, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        finally
        {
            var cleanup = published ? destination : stage;
            if (!committed && Directory.Exists(cleanup))
                try
                {
                    Directory.Delete(cleanup, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            if (!committed && published && useSelectedDirectory && !(File.Exists(destination) || Directory.Exists(destination)))
                Directory.CreateDirectory(destination);
        }
    }

    // 判断 path 是否等于或位于 directory 目录之内。
    static bool Within(string path, string directory) =>
        PathComparer.Equals(path, directory)
        || path.StartsWith(
            Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            PathComparison
        );

    // 沿路径向上检查是否存在符号链接/重解析点，发现则拒绝迁移。
    static void RejectLinks(string path)
    {
        for (string? cursor = path; cursor != null; cursor = Path.GetDirectoryName(cursor))
            if (File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked Java directories cannot be migrated.");
    }

    // 递归复制 Java 目录树到暂存目录（拒绝符号链接，保留 Unix 文件权限）。
    static async Task CopyJavaTree(string source, string stage, CancellationToken ct)
    {
        var directories = new Stack<string>();
        directories.Push(source);
        while (directories.TryPop(out var directory))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(item);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Linked Java files cannot be migrated.");
                var target = Store.SafePath(stage, Path.GetRelativePath(source, item));
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.CreateDirectory(target);
                    directories.Push(item);
                }
                else
                {
                    await using var input = File.OpenRead(item);
                    await using var output = File.Open(
                        target,
                        FileMode.CreateNew,
                        FileAccess.Write
                    );
                    await input.CopyToAsync(output, ct);
#if NET7_0_OR_GREATER
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(target, File.GetUnixFileMode(item));
#endif
                }
            }
        }
    }
}
