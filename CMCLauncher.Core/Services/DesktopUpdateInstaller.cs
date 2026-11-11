using System.Diagnostics;

namespace CMCLauncher;

public static class DesktopUpdateInstaller
{
    public const string WorkerSwitch = "--apply-desktop-update";
    public static bool PackageManaged =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "cmclauncher-distribution"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE"));
    public static string ExecutableName =>
        MinecraftPlatform.OsName == "windows" ? "CMCLauncher.Avalonia.exe" : "CMCLauncher.Avalonia";
    public static string Rid =>
        (
            MinecraftPlatform.OsName == "osx" ? "osx"
            : MinecraftPlatform.OsName == "windows" ? "win"
            : "linux"
        )
        + "-"
        + (
            MinecraftPlatform.Architecture switch
            {
                "x86_64" => "x64",
                "aarch64" => "arm64",
                _ => throw new PlatformNotSupportedException(
                    "No desktop update package is available for this architecture."
                ),
            }
        );

    public static bool Preserve(string relative) =>
        relative.Split('/')[0].ToLowerInvariant()
            is "locales"
                or "themes"
                or "frpc"
                or "state"
                or "logs"
                or "instances"
                or "tools"
                or ".minecraft"
                or ".config"
        || Path.GetFileName(relative).Equals("settings.json", StringComparison.OrdinalIgnoreCase);

    public static async Task Stage(string package, string stage, CancellationToken ct)
    {
        await RuntimeArchive.Extract(
            package,
            stage,
            !package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
            1024L * 1024 * 1024,
            ct
        );
        var files = Files(stage);
        if (!files.Contains(ExecutableName) || !files.Contains("CMCLauncher.Avalonia.dll"))
            throw new IOException("The update package is missing desktop runtime files.");
    }

    static string[] Files(string root)
    {
        var files = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Update packages cannot contain links.");
                if (Directory.Exists(path))
                    queue.Enqueue(path);
                else
                    files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (files.Count > 5000)
                    throw new IOException("Too many update files.");
            }
        return files.ToArray();
    }

    public static void Start(string stage, string target)
    {
        var probe = Path.Combine(target, ".update-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, "");
        File.Delete(probe);
        using var parent = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(Path.Combine(stage, ExecutableName))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = stage,
        };
        foreach (
            var value in new[]
            {
                WorkerSwitch,
                parent.Id.ToString(),
                parent.StartTime.ToUniversalTime().Ticks.ToString(),
                Path.GetFullPath(target),
                Path.GetFullPath(stage),
            }
        )
            info.ArgumentList.Add(value);
        using var worker =
            Process.Start(info) ?? throw new IOException("Could not start the desktop updater.");
    }

    public static bool Handle(string[] args)
    {
        if (args.Length == 0 || args[0] != WorkerSwitch)
            return false;
        if (args.Length != 5)
            throw new ArgumentException("Invalid updater arguments.");
        var root = Path.GetFullPath(args[3]);
        var stage = Path.GetFullPath(args[4]);
        if (root == stage || !File.Exists(Path.Combine(stage, ExecutableName)))
            throw new IOException("Invalid update location.");
        using var parent = TryParent(int.Parse(args[1]), long.Parse(args[2]));
        if (parent != null && !parent.WaitForExit(30000))
            throw new IOException("Close CMCLauncher before updating.");
        Apply(stage, root);
        Process
            .Start(
                new ProcessStartInfo(Path.Combine(root, ExecutableName))
                {
                    UseShellExecute = false,
                    WorkingDirectory = root,
                }
            )
            ?.Dispose();
        return true;
    }

    public static void Apply(string stage, string root)
    {
        stage = Path.GetFullPath(stage);
        root = Path.GetFullPath(root);
        if (stage == root)
            throw new IOException("Invalid update location.");
        var files = Files(stage).Where(file => !Preserve(file)).ToArray();
        var backup = Path.Combine(stage, ".rollback");
        if (Directory.Exists(backup))
            throw new IOException("This update stage was already applied.");
        var changed = new List<(string Relative, bool Existed)>();
        try
        {
            foreach (var relative in files)
            {
                var destination = Store.SafePath(root, relative);
                if (
                    File.Exists(destination)
                    && File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)
                )
                    throw new IOException("Update destination contains a link.");
                for (
                    var directory = Path.GetDirectoryName(destination);
                    directory != null;
                    directory = Path.GetDirectoryName(directory)
                )
                {
                    if (
                        Directory.Exists(directory)
                        && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)
                    )
                        throw new IOException("Update destination contains a link.");
                    if (directory == root)
                        break;
                }
                var existed = File.Exists(destination);
                if (existed)
                {
                    var saved = Store.SafePath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved);
                }
                changed.Add((relative, existed));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Store.SafePath(stage, relative), destination, true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(
                        destination,
                        File.GetUnixFileMode(Store.SafePath(stage, relative))
                    );
            }
        }
        catch
        {
            foreach (var entry in changed.AsEnumerable().Reverse())
                try
                {
                    var destination = Store.SafePath(root, entry.Relative);
                    if (entry.Existed)
                        File.Copy(Store.SafePath(backup, entry.Relative), destination, true);
                    else
                        File.Delete(destination);
                }
                catch { }
            throw;
        }
    }

    static Process? TryParent(int pid, long ticks)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks == ticks)
                return process;
            process.Dispose();
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
