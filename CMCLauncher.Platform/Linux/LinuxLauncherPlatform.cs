using System.Text;
using CMCLauncher.Platform;

namespace CMCLauncher;

public sealed class LinuxLauncherPlatform : LauncherPlatform
{
    public static void Initialize() => Current = new LinuxLauncherPlatform();

    public override string MinecraftOsName => "linux";
    ulong lastIdle,
        lastTotal;

    public override async Task<double?> CpuUsage(CancellationToken ct)
    {
        var line = (await File.ReadAllLinesAsync("/proc/stat", ct)).First(l =>
            l.StartsWith("cpu ")
        );
        var ticks = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(ulong.Parse)
            .ToArray();
        var total = ticks.Take(8).Aggregate(0UL, (a, b) => a + b);
        var idle = ticks[3] + ticks.ElementAtOrDefault(4);
        double? value =
            lastTotal == 0 || total <= lastTotal
                ? null
                : Math.Clamp(100.0 * (1 - (double)(idle - lastIdle) / (total - lastTotal)), 0, 100);
        lastIdle = idle;
        lastTotal = total;
        return value;
    }

    public override Task<IReadOnlyList<GraphicsUsage>> Graphics(
        string cachePath,
        CancellationToken ct
    ) =>
        Task.Run<IReadOnlyList<GraphicsUsage>>(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return PlatformCommand
                        .Execute(
                            "nvidia-smi",
                            ["--query-gpu=name,utilization.gpu", "--format=csv,noheader,nounits"]
                        )
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line =>
                        {
                            var separator = line.LastIndexOf(',');
                            return new GraphicsUsage(
                                line[..separator].Trim(),
                                double.Parse(
                                    line[(separator + 1)..].Trim(),
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                            );
                        })
                        .ToArray();
                }
                catch
                {
                    return [];
                }
            },
            ct
        );

    public override byte[] TransformCredential(byte[] data, bool protect)
    {
        if (protect)
        {
            var id = Guid.NewGuid().ToString("N");
            PlatformCommand.Execute(
                "secret-tool",
                ["store", "--label=CMCLauncher", "application", "CMCLauncher", "id", id],
                Convert.ToBase64String(data)
            );
            return Encoding.UTF8.GetBytes("secret-service:" + id);
        }
        var value = Encoding.UTF8.GetString(data);
        if (!value.StartsWith("secret-service:") || !Guid.TryParseExact(value[15..], "N", out _))
            throw new IOException("These credentials belong to another platform. Sign in again.");
        return Convert.FromBase64String(
            PlatformCommand.Execute(
                "secret-tool",
                ["lookup", "application", "CMCLauncher", "id", value[15..]]
            )
        );
    }

    public override (long TotalMb, long AvailableMb)? PhysicalMemory()
    {
        try
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(l => l.Split(':', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => p[0],
                    p =>
                        long.Parse(p[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
                        / 1024
                );
            return (
                values["MemTotal"],
                values.GetValueOrDefault("MemAvailable", values["MemFree"])
            );
        }
        catch
        {
            return null;
        }
    }

    public override IEnumerable<string> JavaHomes() =>
        new[]
        {
            "/usr/lib/jvm",
            "/usr/java",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"),
        }
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateDirectories(p));

    public override Task MoveToTrash(string path) =>
        Task.Run(() => PlatformCommand.Execute("gio", ["trash", Path.GetFullPath(path)]));
}
