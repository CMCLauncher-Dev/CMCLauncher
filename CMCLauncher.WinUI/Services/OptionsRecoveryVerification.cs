using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyOptionsRecoveryUi(Action<bool, string> check, string directory)
    {
        var instance = new Instance
        {
            Name = "Options recovery fixture",
            Directory = Path.Combine(directory, "options-game"),
            OptionsBackupLimit = 3,
        };
        Directory.CreateDirectory(instance.Directory);
        var file = Path.Combine(instance.Directory, "options.txt");
        File.WriteAllText(file, "resourcePacks:[]\nkey_key.forward:key.keyboard.w\n");
        var good = core.OptionsBackups.BeforeLaunch(instance)!;
        core.OptionsBackups.Complete(instance, good.Id, 0);
        File.WriteAllText(file, "resourcePacks:[\"file/broken.zip\"]\n");
        var bad = core.OptionsBackups.BeforeLaunch(instance)!;
        core.OptionsBackups.Observe(instance, bad.Id, "[ERROR] Failed to load resource packs");
        var previousShow = showCrashReportDialog;
        var previousOptionsShow = showOptionsRecoveryDialog;
        var offered = false;
        store.Config.Instances.Add(instance);
        try
        {
            await SwitchLanguage("zh_cn");
            foreach (var theme in new[] { "light", "dark" })
            {
                store.Config.Settings.Theme = theme;
                ApplyTheme();
                PageBody.Children.Clear();
                InstanceOverview(instance);
                await Task.Delay(100);
                check(
                    Descendants(PageBody)
                        .OfType<NumberBox>()
                        .Any(n =>
                            Equals(n.Tag, "options-backup-limit")
                            && n.Minimum == 2
                            && n.Maximum == 100
                        ),
                    theme + ": instance options backup limit is editable"
                );
                check(
                    Descendants(PageBody)
                        .OfType<Button>()
                        .Any(b => Equals(b.Tag, "options-backup-restore")),
                    theme + ": instance options restore entry is visible"
                );
                await Capture(
                    Path.Combine(directory, "options-backups-" + theme + ".png"),
                    Descendants(PageBody)
                        .OfType<Microsoft.UI.Xaml.Controls.Border>()
                        .Single(b => Equals(b.Tag, "options-backups"))
                );
            }
            showCrashReportDialog = dialog =>
            {
                offered =
                    dialog.Title?.ToString() == Ui("Resource reload failed")
                    && dialog.SecondaryButtonText == Ui("Restore options");
                return Task.FromResult(ContentDialogResult.Secondary);
            };
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
                var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "exit 0" }
            )
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            await HandleGameExit(instance, process, DateTimeOffset.UtcNow.AddSeconds(-1));
            check(
                offered && File.ReadAllText(file).Contains("resourcePacks:[]"),
                "resource reload failure offers and restores the verified config even with exit code zero"
            );
            check(
                core.OptionsBackups.PendingRecovery(instance) == null,
                "completed recovery does not prompt again at startup"
            );
            showOptionsRecoveryDialog = dialog =>
            {
                check(
                    dialog.IsPrimaryButtonEnabled,
                    "manual options restore lists available snapshots"
                );
                return Task.FromResult(ContentDialogResult.None);
            };
            await ShowOptionsBackups(instance);
            await store.Save();
            check(
                new Store(store.Root)
                    .Config.Instances.Single(i => i.Id == instance.Id)
                    .OptionsBackupLimit == 3,
                "per-instance backup retention survives settings reload"
            );
        }
        finally
        {
            showCrashReportDialog = previousShow;
            showOptionsRecoveryDialog = previousOptionsShow;
            store.Config.Instances.Remove(instance);
            await store.Save();
            page = "home";
            await RenderAsync();
        }
    }
}
