using System.Diagnostics;
using System.Globalization;

namespace CMCLauncher.Platform;

public static class GameExitWatchdog
{
    const string Switch = "--watch-game-exit";

    public static Action Start(Process game, string executable, string? assembly = null)
    {
        using var owner = Process.GetCurrentProcess();
        var ownerStart = StartIdentity(owner);
        var gameStart = StartIdentity(game);
        if (ownerStart == null || gameStart == null)
            return static () => { };
        var acknowledgment = Path.Combine(Path.GetTempPath(), $"cmcl-game-exit-{Guid.NewGuid():N}.ack");
        var info = new ProcessStartInfo(Environment.ProcessPath ?? executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        if (assembly != null)
            info.ArgumentList.Add(assembly);
        foreach (
            var value in new[]
            {
                Switch,
                owner.Id.ToString(CultureInfo.InvariantCulture),
                ownerStart.Value.ToString(CultureInfo.InvariantCulture),
                game.Id.ToString(CultureInfo.InvariantCulture),
                gameStart.Value.ToString(CultureInfo.InvariantCulture),
                executable,
                assembly ?? "",
                AppContext.BaseDirectory,
                acknowledgment,
            }
        )
            info.ArgumentList.Add(value);
        using var watcher =
            Process.Start(info) ?? throw new IOException("Could not start the game-exit monitor.");
        return () => File.WriteAllText(acknowledgment, "");
    }

    // A separate process still observes game exit if the UI hangs or is closed during play.
    public static bool Handle(string[] args)
    {
        if (args.Length == 0 || args[0] != Switch)
            return false;
        if (args.Length != 9)
            throw new ArgumentException("Invalid game watchdog arguments.");
        var ownerId = int.Parse(args[1], CultureInfo.InvariantCulture);
        var ownerStart = long.Parse(args[2], CultureInfo.InvariantCulture);
        var gameId = int.Parse(args[3], CultureInfo.InvariantCulture);
        var gameStart = long.Parse(args[4], CultureInfo.InvariantCulture);
        // A watchdog that could not establish both identities at startup cannot
        // distinguish a stale PID from a newly launched game. Leave the UI alone.
        if (!Matches(gameId, gameStart) || !Matches(ownerId, ownerStart))
            return true;
        while (Matches(gameId, gameStart))
            Thread.Sleep(250);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(args[8]))
            {
                File.Delete(args[8]);
                return true;
            }
            if (!Matches(ownerId, ownerStart))
                break;
            Thread.Sleep(250);
        }
        if (File.Exists(args[8]))
        {
            File.Delete(args[8]);
            return true;
        }
        if (Matches(ownerId, ownerStart))
        {
            using var owner = Process.GetProcessById(ownerId);
            // Do not kill the process tree: frpc or a user-created child may be independent.
            owner.Kill();
            owner.WaitForExit(5000);
        }
        var restart = new ProcessStartInfo(args[5])
        {
            UseShellExecute = false,
            WorkingDirectory = args[7],
        };
        if (args[6].Length != 0)
            restart.ArgumentList.Add(args[6]);
        Process.Start(restart)?.Dispose();
        return true;
    }

    static long? StartIdentity(Process process)
    {
        if (process.HasExited)
            return null;
        if (!OperatingSystem.IsLinux())
            return process.StartTime.ToUniversalTime().Ticks;

        // Linux exposes the same start tick to both processes. Converting it to
        // a wall-clock time can use different boot-time estimates in each host.
        try
        {
            var stat = File.ReadAllText($"/proc/{process.Id}/stat");
            // The command name is parenthesized and may itself contain spaces or ')'.
            var endOfName = stat.LastIndexOf(')');
            if (endOfName < 0)
                return null;
            var fields = stat[(endOfName + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // The suffix begins at field 3 (state); field 22 is starttime.
            if (fields.Length <= 19 || fields[0] is "Z" or "X" or "x")
                return null;
            if (long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                return ticks;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        return null;
    }

    static bool Matches(int id, long started)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return StartIdentity(process) == started;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
