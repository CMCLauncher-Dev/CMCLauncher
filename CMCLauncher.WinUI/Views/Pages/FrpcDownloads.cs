using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Func<int, CancellationToken, Task<FrpcReleasePage>> fetchFrpcReleases = null!;
    readonly List<string> frpcOnlineVersions = new();
    string? frpcDownloadVersion;
    Task frpcVersionsLoad = Task.CompletedTask;

    void BuildFrpcDownloads(Settings settings, string[] versions)
    {
        var generation = renderVersion;
        var cancellation = new CancellationTokenSource();
        var requestRevision = 0;
        var summary = Text(
            versions.Length > 0
                ? Ui("Installed · ") + settings.FrpcVersion
                : Ui("Client not downloaded"),
            12
        );
        var downloadVersion = new BelowComboBox
        {
            Header = Ui("Online versions"),
            PlaceholderText = Ui("Loading releases…"),
            Padding = new Thickness(12, 7, 36, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-download-version",
        };
        var installed = new BelowComboBox
        {
            Header = Ui("Downloaded client"),
            PlaceholderText = Ui("Not downloaded"),
            Padding = new Thickness(12, 7, 36, 7),
            ItemsSource = versions,
            SelectedItem = settings.FrpcVersion,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        installed.SelectionChanged += async (_, _) =>
        {
            if (installed.SelectedItem is string value)
            {
                settings.FrpcVersion = value;
                summary.Text = Ui("Installed · ") + value;
                await store.Save();
                UpdateFrpcStatus();
            }
        };
        downloadVersion.SelectionChanged += (_, _) =>
            frpcDownloadVersion = downloadVersion.SelectedItem as string;
        var status = Text("", 12);
        status.Visibility = Visibility.Collapsed;
        void Populate()
        {
            var choice = frpcDownloadVersion;
            downloadVersion.ItemsSource = frpcOnlineVersions.ToArray();
            downloadVersion.SelectedItem = frpcOnlineVersions.Contains(choice ?? "")
                ? choice
                : frpcOnlineVersions.FirstOrDefault();
        }
        async Task Load()
        {
            var revision = ++requestRevision;
            try
            {
                status.Visibility = Visibility.Visible;
                status.Text = Ui("Fetching official releases…");
                var result = await fetchFrpcReleases(1, cancellation.Token);
                if (generation != renderVersion || revision != requestRevision)
                    return;
                frpcOnlineVersions.Clear();
                foreach (var version in result.Versions)
                    if (!frpcOnlineVersions.Contains(version))
                        frpcOnlineVersions.Add(version);
                frpcOnlineVersions.Sort(
                    (a, b) => System.Version.Parse(b).CompareTo(System.Version.Parse(a))
                );
                Populate();
                downloadVersion.PlaceholderText = Ui("No releases available");
                status.Text = UiFormat("{0} releases loaded", frpcOnlineVersions.Count);
                if (result.Source == "web")
                    status.Text += Ui(" · Using official release page");
                if (result.IsCached)
                    status.Text += Ui(" · Offline; using local cache");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (generation == renderVersion && revision == requestRevision)
                {
                    Populate();
                    status.Text = ex.Message;
                }
            }
        }
        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.Children.Add(
            Action(
                Ui("Download online"),
                async (_, _) =>
                {
                    var version =
                        downloadVersion.SelectedItem as string
                        ?? throw new IOException(Ui("Select an online release first."));
                    await Run(
                        async ct =>
                        {
                            var release = await frpc.FindRelease(version, ct);
                            await frpc.Install(release, ct);
                            settings.FrpcVersion = release.Version;
                            await store.Save();
                        },
                        Ui("Download frpc")
                    );
                    Render();
                },
                true
            )
        );
        var deleteVersion = Action(
            "Delete downloaded version",
            async (sender, _) =>
            {
                if (installed.SelectedItem is not string version)
                    return;
                if (
                    !await ConfirmPop(
                        (FrameworkElement)sender,
                        Ui("Delete downloaded version?"),
                        version,
                        Ui("Remove")
                    )
                )
                    return;
                await Run(
                    async ct =>
                    {
                        await frpc.Remove(version, ct);
                        if (settings.FrpcVersion == version)
                            settings.FrpcVersion = frpc.Installed().FirstOrDefault() ?? "";
                        await store.Save();
                    },
                    Ui("Delete downloaded version")
                );
                Render();
            }
        );
        deleteVersion.IsEnabled = versions.Length > 0 && !frpc.Running;
        deleteVersion.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(deleteVersion, 1);
        actions.Children.Add(deleteVersion);
        var fields = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.Children.Add(downloadVersion);
        Grid.SetColumn(installed, 1);
        fields.Children.Add(installed);
        bool? fieldsNarrow = null;
        fields.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 480;
            if (fieldsNarrow == narrow)
                return;
            fieldsNarrow = narrow;
            Grid.SetColumnSpan(downloadVersion, narrow ? 2 : 1);
            Grid.SetColumnSpan(installed, narrow ? 2 : 1);
            Grid.SetColumn(installed, narrow ? 0 : 1);
            Grid.SetRow(installed, narrow ? 1 : 0);
        };
        var content = new StackPanel
        {
            Spacing = 10,
            Children = { Text("frpc", 15, true), summary, fields, actions, status },
        };
        var panel = Card(content);
        panel.Padding = new Thickness(12);
        panel.Tag = "frpc-download-panel";
        panel.Unloaded += (_, _) => cancellation.Cancel();
        PageBody.Children.Add(panel);
        Populate();
        frpcVersionsLoad = frpcOnlineVersions.Count == 0 ? Load() : Task.CompletedTask;
    }
}
