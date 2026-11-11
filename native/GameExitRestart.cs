using System.Diagnostics;
using System.Text;

namespace CMCLauncher;

// A separate OS process survives a hidden, closed or unresponsive launcher.
public static class GameExitRestart
{
    public static void Watch(Process game, string executable, string logFile)
    {
        using var owner = Process.GetCurrentProcess();
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            function Find-Process($idValue, $started) {
                try {
                    $p = [Diagnostics.Process]::GetProcessById($idValue)
                    if ($p.StartTime.ToUniversalTime().Ticks -eq $started) { return $p }
                    $p.Dispose()
                } catch { }
                return $null
            }
            try {
                $ownerProcess = Find-Process {{owner.Id}} {{owner.StartTime.ToUniversalTime().Ticks}}
                $gameProcess = Find-Process {{game.Id}} {{game.StartTime.ToUniversalTime().Ticks}}
                if ($null -ne $gameProcess) { $gameProcess.WaitForExit(); $gameProcess.Dispose() }
                if ($null -ne $ownerProcess) {
                    if (!$ownerProcess.HasExited) {
                        $null = $ownerProcess.CloseMainWindow()
                        if (!$ownerProcess.WaitForExit(10000)) { $ownerProcess.Kill(); $ownerProcess.WaitForExit() }
                    }
                    $ownerProcess.Dispose()
                }
                Start-Process -FilePath {{Quote(
                Path.GetFullPath(executable)
            )}} -WorkingDirectory {{Quote(Path.GetDirectoryName(Path.GetFullPath(executable))!)}}
            } catch {
                [IO.File]::AppendAllText({{Quote(
                logFile
            )}}, [DateTime]::Now.ToString('s') + ' ' + $_.Exception.Message + [Environment]::NewLine)
                exit 1
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        var info = new ProcessStartInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            )
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (
            var argument in new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
            }
        )
            info.ArgumentList.Add(argument);
        foreach (
            var key in new[]
            {
                "CMCLAUNCHER_PROGRAM_DIRECTORY",
                "CMCLAUNCHER_UPDATE_EXECUTABLE",
                "CMCLAUNCHER_UPDATE_PARENT",
            }
        )
            info.Environment.Remove(key);
        using var watcher =
            Process.Start(info)
            ?? throw new IOException("Could not start the game-exit restart monitor.");
    }
}
