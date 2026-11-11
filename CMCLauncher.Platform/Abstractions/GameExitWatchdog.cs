using System.Diagnostics;
using System.Globalization;

namespace CMCLauncher.Platform;

public static class GameExitWatchdog
{
    const string Switch = "--watch-game-exit";

    public static void Start(Process game, string executable, string? assembly = null)
    {
        using var owner = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(executable)
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
                owner.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                game.Id.ToString(CultureInfo.InvariantCulture),
                game.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                executable,
                assembly ?? "",
                AppContext.BaseDirectory,
            }
        )
            info.ArgumentList.Add(value);
        using var watcher =
            Process.Start(info) ?? throw new IOException("Could not start the game-exit monitor.");
    }

    // A separate process still observes game exit if the UI hangs or is closed during play.
    public static bool Handle(string[] args)
    {
        if (args.Length == 0 || args[0] != Switch)
            return false;
        if (args.Length != 8)
            throw new ArgumentException("Invalid game watchdog arguments.");
        var ownerId = int.Parse(args[1], CultureInfo.InvariantCulture);
        var ownerStart = long.Parse(args[2], CultureInfo.InvariantCulture);
        var gameId = int.Parse(args[3], CultureInfo.InvariantCulture);
        var gameStart = long.Parse(args[4], CultureInfo.InvariantCulture);
        while (Matches(gameId, gameStart))
            Thread.Sleep(250);
        var deadline = Stopwatch.StartNew();
        while (Matches(ownerId, ownerStart) && deadline.Elapsed < TimeSpan.FromSeconds(10))
            Thread.Sleep(250);
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

    static bool Matches(int id, long started)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
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
