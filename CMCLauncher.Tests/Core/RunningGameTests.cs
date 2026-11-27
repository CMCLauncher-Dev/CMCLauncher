using System.Diagnostics;
using CMCLauncher;

static class RunningGameTests
{
    public static async Task Run(string root)
    {
        using var firstProcess = StartIdleProcess();
        using var secondProcess = StartIdleProcess();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = new RunningGames();
        var first = new Instance { Name = "First game" };
        var second = new Instance { Name = "Second game" };
        var firstDirectory = Path.Combine(root, "first");
        var secondDirectory = Path.Combine(root, "second");
        var firstGame = running.Add(first, firstDirectory, firstProcess);
        var secondGame = running.Add(first, firstDirectory, secondProcess);
        try
        {
            if (running.Active.Count() != 2
                || !running.Contains(new Instance(), firstDirectory + Path.DirectorySeparatorChar)
                || running.Contains(second, secondDirectory)
                || firstGame.Label == secondGame.Label)
                throw new IOException("Game process tracking did not isolate the running instances.");
            running.Stop(firstGame);
            await firstProcess.WaitForExitAsync(timeout.Token);
            running.Remove(firstGame);
            running.Stop(firstGame);
            if (!firstGame.StopRequested || secondProcess.HasExited || running.Active.Single() != secondGame
                || !running.Contains(first, firstDirectory))
                throw new IOException("Stopping one instance must leave the other instance running.");
            Console.WriteLine("PASS two launches of the same instance remain distinguishable and stop independently");
        }
        finally
        {
            running.Stop(firstGame);
            running.Stop(secondGame);
            await secondProcess.WaitForExitAsync(timeout.Token);
        }
    }

    static Process StartIdleProcess()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
            : new ProcessStartInfo("/bin/sleep", "60");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        return Process.Start(start) ?? throw new IOException("Could not start the game process fixture.");
    }
}
