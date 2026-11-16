using Avalonia.Controls;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
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
                    OptionsBackupState.Pending => "Game session pending",
                    _ => "Not verified",
                }
        );

    Control OptionsBackupCard(Instance instance)
    {
        var limit = Number(Math.Clamp(instance.OptionsBackupLimit, 2, 100), 2, 100);
        limit.Increment = 1;
        limit.Tag = "options-backup-limit";
        limit.ValueChanged += async (_, _) =>
        {
            if (limit.Value == null)
                return;
            var count = Math.Clamp((int)limit.Value.Value, 2, 100);
            if (limit.Value != count)
            {
                limit.Value = count;
                return;
            }
            instance.OptionsBackupLimit = count;
            try
            {
                await session.Save();
                await Task.Run(() => session.Core.OptionsBackups.Prune(instance));
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var restore = Btn("Restore options", () => ShowOptionsBackups(instance));
        restore.Tag = "options-backup-restore";
        var card = Card(
            Stack(
                T("Options backups", 16, true),
                T(
                    "Back up options.txt, optionsof.txt and optionsshaders.txt when present before every launch, including resource pack selection, key bindings, video and sound settings.",
                    12
                ),
                Field("Maximum options backups", limit),
                T(
                    "Keep 2–100 snapshots. The latest configuration verified by a normal exit is retained. Restore only after the game exits.",
                    12
                ),
                restore
            )
        );
        card.Tag = "options-backups";
        return card;
    }

    async Task ShowOptionsBackups(Instance instance)
    {
        if (session.GameRunning)
            throw new IOException(Ui("Exit the game before restoring options."));
        var backups = await Task.Run(() => session.Core.OptionsBackups.List(instance));
        if (backups.Count == 0)
        {
            await Dialog(
                "Restore options",
                Stack(
                    T(
                        "No options backups yet. The first snapshot is created before a launch when options.txt exists."
                    )
                ),
                false
            );
            return;
        }
        var choices = backups
            .Select(b => new OptionsBackupChoice(b, OptionsBackupLabel(b)))
            .ToArray();
        var preferred = Array.FindIndex(choices, c => c.Backup.State == OptionsBackupState.Healthy || c.Backup.VerifiedAtStart);
        if (preferred < 0) preferred = Array.FindIndex(choices, c => c.Backup.State is OptionsBackupState.Unknown or OptionsBackupState.BeforeRestore);
        if (preferred < 0) preferred = 0;
        var picker = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = preferred,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        if (
            !await Dialog(
                "Restore options",
                Stack(
                    T(
                        "Restoring replaces resource pack selection, key bindings and other options with the selected snapshot. Current options are saved first so this can be undone."
                    ),
                    picker
                ),
                true
            )
        )
            return;
        if (session.GameRunning)
            throw new IOException(Ui("Exit the game before restoring options."));
        if (picker.SelectedItem is not OptionsBackupChoice selected)
            return;
        await Task.Run(() => session.Core.OptionsBackups.Restore(instance, selected.Backup.Id));
        ShowToast(Ui("Options restored. They will take effect on the next game launch."));
    }

    async Task ShowStartupOptionsRecoveries()
    {
        foreach (var instance in session.Store.Config.Instances.ToArray())
        {
            try
            {
                var recovery = await Task.Run(() =>
                    session.Core.OptionsBackups.PendingRecovery(instance)
                );
                if (recovery == null)
                    continue;
                var message =
                    recovery.Recommended == null
                        ? "Resource reload failed in the last game session. No earlier configuration has been verified by a normal exit. You can inspect snapshots in version settings."
                        : "Resource reload failed in the last game session. Restore the previous configuration verified by a normal exit? Current options will be backed up before restoring.";
                var body = Stack(T(instance.Name, 16, true), T(message));
                if (recovery.Recommended != null)
                    body.Children.Add(T(OptionsBackupLabel(recovery.Recommended)));
                if (await Dialog("Resource reload failed", body, true))
                {
                    if (recovery.Recommended != null)
                    {
                        if (session.GameRunning)
                            throw new IOException(Ui("Exit the game before restoring options."));
                        await Task.Run(() =>
                            session.Core.OptionsBackups.Restore(instance, recovery.Recommended.Id)
                        );
                        ShowToast(
                            Ui("Options restored. They will take effect on the next game launch.")
                        );
                    }
                    else
                        await ShowOptionsBackups(instance);
                }
                await Task.Run(() =>
                    session.Core.OptionsBackups.Acknowledge(instance, recovery.Failure.Id)
                );
            }
            catch (Exception ex)
            {
                session.Log("ERROR", "Options recovery: " + ex);
                await Error(ex);
            }
        }
    }
}
