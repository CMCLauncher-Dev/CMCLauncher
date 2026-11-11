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

    async Task<Control> DownloadsPage(CancellationToken ct)
    {
        var body = Stack();
        var snapshots = new CheckBox { Content = Ui("Show snapshots") };
        var april = new CheckBox { Content = Ui("Show April Fools") };
        var search = Input("", "Search game versions");
        var versions = Stack();
        body.Children.Add(Row(snapshots, april));
        body.Children.Add(search);
        body.Children.Add(versions);
        async Task Draw()
        {
            versions.Children.Clear();
            gameCatalog ??= (await session.Core.VersionCatalog(ct)).ToArray();
            foreach (
                var version in gameCatalog
                    .Where(v =>
                        (
                            v.Type == "release" && !GameRelease.IsAprilFoolsVersion(v.Id)
                            || snapshots.IsChecked == true
                                && v.Type != "release"
                                && !GameRelease.IsAprilFoolsVersion(v.Id)
                            || april.IsChecked == true && GameRelease.IsAprilFoolsVersion(v.Id)
                        )
                        && (
                            string.IsNullOrWhiteSpace(search.Text)
                            || v.Id.Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .Take(100)
            )
                versions.Children.Add(
                    Card(
                        Row(
                            T(version.Id, 16, true),
                            Btn("Install options", () => GameInstallOptions(version.Id))
                        )
                    )
                );
        }
        snapshots.IsCheckedChanged += async (_, _) =>
        {
            try
            {
                await Draw();
            }
            catch (OperationCanceledException) { }
        };
        april.IsCheckedChanged += async (_, _) =>
        {
            try
            {
                await Draw();
            }
            catch (OperationCanceledException) { }
        };
        search.TextChanged += async (_, _) =>
        {
            try
            {
                await Draw();
            }
            catch (OperationCanceledException) { }
        };
        await Draw();
        return body;
    }

    Task GameInstallOptions(string gameVersion)
    {
        pageTitle.Text = "Minecraft " + gameVersion;
        var name = Input(gameVersion);
        var loader = Choice(new[] { "vanilla", "fabric", "quilt", "forge", "neoforge" });
        var builds = Choice(Array.Empty<string>());
        long loaderRevision = 0;
        var body = Stack(
            Row(Btn("Back", () => Navigate("downloads"))),
            Field("Instance name", name),
            Field("Loader", loader),
            Field("Loader version", builds)
        );
        loader.SelectionChanged += async (_, _) =>
        {
            var request = ++loaderRevision;
            builds.ItemsSource = Array.Empty<string>();
            try
            {
                if (loader.SelectedItem is not string selected || selected == "vanilla")
                {
                    builds.ItemsSource = Array.Empty<string>();
                    return;
                }
                var values = await session.Core.LoaderBuilds(
                    gameVersion,
                    selected,
                    navigationRequest?.Token ?? default
                );
                if (request != loaderRevision)
                    return;
                builds.ItemsSource = values;
                builds.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
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
        var game = Choice(new[] { "" }, state.Filters.GameVersion);
        var loader = Choice(
            new[] { "", "fabric", "forge", "neoforge", "quilt" },
            state.Filters.Loader
        );
        var sort = Choice(new[] { "", "downloads", "newest", "updated" }, state.Filters.Sort);
        var category = Choice(new[] { "" });
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
                                await Search(0);
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
        var searchButton = Btn("Search", () => Search(0), true);
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
                Loader: loader.SelectedItem?.ToString() ?? "",
                Categories: category.SelectedItem is CommunityCategory c ? [c.Name] : [],
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
                var labels = Stack(
                    T(item.Name, 17, true),
                    description,
                    T(item.Source == "curseforge" ? "CurseForge" : "Modrinth", 11),
                    Btn("View details", () => CommunityDetails(item, type))
                );
                var card = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("110,*"),
                    ColumnSpacing = 12,
                };
                card.Children.Add(Preview(item.IconUrl));
                Grid.SetColumn(labels, 1);
                card.Children.Add(labels);
                list.Children.Add(Card(card));
            }
            var totalPages = Math.Max(1, (state.Result.TotalHits + 15) / 16);
            pageLabel.Text = $"{state.Page + 1}/{totalPages}";
            jump.Text = (state.Page + 1).ToString();
            previous.IsEnabled = state.Page > 0;
            next.IsEnabled = state.Result.HasNext;
            status.Text = state.Result.Warning ?? $"{state.Result.TotalHits:N0}";
        }
        async Task Search(int page)
        {
            if (loading || page < 0)
                return;
            if (state.Result != null && page > 0 && page * 16 >= state.Result.TotalHits)
                return;
            loading = true;
            source.IsEnabled = false;
            status.Text = Ui("Loading…");
            try
            {
                var filters = Read();
                var text = query.Text?.Trim() ?? "";
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
                RefreshVersions();
                category.ItemsSource = new object[] { "" }.Concat(
                    catalog.Categories.Where(c => c.ProjectType == type)
                );
                category.SelectedItem = catalog.Categories.FirstOrDefault(c =>
                    state.Filters.Categories?.Contains(c.Name) == true
                );
                if (category.SelectedIndex < 0)
                    category.SelectedIndex = 0;
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
                await Search(0);
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
        var project = await session.Community.ProjectDetails(item.Id, ct);
        CommunityVersionPage? firstPage =
            item.Source == "curseforge"
                ? await session.CurseForge.ProjectVersionPage(item.Id, 0, 50, ct)
                : null;
        var versions =
            firstPage?.Versions.ToList() ?? await session.Modrinth.ProjectVersions(item.Id, ct);
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
                                Btn(
                                    "Show dependencies",
                                    async () =>
                                    {
                                        var dependencies = await session.Community.Dependencies(
                                            item.Id,
                                            version.Id,
                                            ct
                                        );
                                        await Dialog(
                                            "Dependencies",
                                            Stack(
                                                dependencies
                                                    .Select(d =>
                                                        (Control)T(
                                                            d.Name
                                                                + " · "
                                                                + d.Relation
                                                                + " · "
                                                                + d.Version
                                                        )
                                                    )
                                                    .ToArray()
                                            ),
                                            false
                                        );
                                    }
                                ),
                                Btn("Download file", () => DownloadResourceFile(item, version)),
                                Btn("Install", () => InstallResource(item, version), true)
                            )
                        )
                    )
                );
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
