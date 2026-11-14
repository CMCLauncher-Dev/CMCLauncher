using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    GameRelease[]? gameCatalog;

    string gameReleaseCategory = "正式版";
    string gameVersionQuery = "";

    async Task<Control> DownloadsPage(CancellationToken ct)
    {
        var search = Input(gameVersionQuery, "Find a version, e.g. 1.21");
        var categories = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 6,
        };
        var versions = new ListBox
        {
            MinHeight = 240,
            MaxHeight = 400,
            Name = "GameVersionList",
        };
        var status = T("Fetching official version list…", 12);
        var empty = T("No matching versions. Try a different version number or release type.", 12);
        empty.IsVisible = false;
        void Draw()
        {
            var values = (gameCatalog ?? [])
                .Where(v =>
                    v.Category == gameReleaseCategory
                    && v.Id.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)
                )
                .Select(v => v.Id)
                .ToArray();
            versions.ItemsSource = values;
            status.Text = Ui(gameReleaseCategory) + " · " + values.Length + " " + Ui("versions");
            empty.IsVisible = values.Length == 0;
        }
        foreach (var category in new[] { "正式版", "快照版", "愚人节版" })
        {
            var button = new RadioButton
            {
                Content = Ui(category),
                GroupName = "MinecraftVersionCategory",
                IsChecked = category == gameReleaseCategory,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                Padding = new Thickness(6, 7),
            };
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    gameReleaseCategory = category;
                    Draw();
                }
            };
            Grid.SetColumn(button, categories.Children.Count);
            categories.Children.Add(button);
        }
        search.TextChanged += (_, _) =>
        {
            gameVersionQuery = search.Text ?? "";
            Draw();
        };
        versions.SelectionChanged += async (_, _) =>
        {
            if (versions.SelectedItem is string version)
                await GameInstallOptions(version);
        };
        var body = Card(Stack(categories, search, versions, empty, status));
        if (gameCatalog == null)
            gameCatalog = (await session.Core.VersionCatalog(ct)).ToArray();
        ct.ThrowIfCancellationRequested();
        Draw();
        return body;
    }

    Task GameInstallOptions(string gameVersion)
    {
        pageTitle.Text = Ui("Install options") + " · Minecraft " + gameVersion;
        var name = Input(gameVersion);
        var loader = Choice(new[] { "vanilla", "fabric", "forge", "neoforge", "quilt" });
        var builds = Choice(Array.Empty<string>());
        builds.IsEnabled = false;
        var loaderStatus = T("", 12);
        var buildField = Field("Loader version", builds);
        buildField.IsVisible = false;
        long loaderRevision = 0;
        Button retry = null!;
        retry = Btn("Retry loader lookup", async () => await LoadBuilds());
        retry.IsVisible = false;
        var body = Stack(
            Row(Btn("Back", () => Navigate("downloads"))),
            Field("Instance name", name),
            Field("Loader", loader),
            buildField,
            loaderStatus
        );
        async Task LoadBuilds()
        {
            var request = ++loaderRevision;
            builds.ItemsSource = Array.Empty<string>();
            builds.IsEnabled = false;
            loaderStatus.Text = "";
            retry.IsVisible = false;
            buildField.IsVisible = loader.SelectedItem?.ToString() != "vanilla";
            if (loader.SelectedItem is not string selected || selected == "vanilla")
                return;
            loaderStatus.Text = Ui("Loading…");
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(
                navigationRequest?.Token ?? default
            );
            lookup.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var values = await session.Core.LoaderBuilds(gameVersion, selected, lookup.Token);
                if (request != loaderRevision || navigationRequest?.IsCancellationRequested == true)
                    return;
                builds.ItemsSource = values;
                builds.SelectedIndex = 0;
                builds.IsEnabled = values.Count > 0;
                retry.IsVisible = values.Count == 0;
                loaderStatus.Text =
                    values.Count > 0 ? "" : Ui("No compatible loader versions found.");
            }
            catch (Exception ex)
            {
                if (request != loaderRevision || navigationRequest?.IsCancellationRequested == true)
                    return;
                retry.IsVisible = true;
                loaderStatus.Text = Ui(
                    ex is OperationCanceledException
                        ? "Loader lookup timed out. Select the loader again to retry."
                        : "Failed to load loader versions."
                );
                session.Log("ERROR", ex.ToString());
            }
        }
        loader.SelectionChanged += async (_, _) => await LoadBuilds();
        body.Children.Add(retry);
        body.Children.Add(
            Btn(
                "Install",
                async () =>
                {
                    var instance = new Instance
                    {
                        Name = name.Text?.Trim() ?? "",
                        GameVersion = gameVersion,
                        Loader = loader.SelectedItem?.ToString() ?? "vanilla",
                    };
                    session.Store.EnsureUniqueInstanceName(instance);
                    if (instance.Loader != "vanilla" && builds.SelectedItem == null)
                        throw new IOException(Ui("Select a loader version."));
                    await session.Run(
                        "Install game",
                        async ct =>
                        {
                            await session.Core.InstallNewGame(
                                instance,
                                builds.SelectedItem?.ToString() ?? "",
                                ct
                            );
                            await session.AddInstance(instance);
                        }
                    );
                    await Navigate("instances");
                },
                true
            )
        );
        pageContent.Content = body;
        return Task.CompletedTask;
    }

    sealed class BrowseState
    {
        public string Query = "",
            Source = "modrinth";
        public int Page;
        public CommunitySearchFilters Filters = new();
        public CommunitySearchPage? Result;
    }

    readonly Dictionary<string, BrowseState> browseStates = new();
    readonly Dictionary<(string, string), CommunityFilterCatalog> filterCatalogs = new();

    Control CommunityPage(string type)
    {
        Button previous = null!,
            next = null!;
        var loading = false;
        var state = browseStates.GetValueOrDefault(type);
        if (state == null)
            browseStates[type] = state = new BrowseState
            {
                Source = type == "world" ? "curseforge" : "modrinth",
            };
        var source = Choice(
            type == "world" ? new[] { "curseforge" } : new[] { "modrinth", "curseforge", "all" },
            state.Source
        );
        source.Tag = "community-source";
        var game = Choice(new[] { "" }, state.Filters.GameVersion);
        var loader = Choice(
            new[] { "", "fabric", "forge", "neoforge", "quilt" },
            state.Filters.Loader
        );
        loader.Tag = "community-filter-loader";
        var sort = Choice(new[] { "", "downloads", "newest", "updated" }, state.Filters.Sort);
        var category = Choice(new[] { "" });
        var categoryChoices = new List<ComboBox> { category };
        var environment = Choice(
            new[] { "", "client", "server", "both" },
            state.Filters.Environment
        );
        var author = Input(state.Filters.Author);
        var title = Input(state.Filters.Title);
        var project = Input(state.Filters.ProjectId);
        var license = Input(state.Filters.License);
        var openSource = Choice(new[] { "", "true", "false" }, state.Filters.OpenSource);
        var minimumDownloads = Input(state.Filters.MinDownloads);
        var minimumFollows = Input(state.Filters.MinFollows);
        var created = Input(state.Filters.CreatedAfter, "YYYY-MM-DD");
        var updated = Input(state.Filters.UpdatedAfter, "YYYY-MM-DD");
        var snapshots = new CheckBox
        {
            Content = Ui("Show snapshots"),
            IsChecked = state.Filters.ShowSnapshots,
        };
        var april = new CheckBox
        {
            Content = Ui("Show April Fools"),
            IsChecked = state.Filters.ShowAprilFools,
        };
        var disclosureChoices = new[]
        {
            "explicit",
            "advertising",
            "gambling",
            "social",
            "education",
        }
            .Select(name => new CheckBox
            {
                Content = name,
                Tag = name,
                IsChecked = state.Filters.Disclosures?.Contains(name) == true,
            })
            .ToArray();
        var query = Input(state.Query, "Search community content");
        var list = Stack();
        var status = T("");
        var pageLabel = T("");
        var jump = Input("1");
        jump.Width = 70;
        var body = Stack();
        if (type != "world")
            body.Children.Add(Field("Source", source));
        var basic = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                type is "mod" or "modpack" or "shader" ? "*,*,*,*" : "*,*,*"
            ),
            ColumnSpacing = 10,
        };
        var fields = new List<Control> { Field("Game version", game) };
        if (type is "mod" or "modpack" or "shader")
            fields.Add(Field("Loader", loader));
        fields.Add(Field("Sort", sort));
        fields.Add(Field("Category", category));
        for (var i = 0; i < fields.Count; i++)
        {
            Grid.SetColumn(fields[i], i);
            basic.Children.Add(fields[i]);
        }
        body.Children.Add(basic);
        var more = Btn(
            "More filters",
            () =>
            {
                var advanced = Stack(
                    T("More filters", 20, true),
                    Field("Environment", environment),
                    Field("Open source", openSource),
                    Field("License", license),
                    Field("Author", author),
                    Field("Project title", title),
                    Field("Project ID", project),
                    Field("Minimum downloads", minimumDownloads),
                    Field("Minimum followers", minimumFollows),
                    Field("Created after", created),
                    Field("Updated after", updated)
                );
                foreach (var check in disclosureChoices)
                    advanced.Children.Add(check);
                advanced.Children.Add(
                    Row(
                        Btn(
                            "Apply",
                            async () =>
                            {
                                drawer.IsPaneOpen = false;
                                await Search(0, true);
                            },
                            true
                        ),
                        Btn(
                            "Reset",
                            () =>
                            {
                                environment.SelectedIndex = 0;
                                openSource.SelectedIndex = 0;
                                foreach (
                                    var input in new[]
                                    {
                                        license,
                                        author,
                                        title,
                                        project,
                                        minimumDownloads,
                                        minimumFollows,
                                        created,
                                        updated,
                                    }
                                )
                                    input.Text = "";
                                foreach (var check in disclosureChoices)
                                    check.IsChecked = false;
                                return Task.CompletedTask;
                            }
                        ),
                        Btn(
                            "Close",
                            () =>
                            {
                                drawer.IsPaneOpen = false;
                                return Task.CompletedTask;
                            }
                        )
                    )
                );
                advanced.Margin = new Thickness(18);
                drawer.OpenPaneLength = 540;
                drawer.Pane = new ScrollViewer { Content = advanced };
                drawer.IsPaneOpen = true;
                return Task.CompletedTask;
            }
        );
        var options = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 10,
        };
        options.Children.Add(snapshots);
        Grid.SetColumn(april, 1);
        options.Children.Add(april);
        Grid.SetColumn(more, 3);
        options.Children.Add(more);
        body.Children.Add(options);
        var searchButton = Btn("Search", () => Search(0, true), true);
        var searchRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
        };
        searchRow.Children.Add(query);
        Grid.SetColumn(searchButton, 1);
        searchRow.Children.Add(searchButton);
        body.Children.Add(searchRow);
        body.Children.Add(status);
        body.Children.Add(list);
        previous = Btn("Previous", () => Search(state.Page - 1));
        next = Btn("Next", () => Search(state.Page + 1));
        body.Children.Add(
            Row(
                previous,
                pageLabel,
                jump,
                Btn(
                    "Go",
                    () =>
                        int.TryParse(jump.Text, out var number) && number > 0
                            ? Search(number - 1)
                            : throw new IOException("Enter a valid page number.")
                ),
                next
            )
        );

        CommunityFilterCatalog? catalog = null;
        CommunitySearchFilters Read() =>
            new(
                GameVersion: game.SelectedItem?.ToString() ?? "",
                Loader: loader.IsEnabled ? loader.SelectedItem?.ToString() ?? "" : "",
                Categories: categoryChoices
                    .Where(choice => choice.IsEnabled)
                    .Select(choice => choice.SelectedItem)
                    .OfType<CommunityCategory>()
                    .Select(c => c.Name)
                    .ToArray(),
                Environment: source.SelectedItem?.ToString() == "modrinth"
                    ? environment.SelectedItem?.ToString() ?? ""
                    : "",
                License: source.SelectedItem?.ToString() == "modrinth" ? license.Text ?? "" : "",
                OpenSource: source.SelectedItem?.ToString() == "modrinth"
                    ? openSource.SelectedItem?.ToString() ?? ""
                    : "",
                Sort: sort.SelectedItem?.ToString() ?? "",
                Author: source.SelectedItem?.ToString() == "modrinth" ? author.Text ?? "" : "",
                Title: source.SelectedItem?.ToString() == "modrinth" ? title.Text ?? "" : "",
                ProjectId: source.SelectedItem?.ToString() == "modrinth" ? project.Text ?? "" : "",
                MinDownloads: source.SelectedItem?.ToString() == "modrinth"
                    ? minimumDownloads.Text ?? ""
                    : "",
                MinFollows: source.SelectedItem?.ToString() == "modrinth"
                    ? minimumFollows.Text ?? ""
                    : "",
                CreatedAfter: source.SelectedItem?.ToString() == "modrinth"
                    ? created.Text ?? ""
                    : "",
                UpdatedAfter: source.SelectedItem?.ToString() == "modrinth"
                    ? updated.Text ?? ""
                    : "",
                Disclosures: source.SelectedItem?.ToString() == "modrinth"
                    ? disclosureChoices
                        .Where(c => c.IsChecked == true)
                        .Select(c => c.Tag!.ToString()!)
                        .ToArray()
                    : [],
                ShowSnapshots: snapshots.IsChecked == true,
                ShowAprilFools: april.IsChecked == true,
                Source: source.SelectedItem?.ToString() ?? "modrinth"
            );
        void Draw()
        {
            list.Children.Clear();
            if (state.Result == null)
                return;
            foreach (var item in state.Result.Hits)
            {
                var description = T(
                    System.Text.RegularExpressions.Regex.Replace(item.Description, @"\s+", " "),
                    12
                );
                description.MaxLines = 1;
                description.TextTrimming = TextTrimming.CharacterEllipsis;
                var title = T(item.Name, 16, true);
                title.MaxLines = 1;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                var labels = Stack(
                    title,
                    description,
                    T(item.Source == "curseforge" ? "CurseForge" : "Modrinth", 11)
                );
                labels.Spacing = 4;
                labels.VerticalAlignment = VerticalAlignment.Center;
                var card = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("64,*,Auto"),
                    ColumnSpacing = 12,
                };
                var icon = Preview(item.IconUrl);
                icon.Width = icon.Height = 64;
                card.Children.Add(icon);
                Grid.SetColumn(labels, 1);
                card.Children.Add(labels);
                var details = Btn("View details", () => CommunityDetails(item, type));
                details.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(details, 2);
                card.Children.Add(details);
                list.Children.Add(Card(card));
                _ = AutoTranslateCardAsync(item.Name, item.Description, title, description);
            }
            var totalPages = Math.Max(1, (state.Result.TotalHits + 15) / 16);
            pageLabel.Text = $"{state.Page + 1}/{totalPages}";
            jump.Text = (state.Page + 1).ToString();
            previous.IsEnabled = state.Page > 0;
            next.IsEnabled = state.Result.HasNext;
            status.Text = state.Result.Warning ?? $"{state.Result.TotalHits:N0}";
        }
        // 搜索结果加载后自动把标题/摘要翻译为界面语言（原文写入 Tag 供恢复）。
        async Task AutoTranslateCardAsync(
            string originalTitle,
            string originalDescription,
            TextBlock title,
            TextBlock description
        )
        {
            try
            {
                var settings = session.Store.Config.Settings;
                if (!settings.ModTranslationEnabled)
                    return;
                var target = settings.LauncherLanguage;
                if (ModTranslation.ServiceLanguage(target) == "en")
                    return;
                using var request =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        navigationRequest?.Token ?? default
                    );
                request.CancelAfter(TimeSpan.FromSeconds(30));
                var titleTask = ModTranslation.TranslateAsync(
                    session.Core.Http,
                    originalTitle,
                    "en_us",
                    target,
                    request.Token
                );
                var descTask = ModTranslation.TranslateAsync(
                    session.Core.Http,
                    originalDescription,
                    "en_us",
                    target,
                    request.Token
                );
                title.Text = await titleTask;
                description.Text = await descTask;
                title.Tag = originalTitle;
                description.Tag = originalDescription;
            }
            catch
            {
                // 翻译失败或已取消时保留原文。
            }
        }
        async Task Search(int page, bool submit = false)
        {
            if (loading || page < 0)
                return;
            if (!submit && state.Result != null && page > 0 && page * 16 >= state.Result.TotalHits)
                return;
            loading = true;
            source.IsEnabled = false;
            status.Text = Ui("Loading…");
            try
            {
                var filters = submit ? Read() : state.Filters;
                var text = submit ? query.Text?.Trim() ?? "" : state.Query;
                var response = await session.Community.SearchPage(
                    text,
                    type,
                    navigationRequest?.Token ?? default,
                    page * 16,
                    16,
                    filters
                );
                state.Query = text;
                state.Filters = filters;
                state.Source = filters.Source;
                state.Page = page;
                state.Result = response;
                Draw();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
            finally
            {
                loading = false;
                source.IsEnabled = true;
            }
        }
        void RefreshVersions()
        {
            var selected = game.SelectedItem?.ToString() ?? "";
            game.ItemsSource = new[] { "" }
                .Concat(
                    catalog
                        ?.GameVersions.Where(v =>
                            v.Type == "release" && !GameRelease.IsAprilFoolsVersion(v.Id)
                            || snapshots.IsChecked == true
                                && v.Type != "release"
                                && !GameRelease.IsAprilFoolsVersion(v.Id)
                            || april.IsChecked == true && GameRelease.IsAprilFoolsVersion(v.Id)
                        )
                        .Select(v => v.Id) ?? []
                )
                .ToArray();
            game.SelectedItem = selected;
            if (game.SelectedIndex < 0)
                game.SelectedIndex = 0;
        }
        async Task LoadFilters()
        {
            var provider = source.SelectedItem?.ToString() ?? "modrinth";
            more.IsEnabled = provider == "modrinth";
            var selectedLoader =
                catalog == null ? state.Filters.Loader : loader.SelectedItem?.ToString() ?? "";
            loader.IsEnabled = false;
            foreach (var choice in categoryChoices)
                choice.IsEnabled = false;
            try
            {
                if (!filterCatalogs.TryGetValue((provider, type), out var loaded))
                    filterCatalogs[(provider, type)] = loaded =
                        provider == "curseforge"
                            ? await session.CurseForge.FilterCatalog(
                                type,
                                navigationRequest?.Token ?? default
                            )
                            : await session.Modrinth.FilterCatalog(
                                navigationRequest?.Token ?? default
                            );
                if (source.SelectedItem?.ToString() != provider)
                    return;
                catalog = loaded;
                loader.ItemsSource = new[] { "" }
                    .Concat(
                        catalog
                            .Loaders.Where(entry => entry.ProjectTypes.Contains(type))
                            .Where(entry =>
                                provider == "modrinth"
                                || entry.Name is "forge" or "fabric" or "quilt" or "neoforge"
                            )
                            .Select(entry => entry.Name)
                    )
                    .ToArray();
                loader.SelectedItem = selectedLoader;
                if (loader.SelectedIndex < 0)
                    loader.SelectedIndex = 0;
                loader.IsEnabled = true;
                RefreshVersions();
                // Provider categories include independent feature/resolution groups.
                // Rebuild columns together so every applicable filter stays in one row.
                var applicable = catalog.Categories.Where(c => c.ProjectType == type).ToArray();
                if (type == "datapack" && applicable.Length == 0)
                    applicable = catalog.Categories.Where(c => c.ProjectType == "mod").ToArray();
                while (fields.Count > (type is "mod" or "modpack" or "shader" ? 3 : 2))
                    fields.RemoveAt(fields.Count - 1);
                categoryChoices.Clear();
                foreach (var group in applicable.GroupBy(c => c.Header))
                {
                    if (provider == "all")
                        break;
                    var choice = Choice(new object[] { "" }.Concat(group));
                    choice.Tag = "community-filter-category-" + group.Key;
                    choice.SelectedItem =
                        group.FirstOrDefault(c =>
                            state.Filters.Categories?.Contains(c.Name) == true
                        ) ?? (object)"";
                    categoryChoices.Add(choice);
                    var label = group.Key switch
                    {
                        "categories" => "Category",
                        "features" => "Features",
                        "resolutions" => "Resolution",
                        "performance impact" => "Performance impact",
                        _ => group.Key,
                    };
                    fields.Add(Field(label, choice));
                }
                if (categoryChoices.Count == 0)
                {
                    category.ItemsSource = new[] { "" };
                    category.SelectedIndex = 0;
                    category.IsEnabled = false;
                    categoryChoices.Add(category);
                    fields.Add(Field("Category", category));
                }
                basic.Children.Clear();
                basic.ColumnDefinitions.Clear();
                for (var i = 0; i < fields.Count; i++)
                {
                    basic.ColumnDefinitions.Add(
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                    );
                    Grid.SetColumn(fields[i], i);
                    basic.Children.Add(fields[i]);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }
        source.SelectionChanged += async (_, _) => await LoadFilters();
        snapshots.IsCheckedChanged += (_, _) => RefreshVersions();
        april.IsCheckedChanged += (_, _) => RefreshVersions();
        query.KeyDown += async (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                await Search(0, true);
            }
        };
        Draw();
        if (!Verifying)
            _ = LoadFilters();
        return body;
    }

    async Task CommunityDetails(SearchResult item, string returnRoute)
    {
        var revision = ++generation;
        pageTitle.Text = item.Name;
        var host = Stack(Btn("Back", () => Navigate(returnRoute)), T("Loading…"));
        pageContent.Content = host;
        var ct = navigationRequest?.Token ?? default;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        request.CancelAfter(TimeSpan.FromSeconds(45));
        var projectTask = Task.Run(
            () => session.Community.ProjectDetails(item.Id, request.Token),
            request.Token
        );
        var versionTask = Task.Run(
            async () =>
            {
                if (item.Source == "curseforge")
                {
                    var page = await session.CurseForge.ProjectVersionPage(
                        item.Id,
                        0,
                        50,
                        request.Token
                    );
                    return (Page: (CommunityVersionPage?)page, Versions: page.Versions.ToList());
                }
                return (
                    Page: (CommunityVersionPage?)null,
                    Versions: await session.Modrinth.ProjectVersions(item.Id, request.Token)
                );
            },
            request.Token
        );
        var pending = Task.WhenAll(projectTask, (Task)versionTask);
        try
        {
            await pending.WaitAsync(request.Token);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested || revision != generation)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            throw new IOException(Ui("The request timed out. Please try again."));
        }
        finally
        {
            _ = pending.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
        var project = await projectTask;
        var (firstPage, versions) = await versionTask;
        if (revision != generation)
            return;
        host.Children.Clear();
        host.Children.Add(Btn("Back", () => Navigate(returnRoute)));
        host.Children.Add(
            Card(
                Stack(
                    T(project.Name, 23, true),
                    T(project.Authors),
                    T(project.Description),
                    Btn(
                        "Open website",
                        () =>
                        {
                            Open(project.WebsiteUrl);
                            return Task.CompletedTask;
                        }
                    )
                )
            )
        );
        host.Children.Add(
            new Expander
            {
                Header = Ui("Description"),
                Content = MarkdownContent(project.Body),
                IsExpanded = session.Store.Config.Settings.ExpandProjectIntroduction,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            }
        );
        var shown = Stack();
        host.Children.Add(shown);
        var offset = firstPage?.NextOffset ?? Math.Min(50, versions.Count);
        var hasMore = firstPage?.HasNext ?? versions.Count > 50;
        void Append(IEnumerable<CommunityVersion> added)
        {
            foreach (var version in added)
            {
                var dependencies = Stack();
                dependencies.IsVisible = false;
                var showDependencies = Btn(
                    "Show dependencies",
                    async () =>
                    {
                        dependencies.IsVisible = !dependencies.IsVisible;
                        if (!dependencies.IsVisible)
                            return;
                        dependencies.Children.Clear();
                        dependencies.Children.Add(T("Loading dependencies…", 12));
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        request.CancelAfter(TimeSpan.FromSeconds(30));
                        try
                        {
                            var values = await session.Community.Dependencies(
                                item.Id,
                                version.Id,
                                request.Token
                            );
                            if (ct.IsCancellationRequested)
                                return;
                            dependencies.Children.Clear();
                            if (values.Count == 0)
                                dependencies.Children.Add(T("No dependencies declared.", 12));
                            foreach (var value in values)
                                dependencies.Children.Add(
                                    T(
                                        value.Name
                                            + " · "
                                            + Ui(value.Relation)
                                            + (
                                                value.Version.Length > 0
                                                    ? " · " + value.Version
                                                    : ""
                                            ),
                                        12
                                    )
                                );
                        }
                        catch (Exception ex)
                        {
                            if (ct.IsCancellationRequested)
                                return;
                            dependencies.Children.Clear();
                            dependencies.Children.Add(
                                T(Ui("Could not load dependencies: ") + ex.Message, 12)
                            );
                        }
                    }
                );
                shown.Children.Add(
                    Card(
                        Stack(
                            T(version.Name, 15, true),
                            T(
                                string.Join(", ", version.GameVersions)
                                    + " · "
                                    + string.Join(", ", version.Loaders),
                                12
                            ),
                            Row(
                                showDependencies,
                                Btn("Download file", () => DownloadResourceFile(item, version)),
                                Btn("Install", () => InstallResource(item, version), true)
                            ),
                            dependencies
                        )
                    )
                );
            }
        }
        Append(versions.Take(50));
        Button moreButton = null!;
        moreButton = Btn(
            "Load more versions",
            async () =>
            {
                if (!hasMore)
                    return;
                if (item.Source == "curseforge")
                {
                    var more = await session.CurseForge.ProjectVersionPage(item.Id, offset, 50, ct);
                    offset = more.NextOffset;
                    hasMore = more.HasNext;
                    Append(more.Versions);
                }
                else
                {
                    Append(versions.Skip(offset).Take(50));
                    offset += 50;
                    hasMore = offset < versions.Count;
                }
                moreButton.IsVisible = hasMore;
            }
        );
        moreButton.IsVisible = hasMore;
        host.Children.Add(moreButton);
    }

    async Task DownloadResourceFile(SearchResult item, CommunityVersion version)
    {
        await session.Run(
            "Download file",
            async ct =>
            {
                var stage = Path.Combine(
                    session.Store.Root,
                    "downloads",
                    Guid.NewGuid().ToString("N")
                );
                try
                {
                    var file =
                        item.Source == "curseforge"
                            ? await session.CurseForge.DownloadProjectFile(
                                item.Id,
                                version.Id,
                                stage,
                                ct
                            )
                            : await session.Modrinth.DownloadProjectFile(
                                item.Id,
                                version.Id,
                                stage,
                                ct
                            );
                    var target = await SaveFile(Path.GetFileName(file), Path.GetExtension(file));
                    if (target != null)
                        File.Copy(file, target, true);
                }
                finally
                {
                    if (Directory.Exists(stage))
                        Directory.Delete(stage, true);
                }
            }
        );
    }

    bool Compatible(Instance instance, string type, CommunityVersion version) =>
        version.GameVersions.Contains(instance.GameVersion)
        && (
            type != "mod"
            || ContentManager.ModInstallUnsupportedReason(instance) == null
                && version.Loaders.Contains(instance.Loader)
        )
        && (
            type != "shader"
            || ContentManager.HasShaderSupport(instance, session.Store.GameDirectory(instance))
        );

    async Task InstallResource(SearchResult item, CommunityVersion version)
    {
        session.EnsureFilesIdle();
        if (item.Type == "modpack")
        {
            await session.Run(
                "Install modpack",
                async ct =>
                {
                    var instance =
                        item.Source == "curseforge"
                            ? await session.CurseForge.InstallPack(
                                item.Id,
                                version.Id,
                                ct,
                                ResolvePackName
                            )
                            : await session.Modrinth.InstallProjectPack(
                                item.Id,
                                ct,
                                version.Id,
                                ResolvePackName
                            );
                    await session.AddInstance(instance);
                }
            );
            return;
        }
        var choices = session
            .Store.Config.Instances.Where(i => Compatible(i, item.Type, version))
            .ToArray();
        if (choices.Length == 0)
            throw new IOException(
                Ui(
                    "No compatible install location. Please download or configure a game version that supports this resource."
                )
            );
        var target = Choice(
            choices,
            choices.Contains(session.Selected) ? session.Selected : choices[0]
        );
        var worlds = Choice(Array.Empty<string>());
        void UpdateWorlds()
        {
            worlds.ItemsSource = target.SelectedItem is Instance instance
                ? ContentManager.Worlds(session.Store.GameDirectory(instance))
                : [];
            worlds.SelectedIndex = 0;
        }
        target.SelectionChanged += (_, _) => UpdateWorlds();
        UpdateWorlds();
        var content = Stack(Field("Instance", target));
        if (item.Type == "datapack")
            content.Children.Add(Field("World", worlds));
        if (
            !await Dialog("Choose install location", content, true)
            || target.SelectedItem is not Instance selected
        )
            return;
        await session.Run(
            "Install resource",
            async ct =>
            {
                if (item.Source == "curseforge")
                    await session.CurseForge.InstallContent(
                        selected,
                        item.Id,
                        version.Id,
                        item.Type,
                        worlds.SelectedItem?.ToString(),
                        ct
                    );
                else if (item.Type == "mod")
                    await session.Modrinth.InstallMod(
                        selected,
                        item.Id,
                        ct,
                        selectedVersionId: version.Id
                    );
                else
                    await session.Modrinth.InstallContent(
                        selected,
                        item.Id,
                        item.Type,
                        worlds.SelectedItem?.ToString(),
                        ct,
                        version.Id
                    );
            }
        );
    }
}