using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal Func<ContentDialog, Task<ContentDialogResult>> showOptionsRecoveryDialog =
        async dialog => await dialog.ShowAsync();

    sealed record OptionsBackupChoice(OptionsBackup Backup, string Label)
    {
        public override string ToString() => Label;
    }

    string OptionsBackupLabel(OptionsBackup b) =>
        b.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        + " · "
        + Ui(
            b.VerifiedAtStart && b.State == OptionsBackupState.ReloadFailed
                ? "Pre-launch options verified; reload failed later"
                : b.State switch
                {
                    OptionsBackupState.Healthy => "Normal exit verified",
                    OptionsBackupState.ReloadFailed => "Resource reload failed",
                    OptionsBackupState.BeforeRestore => "Before options restore",
                    OptionsBackupState.Manual => "Manual options backup",
                    OptionsBackupState.Pending => "Game session pending",
                    _ => "Not verified",
                }
        );

    Border OptionsBackupCard(Instance instance)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Options backups", 15, true));
        body.Children.Add(
            Text(
                "Back up options.txt, optionsof.txt and optionsshaders.txt when present before every launch, including resource pack selection, key bindings, video and sound settings.",
                12
            )
        );
        var limit = new NumberBox
        {
            Header = Ui("Maximum options backups"),
            Minimum = 2,
            Maximum = 100,
            SmallChange = 1,
            Value = Math.Clamp(instance.OptionsBackupLimit, 2, 100),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Tag = "options-backup-limit",
        };
        limit.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(limit.Value))
                return;
            var count = Math.Clamp((int)limit.Value, 2, 100);
            if (limit.Value != count)
            {
                limit.Value = count;
                return;
            }
            instance.OptionsBackupLimit = count;
            try
            {
                await store.Save();
                await Task.Run(() => core.OptionsBackups.Prune(instance));
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        body.Children.Add(limit);
        body.Children.Add(
            Text(
                "Keep 2–100 snapshots. The latest configuration verified by a normal exit is retained. Restore only after the game exits.",
                12
            )
        );
        var restore = Action("Restore options", async (_, _) => await ShowOptionsBackups(instance));
        restore.Tag = "options-backup-restore";
        body.Children.Add(Buttons(
            Action("Back up options now", async (_, _) =>
            {
                EnsureGameFilesIdle();
                var backup = await Task.Run(() => core.OptionsBackups.BackupNow(instance));
                ShowToast(Ui(backup == null ? "No options.txt to back up" : "Options backup created"));
            }),
            restore,
            Action("Delete options backup", async (_, _) => await ShowOptionsBackups(instance, true))
        ));
        var card = Card(body);
        card.Tag = "options-backups";
        return card;
    }

    async Task ShowOptionsBackups(Instance instance, bool delete = false)
    {
        EnsureGameFilesIdle();
        var backups = await Task.Run(() => core.OptionsBackups.List(instance));
        var choices = backups
            .Select(b => new OptionsBackupChoice(b, OptionsBackupLabel(b)))
            .ToArray();
        var preferred = Array.FindIndex(choices, c => c.Backup.State == OptionsBackupState.Healthy || c.Backup.VerifiedAtStart);
        if (preferred < 0) preferred = Array.FindIndex(choices, c => c.Backup.State is OptionsBackupState.Unknown or OptionsBackupState.BeforeRestore);
        if (preferred < 0 && choices.Length > 0) preferred = 0;
        var picker = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = delete ? -1 : preferred,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(
            Text(
                delete ? "Select a backup to permanently delete. Current game options are kept."
                    : "Restoring replaces resource pack selection, key bindings and other options with the selected snapshot. Current options are saved first so this can be undone.",
                13
            )
        );
        body.Children.Add(picker);
        if (choices.Length == 0)
            body.Children.Add(
                Text(
                    "No options backups yet. The first snapshot is created before a launch when options.txt exists.",
                    12
                )
            );
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui(delete ? "Delete options backup" : "Restore options"),
            Content = body,
            PrimaryButtonText = Ui(delete ? "Delete selected backup" : "Restore selected backup"),
            CloseButtonText = Ui("Cancel"),
            IsPrimaryButtonEnabled = !delete && choices.Length != 0,
            DefaultButton = ContentDialogButton.Close,
        };
        picker.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = picker.SelectedIndex >= 0;
        dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await showOptionsRecoveryDialog(dialog);
        }
        finally
        {
            dialogOpen = false;
        }
        if (
            result != ContentDialogResult.Primary
            || picker.SelectedItem is not OptionsBackupChoice chosen
        )
            return;
        EnsureGameFilesIdle();
        if (delete)
        {
            await Task.Run(() => core.OptionsBackups.Delete(instance, chosen.Backup.Id));
            ShowToast(Ui("Options backup deleted"));
            return;
        }
        await Task.Run(() => core.OptionsBackups.Restore(instance, chosen.Backup.Id));
        Report(Ui("Options restored. They will take effect on the next game launch."));
    }

    string OptionsRecoveryText(OptionsRecovery recovery) =>
        Ui(
            recovery.Recommended == null
                ? "Resource reload failed in the last game session. No earlier configuration has been verified by a normal exit. You can inspect snapshots in version settings."
                : "Resource reload failed in the last game session. Restore the previous configuration verified by a normal exit? Current options will be backed up before restoring."
        ) + (recovery.Recommended == null ? "" : "\n" + OptionsBackupLabel(recovery.Recommended));

    async Task ShowStartupOptionsRecoveries()
    {
        foreach (var instance in store.Config.Instances.ToArray())
        {
            try
            {
                var recovery = await Task.Run(() => core.OptionsBackups.PendingRecovery(instance));
                if (recovery == null)
                    continue;
                while (dialogOpen)
                    await Task.Delay(150);
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = Ui("Resource reload failed") + " · " + instance.Name,
                    Content = Text(OptionsRecoveryText(recovery)),
                    PrimaryButtonText = Ui(
                        recovery.Recommended == null ? "Choose a backup" : "Restore options"
                    ),
                    CloseButtonText = Ui("Not now"),
                    DefaultButton = ContentDialogButton.Close,
                };
                dialogOpen = true;
                ContentDialogResult result;
                try
                {
                    result = await showOptionsRecoveryDialog(dialog);
                }
                finally
                {
                    dialogOpen = false;
                }
                if (result == ContentDialogResult.Primary && recovery.Recommended != null)
                {
                    EnsureGameFilesIdle();
                    await Task.Run(() =>
                        core.OptionsBackups.Restore(instance, recovery.Recommended.Id)
                    );
                    Report(Ui("Options restored. They will take effect on the next game launch."));
                }
                else if (result == ContentDialogResult.Primary)
                    await ShowOptionsBackups(instance);
                await Task.Run(() =>
                    core.OptionsBackups.Acknowledge(instance, recovery.Failure.Id)
                );
            }
            catch (Exception ex)
            {
                AddLog("Options recovery: " + ex);
                await Error(ex);
            }
        }
    }
}
