using System.Diagnostics;
using CMCLauncher;

static class GameExitRestartTests
{
    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "unexpected-restart.txt");
        var replacement = Path.Combine(root, "replacement.cmd");
        await File.WriteAllTextAsync(
            replacement,
            "@echo off\r\necho restarted>\"" + marker + "\"\r\n"
        );
        var shell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe"
        );
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (
            var arg in new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Start-Sleep -Seconds 1",
            }
        )
            start.ArgumentList.Add(arg);
        using var game = Process.Start(start)!;
        var log = Path.Combine(root, "monitor.log");
        var acknowledge = GameExitRestart.Watch(game, replacement, log);
        await game.WaitForExitAsync();
        acknowledge();
        var timeout = Stopwatch.StartNew();
        while (
            Directory.EnumerateFiles(root, "*.ack").Any()
            && timeout.Elapsed < TimeSpan.FromSeconds(15)
        )
            await Task.Delay(100);
        if (Directory.EnumerateFiles(root, "*.ack").Any() || File.Exists(log))
            throw new Exception("The game-exit monitor did not consume the UI acknowledgment.");
        await Task.Delay(11000);
        if (File.Exists(marker))
            throw new Exception("A handled game exit restarted the launcher.");
    }
}
