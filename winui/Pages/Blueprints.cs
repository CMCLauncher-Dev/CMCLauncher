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

    HttpClient BlueprintHttp() =>
        new(
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
            ItemsSource = new[] { "Schematicraft", Ui("MCS Blueprint Library") },
            SelectedIndex = blueprintSource == "mcschematic" ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        source.SelectionChanged += async (_, _) =>
        {
            if (source.SelectedIndex < 0)
                return;
            blueprintSource = source.SelectedIndex == 1 ? "mcschematic" : "schematicraft";
            blueprintPage = 1;
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
        PageBody.Children.Add(
            Text(
                "Choose a blueprint format your mod supports. Installing a file does not convert it or install required mods.",
                12
            )
        );
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
                var result = await new BlueprintService(http).Search(
                    blueprintQuery,
                    blueprintPage,
                    request.Token,
                    selectedSource
                );
                if (generation != renderVersion || current != revision)
                    return;
                status.Text = result.Items.Count == 0 ? Ui("No blueprints found.") : "";
                foreach (var entry in result.Items)
                {
                    var body = new StackPanel { Spacing = 6 };
                    body.Children.Add(Text(entry.Title, 15, true));
                    body.Children.Add(Text(entry.Author, 12));
                    var description = Text(
                        entry.Description.Replace('\r', ' ').Replace('\n', ' '),
                        12
                    );
                    description.TextWrapping = TextWrapping.NoWrap;
                    description.TextTrimming = TextTrimming.CharacterEllipsis;
                    body.Children.Add(description);
                    body.Children.Add(
                        new HyperlinkButton
                        {
                            Content = Ui("View on website"),
                            NavigateUri = new Uri(entry.Website),
                        }
                    );
                    body.Children.Add(
                        Buttons(
                            Action(
                                "Download file",
                                async (_, _) =>
                                    await DownloadBlueprint(entry, false, selectedSource)
                            ),
                            Action(
                                "Install blueprint",
                                async (_, _) =>
                                    await DownloadBlueprint(entry, true, selectedSource, target)
                            )
                        )
                    );
                    if (
                        supported.Length == 0
                        && body.Children.LastOrDefault() is StackPanel buttons
                    )
                    {
                        var installButton = buttons.Children.OfType<Button>().Last();
                        installButton.IsEnabled = false;
                        taskButtons.Remove(installButton);
                        ToolTipService.SetToolTip(installButton, Ui("No compatible instances"));
                    }
                    var preview = new Grid
                    {
                        Width = 120,
                        Height = 90,
                        VerticalAlignment = VerticalAlignment.Top,
                    };
                    preview.Children.Add(
                        new FontIcon
                        {
                            Glyph = "\uE8A5",
                            FontSize = 28,
                            Opacity = .5,
                        }
                    );
                    if (
                        Uri.TryCreate(entry.ImageUrl, UriKind.Absolute, out var imageUri)
                        && imageUri.Scheme == "https"
                        && imageUri.UserInfo.Length == 0
                    )
                    {
                        var image = new Image
                        {
                            Source = new BitmapImage(imageUri) { DecodePixelWidth = 240 },
                            Stretch = Stretch.Uniform,
                            Width = 120,
                            Height = 90,
                        };
                        image.ImageFailed += (_, _) => image.Visibility = Visibility.Collapsed;
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                            image,
                            entry.Title
                        );
                        preview.Children.Add(image);
                    }
                    var row = new Grid { ColumnSpacing = 12 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    );
                    row.Children.Add(preview);
                    Grid.SetColumn(body, 1);
                    row.Children.Add(body);
                    rows.Children.Add(CompactCard(row));
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
