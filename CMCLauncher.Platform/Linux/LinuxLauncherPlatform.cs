using System.Diagnostics;
using System.ComponentModel;
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
                var adapters = new List<GraphicsUsage>();
                try
                {
                    adapters.AddRange(
                        PlatformCommand
                            .Execute(
                                "nvidia-smi",
                                [
                                    "--query-gpu=name,utilization.gpu",
                                    "--format=csv,noheader,nounits",
                                ]
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
                            .ToArray()
                    );
                }
                catch (Exception) when (!ct.IsCancellationRequested) { }
                adapters.AddRange(LinuxGraphics.ReadDrm("/sys/class/drm", adapters.Count == 0, ct));
                return adapters;
            },
            ct
        );

    public override byte[] TransformCredential(byte[] data, bool protect) =>
        TransformCredential(
            data,
            protect,
            (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
                .Split(':').Contains("KDE", StringComparer.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("KDE_FULL_SESSION"), "true", StringComparison.OrdinalIgnoreCase),
            RunSecretTool,
            RunKWallet
        );

    internal static byte[] TransformCredential(
        byte[] data,
        bool protect,
        bool kdeSession,
        Func<string[], string?, string> secretTool,
        Func<string[], string?, string> kwallet
    )
    {
        const string secretServicePrefix = "secret-service:";
        const string kwalletPrefix = "kwallet:";
        string Wallet(string action, string id, string? input = null) =>
            kwallet([action, id, "--folder", "CMCLauncher", "kdewallet"], input);

        if (protect)
        {
            var id = Guid.NewGuid().ToString("N");
            var encoded = Convert.ToBase64String(data);
            try
            {
                secretTool(["store", "--label=CMCLauncher", "application", "CMCLauncher", "id", id], encoded);
                return Encoding.UTF8.GetBytes(secretServicePrefix + id);
            }
            catch (IOException secretServiceError) when (kdeSession)
            {
                try
                {
                    Wallet("--write-password", id, encoded);
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
                {
                    // Preserve the original Secret Service diagnosis if KWallet's helper is absent.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(secretServiceError);
                }
                if (!string.Equals(Wallet("--read-password", id), encoded, StringComparison.Ordinal))
                    throw new IOException("Could not verify the login saved in KDE Wallet.");
                return Encoding.UTF8.GetBytes(kwalletPrefix + id);
            }
        }

        var value = Encoding.UTF8.GetString(data);
        var inKWallet = value.StartsWith(kwalletPrefix, StringComparison.Ordinal);
        var prefix = inKWallet ? kwalletPrefix : secretServicePrefix;
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(value[prefix.Length..], "N", out _))
            throw new IOException("These credentials belong to another platform. Sign in again.");
        var reference = value[prefix.Length..];
        string stored;
        try
        {
            stored = inKWallet
                ? Wallet("--read-password", reference)
                : secretTool(["lookup", "application", "CMCLauncher", "id", reference], null);
        }
        catch (Win32Exception ex) when (inKWallet && ex.NativeErrorCode == 2)
        {
            throw new IOException("kwallet-query is missing. Install the KDE Wallet command-line tools.");
        }
        if (stored.Length == 0)
            throw new IOException("The saved Linux credential is missing. Sign in again.");
        try
        {
            return Convert.FromBase64String(stored);
        }
        catch (FormatException)
        {
            throw new IOException("The saved Linux credential is invalid. Sign in again.");
        }
    }

    static string RunKWallet(string[] arguments, string? input)
    {
        try
        {
            return PlatformCommand.Execute("kwallet-query", arguments, input,
                _ => "KDE Wallet could not save or read the login. Check that kdewallet is available and unlocked.");
        }
        catch (TimeoutException)
        {
            throw new IOException("Access to KDE Wallet timed out.");
        }
    }

    static string RunSecretTool(string[] arguments, string? input)
    {
        var writing = input != null;
        try
        {
            return PlatformCommand.Execute("secret-tool", arguments, input,
                diagnostic => SecretToolFailure(diagnostic, writing));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new IOException("secret-tool is missing. Install the Linux libsecret command-line tools.");
        }
        catch (TimeoutException)
        {
            throw new IOException(writing
                ? "Saving the login to the Linux keyring timed out."
                : "Reading the login from the Linux keyring timed out.");
        }
    }

    internal static string SecretToolFailure(string diagnostic, bool writing)
    {
        bool Has(string message) => diagnostic.Contains(message, StringComparison.OrdinalIgnoreCase);

        // Only emit fixed messages. A provider can include sensitive data in stderr.
        if (Has("ServiceUnknown") || Has("was not provided by any .service files")
            || Has("Cannot autolaunch D-Bus") || Has("could not connect")
            || Has("couldn't connect") || Has("failed to connect") || Has("connection refused"))
            return "The Linux Secret Service is unavailable in this desktop session.";
        if (Has("IsLocked") || Has("is locked") || Has("collection locked") || Has("unlock"))
            return "The Linux keyring could not be unlocked.";
        if (Has("No such secret collection") || Has("default collection")
            || Has("default alias") || Has("No such object path"))
            return "The Linux Secret Service could not find the default keyring.";
        if (!writing && string.IsNullOrWhiteSpace(diagnostic))
            return "The saved Linux credential is missing. Sign in again.";
        return writing
            ? "Could not save the login to the Linux keyring."
            : "Could not read the login from the Linux keyring.";
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

    public override Task<bool> OpenDirectory(string path) => Task.Run(() =>
    {
        var directory = Path.GetFullPath(path);
        var uri = new Uri(directory).AbsoluteUri.Replace("'", "%27", StringComparison.Ordinal);
        try
        {
            PlatformCommand.Execute("gdbus",
                ["call", "--session", "--timeout", "5", "--dest", "org.freedesktop.FileManager1",
                 "--object-path", "/org/freedesktop/FileManager1",
                 "--method", "org.freedesktop.FileManager1.ShowFolders", $"['{uri}']", "''"]);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or TimeoutException)
        {
            // Call a file manager directly when the desktop does not export FileManager1.
            // A generic URI opener can route a misconfigured directory association to this launcher.
        }

        var kde = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
            .Split(':').Contains("KDE", StringComparer.OrdinalIgnoreCase);
        var managers = kde
            ? new[] { "dolphin", "nautilus", "thunar", "nemo", "pcmanfm-qt", "pcmanfm" }
            : new[] { "nautilus", "thunar", "nemo", "pcmanfm-qt", "pcmanfm", "dolphin" };
        foreach (var manager in managers)
        {
            var request = new ProcessStartInfo(manager) { UseShellExecute = false };
            request.ArgumentList.Add(directory);
            try
            {
                using var process = Process.Start(PlatformCommand.SystemEnvironment(request));
                if (process != null && (!process.WaitForExit(200) || process.ExitCode == 0))
                    return true;
            }
            catch (Win32Exception) { }
        }
        return false;
    });

    public override Task MoveToTrash(string path) =>
        Task.Run(() => PlatformCommand.Execute("gio", ["trash", Path.GetFullPath(path)]));
}
