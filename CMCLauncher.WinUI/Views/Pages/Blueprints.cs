using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    string blueprintQuery = "";
    string blueprintSource = "schematicraft";
    int blueprintPage = 1;
    readonly Dictionary<(string Source, string Query, int Page), BlueprintPage> blueprintListCache =
        new();

    Func<HttpClient>? blueprintHttpOverride;

    HttpClient BlueprintHttp() =>
        blueprintHttpOverride?.Invoke()
        ?? new(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                Proxy = NetworkProxy.Create(store.Config.Settings),
                UseProxy = NetworkProxy.Mode(store.Config.Settings) != "direct",
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(45),
        };

    async Task BlueprintsPage()
    {
        Header("Blueprints", "");
        var generation = renderVersion;
        Func<Task> load = () => Task.CompletedTask;
        var source = new BelowComboBox
        {
            Header = Ui("Download source"),
            ItemsSource = new[]
            {
                "Schematicraft",
                Ui("MCS Blueprint Library"),
                Ui("CMS Blueprint Library"),
            },
            SelectedIndex =
                blueprintSource == "cms" ? 2
                : blueprintSource == "mcschematic" ? 1
                : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        source.SelectionChanged += async (_, _) =>
        {
            if (source.SelectedIndex < 0)
                return;
            var wasCms = blueprintSource == "cms";
            blueprintSource =
                source.SelectedIndex == 2 ? "cms"
                : source.SelectedIndex == 1 ? "mcschematic"
                : "schematicraft";
            blueprintPage = 1;
            if (!wasCms && blueprintSource == "cms")
                cmsNoticeAccepted = false;
            if (wasCms || blueprintSource == "cms")
                await RenderAsync();
            else
                await load();
        };
        PageBody.Children.Add(source);
        if (pageFrameReady)
            NavigatePage(PageBody);
        var candidates = store.Config.Instances.ToArray();
        var supported = await Task.Run(() =>
            candidates
                .Where(instance =>
                {
                    try
                    {
                        return SchematicManager.EnsureDirectory(store, instance, false) != null;
                    }
                    catch (Exception ex)
                        when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        return false;
                    }
                })
                .ToArray()
        );
        if (generation != renderVersion)
            return;
        Instance? target =
            supported.FirstOrDefault(i => i == selected) ?? supported.FirstOrDefault();
        var destination = new BelowComboBox
        {
            Header = Ui("Install to instance"),
            ItemsSource = supported,
            DisplayMemberPath = nameof(Instance.Name),
            SelectedItem = target,
            PlaceholderText = Ui("No compatible instances"),
            IsEnabled = supported.Length > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        PageBody.Children.Add(destination);
        var destinationHint = Text("", 12);
        void UpdateDestinationHint() =>
            destinationHint.Text =
                target == null
                    ? Ui(
                        "No compatible instances. Install Create or a schematic mod in a modded instance first. You can still download files."
                    )
                    : Ui("Installation folder: ")
                        + SchematicManager.EnsureDirectory(store, target, false);
        destination.SelectionChanged += (_, _) =>
        {
            target = destination.SelectedItem as Instance;
            UpdateDestinationHint();
        };
        UpdateDestinationHint();
        PageBody.Children.Add(destinationHint);
        if (blueprintSource == "cms")
        {
            await CmsContent(() => target, supported.Length > 0);
            return;
        }
        var query = new TextBox
        {
            PlaceholderText = Ui("Search blueprints"),
            Text = blueprintQuery,
        };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(query);
        async Task SearchBlueprints()
        {
            blueprintQuery = query.Text.Trim();
            blueprintPage = 1;
            await load();
        }
        var searchButton = Action("Search", async (_, _) => await SearchBlueprints());
        Grid.SetColumn(searchButton, 1);
        searchRow.Children.Add(searchButton);
        query.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await SearchBlueprints();
            }
        };
        PageBody.Children.Add(searchRow);
        var status = Text("", 12);
        PageBody.Children.Add(status);
        var rows = new StackPanel { Spacing = 8 };
        PageBody.Children.Add(rows);
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var previous = Action(
            "Previous page",
            async (_, _) =>
            {
                if (blueprintPage > 1)
                {
                    blueprintPage--;
                    await load();
                }
            }
        );
        var next = Action(
            "Next page",
            async (_, _) =>
            {
                blueprintPage++;
                await load();
            }
        );
        var pageLabel = Text("", 13);
        var jump = new NumberBox
        {
            Minimum = 1,
            Value = blueprintPage,
            Width = 85,
        };
        var go = Action(
            "Go",
            async (_, _) =>
            {
                if (double.IsFinite(jump.Value) && jump.Value >= 1 && jump.Value <= int.MaxValue)
                {
                    blueprintPage = (int)jump.Value;
                    await load();
                }
            }
        );
        foreach (var item in new UIElement[] { previous, pageLabel, jump, go, next })
            pager.Children.Add(item);
        PageBody.Children.Add(pager);
        var revision = 0;
        async Task Load()
        {
            var current = ++revision;
            var selectedSource = blueprintSource;
            rows.Children.Clear();
            status.Text = Ui("Loading blueprints…");
            previous.IsEnabled = next.IsEnabled = go.IsEnabled = false;
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            resourceSearch?.Cancel();
            resourceSearch = request;
            try
            {
                using var http = BlueprintHttp();
                var key = (selectedSource, blueprintQuery, blueprintPage);
                if (!blueprintListCache.TryGetValue(key, out var result))
                {
                    result = await new BlueprintService(http).Search(
                        blueprintQuery,
                        blueprintPage,
                        request.Token,
                        selectedSource
                    );
                    if (blueprintListCache.Count >= 20)
                        blueprintListCache.Remove(blueprintListCache.Keys.First());
                    blueprintListCache[key] = result;
                }
                if (generation != renderVersion || current != revision)
                    return;
                status.Text = result.Items.Count == 0 ? Ui("No blueprints found.") : "";
                foreach (var entry in result.Items)
                {
                    rows.Children.Add(BlueprintListCard(entry, () => target, selectedSource));
                }
                pageLabel.Text = UiFormat("Page {0}", blueprintPage);
                jump.Value = blueprintPage;
                previous.IsEnabled = blueprintPage > 1;
                next.IsEnabled = result.HasNext;
                go.IsEnabled = true;
                ApplyPersonalization();
                PageViewport.ChangeView(null, 0, null, true);
            }
            catch (Exception ex)
            {
                if (generation == renderVersion && current == revision)
                {
                    status.Text =
                        ex is OperationCanceledException
                            ? Ui("Loading timed out. Try again.")
                            : Ui(ex.Message);
                    previous.IsEnabled = blueprintPage > 1;
                    go.IsEnabled = true;
                }
            }
            finally
            {
                if (ReferenceEquals(resourceSearch, request))
                    resourceSearch = null;
            }
        }
        load = Load;
        // Mount the page shell before awaiting the network so navigation and its fade start immediately.
        if (!pageFrameReady)
            pendingPageBody = PageBody;
        await load();
    }

    async Task DownloadBlueprint(
        Blueprint entry,
        bool install,
        string source,
        Instance? target = null
    )
    {
        var instance = target ?? selected;
        string? destination = null;
        if (install)
        {
            if (instance == null)
                throw new InvalidOperationException(Ui("Select an instance first."));
            EnsureGameFilesIdle();
            destination =
                SchematicManager.EnsureDirectory(store, instance)
                ?? throw new InvalidOperationException(
                    Ui(
                        "This instance does not support schematics. Install Create or a schematic mod first."
                    )
                );
        }
        var stage = Store.SafePath(
            store.Root,
            "blueprint-downloads/" + Guid.NewGuid().ToString("N")
        );
        try
        {
            await Run(
                async ct =>
                {
                    using var http = BlueprintHttp();
                    var file = await new BlueprintService(http).Download(
                        entry.Id,
                        stage,
                        ct,
                        source,
                        entry.Version
                    );
                    if (install)
                        await SchematicManager.Import(destination!, new[] { file }, ct);
                    else
                    {
                        var picker = new FileSavePicker
                        {
                            SuggestedFileName = Path.GetFileNameWithoutExtension(file),
                        };
                        picker.FileTypeChoices.Add(
                            Ui("Blueprint"),
                            new[] { Path.GetExtension(file) }
                        );
                        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                        var target = await picker.PickSaveFileAsync();
                        if (target == null)
                            throw new OperationCanceledException();
                        await using var input = File.OpenRead(file);
                        await using var output = new FileStream(
                            target.Path,
                            FileMode.Create,
                            FileAccess.Write
                        );
                        await input.CopyToAsync(output, ct);
                    }
                },
                install ? "Install blueprint" : "Download blueprint"
            );
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);
        }
    }
}
