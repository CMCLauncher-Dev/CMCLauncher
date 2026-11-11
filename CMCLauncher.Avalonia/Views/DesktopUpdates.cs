namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    bool checkingUpdate;

    async Task CheckDesktopUpdates(bool interactive)
    {
        if (checkingUpdate || Verifying)
            return;
        checkingUpdate = true;
        try
        {
            using var http = LauncherUpdates.CreateClient(session.Store.Config.Settings);
            var updates = new LauncherUpdates(http);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var release = await updates.Check(
                LauncherUpdates.CurrentVersion,
                session.Store.Config.Settings.ReceiveBetaUpdates,
                false,
                timeout.Token,
                DesktopUpdateInstaller.Rid
            );
            if (release == null)
            {
                if (interactive)
                    ShowToast(Ui("No updates available."));
                return;
            }
            if (!await Confirm("Update available", release.Version + "\n" + release.Notes))
                return;
            session.EnsureFilesIdle();
            if (
                release.Asset == null
                || DesktopUpdateInstaller.PackageManaged
                || !string.Equals(
                    Path.GetFileName(Environment.ProcessPath),
                    DesktopUpdateInstaller.ExecutableName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Open(release.Page.ToString());
                return;
            }
            var directory = Path.Combine(
                Path.GetTempPath(),
                "cmcl-desktop-update-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(directory);
            var ready = false;
            try
            {
                var stage = Path.Combine(directory, "stage");
                await session.Run(
                    "Install launcher update",
                    async ct =>
                    {
                        var package = Path.Combine(directory, release.Asset.Name);
                        await updates.Download(
                            release.Asset,
                            package,
                            null,
                            ct,
                            session.Store.Config.Settings.DownloadMirror
                        );
                        await DesktopUpdateInstaller.Stage(package, stage, ct);
                        ready = true;
                    }
                );
                if (ready)
                {
                    DesktopUpdateInstaller.Start(stage, AppContext.BaseDirectory);
                    Close();
                }
            }
            finally
            {
                if (!ready && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
        catch (Exception ex)
        {
            session.Log("WARN", "Desktop update check: " + ex.Message);
            if (interactive)
                await Error(ex);
        }
        finally
        {
            checkingUpdate = false;
        }
    }
}
