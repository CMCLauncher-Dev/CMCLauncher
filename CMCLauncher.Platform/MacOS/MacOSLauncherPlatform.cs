using System.Text;
using CMCLauncher.Platform;

namespace CMCLauncher;

public sealed class MacOSLauncherPlatform : LauncherPlatform
{
    public static void Initialize() => Current = new MacOSLauncherPlatform();

    public override string MinecraftOsName => "osx";

    public override Task<double?> CpuUsage(CancellationToken ct) =>
        Task.Run<double?>(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                var output = PlatformCommand.Execute("/usr/bin/top", ["-l", "1", "-n", "0"]);
                var idle = System.Text.RegularExpressions.Regex.Match(output, @"([\d.]+)% idle");
                return idle.Success
                    ? Math.Clamp(
                        100
                            - double.Parse(
                                idle.Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                        0,
                        100
                    )
                    : null;
            },
            ct
        );

    public override byte[] TransformCredential(byte[] data, bool protect)
    {
        if (protect)
        {
            var id = Guid.NewGuid().ToString("N");
            PlatformCommand.Execute(
                "/usr/bin/security",
                ["-i"],
                "add-generic-password -a "
                    + id
                    + " -s com.cmclauncher.credentials -w "
                    + Convert.ToBase64String(data)
                    + "\nquit\n"
            );
            var stored = PlatformCommand.Execute(
                "/usr/bin/security",
                ["find-generic-password", "-a", id, "-s", "com.cmclauncher.credentials", "-w"]
            );
            if (stored != Convert.ToBase64String(data))
                throw new IOException("The system keychain did not save the credential.");
            return Encoding.UTF8.GetBytes("keychain:" + id);
        }
        var value = Encoding.UTF8.GetString(data);
        if (!value.StartsWith("keychain:") || !Guid.TryParseExact(value[9..], "N", out _))
            throw new IOException("These credentials belong to another platform. Sign in again.");
        return Convert.FromBase64String(
            PlatformCommand.Execute(
                "/usr/bin/security",
                [
                    "find-generic-password",
                    "-a",
                    value[9..],
                    "-s",
                    "com.cmclauncher.credentials",
                    "-w",
                ]
            )
        );
    }

    public override (long TotalMb, long AvailableMb)? PhysicalMemory()
    {
        try
        {
            var total =
                long.Parse(PlatformCommand.Execute("/usr/sbin/sysctl", ["-n", "hw.memsize"]))
                / 1048576;
            var stats = PlatformCommand.Execute("/usr/bin/vm_stat", []);
            var pageSize = System.Text.RegularExpressions.Regex.Match(
                stats,
                @"page size of (\d+) bytes"
            );
            if (!pageSize.Success)
                return null;
            long pages = 0;
            foreach (var name in new[] { "Pages free", "Pages inactive", "Pages speculative" })
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    stats,
                    System.Text.RegularExpressions.Regex.Escape(name) + @":\s*(\d+)"
                );
                if (match.Success)
                    pages += long.Parse(match.Groups[1].Value);
            }
            return (total, Math.Min(total, pages * long.Parse(pageSize.Groups[1].Value) / 1048576));
        }
        catch
        {
            return null;
        }
    }

    public override IEnumerable<string> JavaHomes() =>
        DiscoverJavaHomes(
            [
                "/Library/Java/JavaVirtualMachines",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Java/JavaVirtualMachines"
                ),
            ],
            ["/opt/homebrew/opt", "/usr/local/opt"]
        );

    internal static IEnumerable<string> DiscoverJavaHomes(
        IEnumerable<string> bundleRoots,
        IEnumerable<string> brewRoots
    )
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Add(string home)
        {
            if (File.Exists(Path.Combine(home, "bin", "java")))
                found.Add(home);
        }
        foreach (
            var (root, brew) in bundleRoots
                .Select(p => (p, false))
                .Concat(brewRoots.Select(p => (p, true)))
        )
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var jdk in Directory.EnumerateDirectories(root, brew ? "openjdk*" : "*"))
                    Add(
                        brew
                            ? Path.Combine(jdk, "libexec", "openjdk.jdk", "Contents", "Home")
                            : Path.Combine(jdk, "Contents", "Home")
                    );
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    public override Task MoveToTrash(string path) =>
        Task.Run(() =>
            PlatformCommand.Execute(
                "/usr/bin/osascript",
                [
                    "-e",
                    "on run argv\n tell application \"Finder\" to delete POSIX file (item 1 of argv)\nend run",
                    Path.GetFullPath(path),
                ]
            )
        );
}
