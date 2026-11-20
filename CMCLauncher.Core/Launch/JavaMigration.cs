namespace CMCLauncher;

public static partial class JavaManager
{
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
        var destination = Path.Combine(parent, Path.GetFileName(source));
        RejectLinks(source);
        RejectLinks(parent);
        if (
            !Directory.Exists(parent)
            || Within(destination, source)
            || Within(source, destination)
            || Path.Exists(destination)
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

        var stage = Path.Combine(parent, ".cmcl-java-" + Guid.NewGuid().ToString("N"));
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
            Directory.Move(stage, destination);
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
        }
    }

    static bool Within(string path, string directory) =>
        PathComparer.Equals(path, directory)
        || path.StartsWith(
            Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            PathComparison
        );

    static void RejectLinks(string path)
    {
        for (string? cursor = path; cursor != null; cursor = Path.GetDirectoryName(cursor))
            if (File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked Java directories cannot be migrated.");
    }

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
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(target, File.GetUnixFileMode(item));
                }
            }
        }
    }
}
