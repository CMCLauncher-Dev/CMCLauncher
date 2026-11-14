using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyCrashExit(Action<bool, string> check, string directory)
    {
        var previousShow = showCrashReportDialog;
        var previousBehavior = launcherExitBehavior;
        var dialogs = 0;
        var instance = new Instance
        {
            Name = "Crash exit fixture",
            Directory = Path.Combine(directory, "crash-exit-game"),
        };
        showCrashReportDialog = dialog =>
        {
            dialogs++;
            check(AppWindow.IsVisible, "crash report restores its hidden launcher owner");
            return Task.FromResult(ContentDialogResult.None);
        };
        try
        {
            foreach (var code in new[] { 0, 7 })
            {
                var started = DateTimeOffset.UtcNow;
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
                    var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "exit " + code }
                )
                    start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync();
                AppWindow.Hide();
                launcherExitBehavior = "reopen";
                await HandleGameExit(instance, process, started);
                check(
                    dialogs == (code == 0 ? 0 : 1),
                    code == 0
                        ? "clean game exit does not open a crash dialog"
                        : "nonzero game exit opens the crash report automatically"
                );
            }
        }
        finally
        {
            AppWindow.Show();
            launcherExitBehavior = previousBehavior;
            showCrashReportDialog = previousShow;
        }
    }
}
