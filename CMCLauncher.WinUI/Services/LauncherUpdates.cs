using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    readonly CancellationTokenSource updateLifetime = new();
    bool checkingLauncherUpdate;
    bool installingLauncherUpdate;
    LauncherRelease? launcherRelease;
    TextBlock? launcherUpdateStatus;
    Button? launcherCheckButton;
    Button? launcherInstallButton;
    HyperlinkButton? launcherReleaseLink;
    string launcherUpdateMessage = "";

    internal Border BuildLauncherUpdateSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text("Launcher updates", 18, true));
        launcherCheckButton = Action(
            "Check for updates",
            async (_, _) => await CheckLauncherUpdate(false)
        );
        launcherInstallButton = Action(
            "Download and install update",
            async (_, _) => await InstallLauncherUpdate(),
            true
        );
        var updateActions = Buttons(launcherCheckButton, launcherInstallButton);
        panel.Children.Add(updateActions);
        var versionRow = new Grid { ColumnSpacing = 12 };
        versionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versionRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        var currentVersion = Text(
            UiFormat("Current version: {0}", LauncherUpdates.CurrentVersion),
            12
        );
        currentVersion.VerticalAlignment = VerticalAlignment.Center;
        versionRow.Children.Add(currentVersion);
        launcherUpdateStatus = Text("", 13);
        launcherUpdateStatus.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(launcherUpdateStatus, 1);
        versionRow.Children.Add(launcherUpdateStatus);
        launcherReleaseLink = new HyperlinkButton
        {
            Content = Ui("View release notes"),
            NavigateUri = new Uri(LauncherUpdates.ReleasesPage),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        };
        updateActions.Children.Add(launcherReleaseLink);
        panel.Children.Add(versionRow);
        var automatic = new ToggleSwitch
        {
            Header = Ui("Check for updates on startup"),
            IsOn = store.Config.Settings.CheckLauncherUpdates,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        automatic.Toggled += async (_, _) =>
        {
            try
            {
                store.Config.Settings.CheckLauncherUpdates = automatic.IsOn;
                await store.Save();
                if (automatic.IsOn)
                    await CheckLauncherUpdate(false);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var beta = new ToggleSwitch
        {
            Header = Ui("Receive Beta releases"),
            IsOn = store.Config.Settings.ReceiveBetaUpdates,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        beta.Toggled += async (_, _) =>
        {
            try
            {
                store.Config.Settings.ReceiveBetaUpdates = beta.IsOn;
                launcherRelease = null;
                launcherUpdateMessage = "";
                RefreshLauncherUpdateControls();
                await store.Save();
                await CheckLauncherUpdate(false);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var switches = new Grid
        {
            ColumnSpacing = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        switches.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        switches.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        switches.Children.Add(automatic);
        Grid.SetColumn(beta, 1);
        switches.Children.Add(beta);
        panel.Children.Add(switches);
        panel.Children.Add(
            Text(
                "Beta releases may be less stable. Turning this off does not downgrade the installed version.",
                12
            )
        );
        RefreshLauncherUpdateControls();
        return Card(panel);
    }

    void RefreshLauncherUpdateControls()
    {
        if (launcherUpdateStatus == null)
            return;
        launcherUpdateStatus.Text =
            checkingLauncherUpdate ? Ui("Checking for updates…")
            : launcherRelease is { } release ? UiFormat("Update available: {0}", release.Version)
            : Ui(launcherUpdateMessage);
        if (
            !checkingLauncherUpdate
            && launcherRelease is { } available
            && available.Asset is not { Sha256.Length: 64 }
        )
            launcherUpdateStatus.Text +=
                "\n" + Ui("This release requires a manual download. Open its release notes.");
        if (launcherCheckButton != null)
            launcherCheckButton.IsEnabled = !checkingLauncherUpdate && !installingLauncherUpdate;
        if (launcherInstallButton != null)
        {
            launcherInstallButton.Visibility = launcherRelease?.Asset is { Sha256.Length: 64 }
                ? Visibility.Visible
                : Visibility.Collapsed;
            launcherInstallButton.IsEnabled = !checkingLauncherUpdate && !installingLauncherUpdate;
        }
        if (launcherReleaseLink != null)
            launcherReleaseLink.NavigateUri =
                launcherRelease?.Page ?? new Uri(LauncherUpdates.ReleasesPage);
    }

    async Task CheckLauncherUpdate(bool background)
    {
        // A channel change during a request causes a fresh request before publishing results.
        if (
            checkingLauncherUpdate
            || installingLauncherUpdate
            || updateLifetime.IsCancellationRequested
        )
            return;
        checkingLauncherUpdate = true;
        RefreshLauncherUpdateControls();
        try
        {
            while (true)
            {
                var beta = store.Config.Settings.ReceiveBetaUpdates;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    updateLifetime.Token
                );
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var client = LauncherUpdates.CreateClient(store.Config.Settings);
                var release = await new LauncherUpdates(client).Check(
                    LauncherUpdates.CurrentVersion,
                    beta,
                    LauncherUpdateInstaller.SingleFile,
                    timeout.Token
                );
                if (beta != store.Config.Settings.ReceiveBetaUpdates)
                    continue;
                if (background && !store.Config.Settings.CheckLauncherUpdates)
                    return;
                launcherRelease = release;
                launcherUpdateMessage = "You are up to date.";
                break;
            }
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            launcherUpdateMessage =
                "Could not check for updates. Try again or open the release page.";
            AddLog("Launcher update check failed: " + ex.Message);
        }
        finally
        {
            checkingLauncherUpdate = false;
            RefreshLauncherUpdateControls();
        }
        if (
            background
            && launcherRelease is { } available
            && !updateLifetime.IsCancellationRequested
        )
            await PromptStartupUpdate(available);
    }

    async Task PromptStartupUpdate(LauncherRelease release)
    {
        try
        {
            // WinUI permits only one ContentDialog at a time. Wait for the window and any existing dialog.
            while (dialogOpen || Content.XamlRoot == null)
                await Task.Delay(200, updateLifetime.Token);
            if (
                !store.Config.Settings.CheckLauncherUpdates
                || launcherRelease != release
                || (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
            )
                return;
            var canInstall = release.Asset is { Sha256.Length: 64 };
            dialogOpen = true;
            ContentDialogResult result;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = UiFormat("Update available: {0}", release.Version),
                    Content = Ui(
                        canInstall
                            ? "The launcher will download and verify the update, then close and restart."
                            : "This release requires a manual download. Open its release notes."
                    ),
                    PrimaryButtonText = Ui(canInstall ? "Update now" : "View release notes"),
                    CloseButtonText = Ui("Later"),
                    DefaultButton = ContentDialogButton.Close,
                    IsPrimaryButtonEnabled =
                        !canInstall || (task == null && game is not { HasExited: false }),
                };
                result = await dialog.ShowAsync();
            }
            finally
            {
                dialogOpen = false;
            }
            if (result != ContentDialogResult.Primary || updateLifetime.IsCancellationRequested)
                return;
            if (canInstall)
                await InstallLauncherUpdate(true);
            else
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(release.Page.AbsoluteUri)
                    {
                        UseShellExecute = true,
                    }
                );
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!updateLifetime.IsCancellationRequested)
                await Error(ex);
        }
    }

    async Task InstallLauncherUpdate(bool confirmed = false)
    {
        if (
            installingLauncherUpdate
            || launcherRelease is not { Asset: { Sha256.Length: 64 } asset } release
        )
            return;
        if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
            return;
        if (task != null || game is { HasExited: false })
            throw new InvalidOperationException(
                Ui("Finish active tasks and close the game before installing an update.")
            );
        if (!confirmed)
        {
            if (dialogOpen)
                return;
            dialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = UiFormat("Install update {0}?", release.Version),
                    Content = Ui(
                        "The launcher will download and verify the update, then close and restart. Your accounts, instances, and settings will be kept."
                    ),
                    PrimaryButtonText = Ui("Download and install update"),
                    CloseButtonText = Ui("Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }
            finally
            {
                dialogOpen = false;
            }
        }
        if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
            return;
        installingLauncherUpdate = true;
        RefreshLauncherUpdateControls();
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMCLauncher",
                "updates",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(directory);
            var package = Path.Combine(
                directory,
                LauncherUpdateInstaller.SingleFile ? "update.exe" : "update.zip"
            );
            var stage = Path.Combine(directory, "stage");
            IReadOnlyList<string>? files = null;
            await Run(
                async ct =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        ct,
                        updateLifetime.Token
                    );
                    using var client = LauncherUpdates.CreateClient(store.Config.Settings);
                    await new LauncherUpdates(client).Download(
                        asset,
                        package,
                        new Progress<double>(value => UpdateDownloadProgress(value)),
                        linked.Token,
                        store.Config.Settings.DownloadMirror
                    );
                    linked.Token.ThrowIfCancellationRequested();
                    files = await Task.Run(
                        () =>
                            LauncherUpdateInstaller.Stage(
                                package,
                                stage,
                                LauncherUpdateInstaller.SingleFile,
                                Path.GetFileName(LauncherUpdateInstaller.Executable)
                            ),
                        linked.Token
                    );
                    linked.Token.ThrowIfCancellationRequested();
                },
                Ui("Download launcher update")
            );
            if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
                return;
            if (task != null || game is { HasExited: false })
                throw new InvalidOperationException(
                    Ui("Finish active tasks and close the game before installing an update.")
                );
            await store.Save();
            await LauncherUpdateInstaller.Start(directory, stage, files!);
            Close();
        }
        finally
        {
            installingLauncherUpdate = false;
            RefreshLauncherUpdateControls();
        }
    }
}
