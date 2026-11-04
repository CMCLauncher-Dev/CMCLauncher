using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    CmsBlueprints? cmsClient;
    string cmsQuery = "",
        cmsSort = "download";
    int cmsPage = 1;
    CmsPage? cmsSnapshot;
    bool cmsInitialAttempted;
    string? cmsLastError;
    string cmsSnapshotQuery = "",
        cmsSnapshotSort = "download";
    CmsBlueprints Cms =>
        cmsClient ??= new CmsBlueprints(
            Store.SafePath(store.Root, "cache/cms"),
            store.Config.Settings
        );
    const string CmsNotice =
        "CMS currently has no public API. Frequent requests may cause HTTP 403 or 429 errors. Use search responsibly and avoid repeated requests in a short period.";

    bool cmsNoticeAccepted;
    ContentDialog? cmsNoticeDialog;

    async Task ShowCmsNotice()
    {
        var ready = false;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Access notice"),
            Content = Text(CmsNotice, 14),
            PrimaryButtonText = UiFormat("I understand ({0}s)", 3),
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.None,
        };
        var clock = new System.Diagnostics.Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var remaining = Math.Max(0, 3 - clock.Elapsed.TotalSeconds);
            ready = remaining <= 0;
            dialog.PrimaryButtonText = ready
                ? Ui("I understand")
                : UiFormat("I understand ({0}s)", (int)Math.Ceiling(remaining));
            dialog.IsPrimaryButtonEnabled = ready;
            if (ready)
                timer.Stop();
        };
        dialog.Opened += (_, _) =>
        {
            clock.Start();
            timer.Start();
        };
        dialog.Closing += (_, e) =>
        {
            if (!ready)
                e.Cancel = true;
        };
        cmsNoticeDialog = dialog;
        dialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                cmsNoticeAccepted = true;
        }
        finally
        {
            timer.Stop();
            clock.Stop();
            cmsNoticeDialog = null;
            dialogOpen = false;
        }
    }

    async Task CmsContent(Func<Instance?> target, bool hasTarget)
    {
        var generation = renderVersion;
        PageBody.Children.Add(
            new HyperlinkButton
            {
                Content = Ui("Open CMS in browser"),
                NavigateUri = new Uri(CmsBlueprints.Home),
            }
        );
        var query = new TextBox
        {
            Tag = "cms-search-input",
            Text = cmsQuery,
            PlaceholderText = Ui("Search CMS blueprints"),
            MaxLength = 200,
        };
        var sort = new BelowComboBox
        {
            ItemsSource = new[] { Ui("Most downloaded"), Ui("Latest") },
            SelectedIndex = cmsSort == "time" ? 1 : 0,
            Width = 145,
        };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(query);
        Grid.SetColumn(sort, 1);
        searchRow.Children.Add(sort);
        var status = new InfoBar { IsOpen = false, IsClosable = true };
        var rows = new StackPanel { Spacing = 8 };
        var pager = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
        var label = Text("", 13);
        var jump = new NumberBox
        {
            Minimum = 1,
            Maximum = 10000,
            Value = cmsPage,
            Width = 85,
        };
        var loading = false;
        CmsPage? displayed = null;
        Button? previous = null,
            next = null,
            go = null,
            search = null;
        async Task Load(bool userAction = true)
        {
            if (loading)
                return;
            loading = true;
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Informational;
            status.Message = Ui("Loading blueprints…");
            search!.IsEnabled = previous!.IsEnabled = next!.IsEnabled = go!.IsEnabled = false;
            using var request = new CancellationTokenSource();
            resourceSearch?.Cancel();
            resourceSearch = request;
            try
            {
                var result =
                    !userAction && cmsSnapshot != null
                        ? cmsSnapshot
                        : await Cms.Search(cmsQuery, cmsSort, cmsPage, request.Token);
                cmsSnapshot = result;
                cmsSnapshotQuery = cmsQuery;
                cmsSnapshotSort = cmsSort;
                if (generation != renderVersion)
                    return;
                displayed = result;
                rows.Children.Clear();
                foreach (var entry in result.Items)
                {
                    rows.Children.Add(BlueprintListCard(entry, target, "cms"));
                }
                status.IsOpen = result.Stale || result.Items.Count == 0;
                status.Severity = result.Stale
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Informational;
                status.Message = Ui(
                    result.Stale
                        ? "CMS is unavailable. Showing cached results that may be out of date."
                        : "No blueprints found."
                );
                label.Text = UiFormat("Page {0} / {1}", result.Page, result.Pages);
                jump.Value = result.Page;
                ApplyPersonalization();
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (generation == renderVersion)
                {
                    status.IsOpen = true;
                    status.Severity = InfoBarSeverity.Warning;
                    cmsLastError = CmsError(ex);
                    status.Message = cmsLastError;
                }
                if (cmsSnapshot != null)
                {
                    cmsPage = cmsSnapshot.Page;
                    cmsQuery = cmsSnapshotQuery;
                    cmsSort = cmsSnapshotSort;
                }
            }
            finally
            {
                loading = false;
                if (ReferenceEquals(resourceSearch, request))
                    resourceSearch = null;
                if (generation == renderVersion)
                {
                    search!.IsEnabled = true;
                    go!.IsEnabled = true;
                    previous!.IsEnabled = (displayed?.Page ?? 1) > 1;
                    next!.IsEnabled = displayed != null && displayed.Page < displayed.Pages;
                }
            }
        }
        async Task Search()
        {
            if (loading)
                return;
            Cms.ResumeUserRequest();
            cmsQuery = query.Text.Trim();
            cmsSort = sort.SelectedIndex == 1 ? "time" : "download";
            cmsPage = 1;
            await Load();
        }
        search = Action("Search", async (_, _) => await Search());
        search.Tag = "cms-search-button";
        Grid.SetColumn(search, 2);
        searchRow.Children.Add(search);
        PageBody.Children.Add(searchRow);
        // Text and focus changes never trigger CMS requests. Only these explicit actions search.
        query.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await Search();
            }
        };
        previous = Action(
            "Previous page",
            async (_, _) =>
            {
                if (!loading && cmsPage > 1)
                {
                    cmsPage--;
                    await Load();
                }
            }
        );
        next = Action(
            "Next page",
            async (_, _) =>
            {
                if (!loading && displayed != null && cmsPage < displayed.Pages)
                {
                    cmsPage++;
                    await Load();
                }
            }
        );
        go = Action(
            "Go",
            async (_, _) =>
            {
                if (!loading && double.IsFinite(jump.Value))
                {
                    cmsPage = (int)Math.Clamp(jump.Value, 1, displayed?.Pages ?? 10000);
                    await Load();
                }
            }
        );
        PageBody.Children.Add(status);
        PageBody.Children.Add(rows);
        foreach (var item in new UIElement[] { previous, label, jump, go, next })
            pager.Children.Add(item);
        PageBody.Children.Add(pager);
        if (cmsSnapshot != null)
        {
            cmsPage = cmsSnapshot.Page;
            cmsQuery = cmsSnapshotQuery;
            cmsSort = cmsSnapshotSort;
            query.Text = cmsQuery;
            sort.SelectedIndex = cmsSort == "time" ? 1 : 0;
        }
        if (!cmsNoticeAccepted)
            await ShowCmsNotice();
        if (generation != renderVersion || !cmsNoticeAccepted)
            return;
        if (cmsInitialAttempted && cmsSnapshot == null)
        {
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Warning;
            status.Message = cmsLastError ?? Ui("Search CMS to load blueprints.");
            previous.IsEnabled = next.IsEnabled = false;
            return;
        }
        cmsInitialAttempted = true;
        await Load(false);
    }

    string CmsError(Exception ex) =>
        ex is HttpRequestException
            ? Ui("Cannot connect to CMS. Check your connection or open the website.")
        : ex is OperationCanceledException ? Ui("CMS request timed out. Try again later.")
        : Ui(ex.Message);

    void CmsPreview(Grid holder, string url, int generation)
    {
        var started = false;
        holder.EffectiveViewportChanged += async (_, e) =>
        {
            if (
                started
                || e.EffectiveViewport.Width <= 0
                || e.EffectiveViewport.Height <= 0
                || e.EffectiveViewport.Right <= 0
                || e.EffectiveViewport.Bottom <= 0
                || e.EffectiveViewport.X >= holder.ActualWidth
                || e.EffectiveViewport.Y >= holder.ActualHeight
                || generation != renderVersion
            )
                return;
            started = true;
            using var request = new CancellationTokenSource();
            var finished = false;
            holder.Unloaded += (_, _) =>
            {
                if (!finished)
                    request.Cancel();
            };
            try
            {
                var bytes = await Cms.Cover(url, request.Token);
                if (generation != renderVersion)
                    return;
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                var bitmap = new BitmapImage { DecodePixelWidth = 240 };
                await bitmap.SetSourceAsync(stream);
                if (generation == renderVersion)
                    holder.Children.Add(
                        new Image
                        {
                            Source = bitmap,
                            Width = 120,
                            Height = 90,
                            Stretch = Stretch.Uniform,
                        }
                    );
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or HttpRequestException
                            or OperationCanceledException
                            or System.Runtime.InteropServices.COMException
                ) { }
            finally
            {
                finished = true;
            }
        };
    }

    async Task CmsSave(CmsFile file, bool install, Instance? instance, InfoBar status)
    {
        if (task != null)
            return;
        Cms.ResumeUserRequest();
        var stage = Store.SafePath(
            store.Root,
            "blueprint-downloads/" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string? destination = null;
            if (install)
            {
                EnsureGameFilesIdle();
                if (instance == null)
                    throw new IOException("Select an instance first.");
                destination =
                    SchematicManager.EnsureDirectory(store, instance)
                    ?? throw new IOException(
                        "This instance does not support schematics. Install Create or a schematic mod first."
                    );
            }
            await Run(
                async ct =>
                {
                    var downloaded = await Cms.Download(file, stage, ct);
                    if (install)
                        await SchematicManager.Import(destination!, new[] { downloaded }, ct);
                    else
                    {
                        var picker = new FolderPicker();
                        picker.FileTypeFilter.Add("*");
                        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder == null)
                            throw new OperationCanceledException();
                        var output = Store.SafePath(folder.Path, Path.GetFileName(downloaded));
                        if (File.Exists(output))
                            throw new IOException(
                                "A file with this name already exists. Choose a new name."
                            );
                        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            await using (var input = File.OpenRead(downloaded))
                            await using (
                                var stream = new FileStream(
                                    temporary,
                                    FileMode.CreateNew,
                                    FileAccess.Write
                                )
                            )
                                await input.CopyToAsync(stream, ct);
                            ct.ThrowIfCancellationRequested();
                            File.Move(temporary, output);
                        }
                        finally
                        {
                            if (File.Exists(temporary))
                                File.Delete(temporary);
                        }
                    }
                },
                install ? "Install blueprint" : "Download blueprint"
            );
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Success;
            status.Message = Ui("Blueprint saved successfully.");
        }
        catch (OperationCanceledException)
        {
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Informational;
            status.Message = Ui("Canceled");
        }
        catch (Exception ex)
        {
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Warning;
            status.Message = CmsError(ex);
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);
        }
    }
}
