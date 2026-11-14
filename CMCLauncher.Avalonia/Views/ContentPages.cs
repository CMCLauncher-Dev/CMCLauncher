using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CMCLauncher.Avalonia.ViewModels;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    string installedModQuery = "";
    int installedModFilter;

    async Task ContentPage(Instance instance, string kind, string? world = null)
    {
        var root = session.Store.GameDirectory(instance);
        var body = Stack(
            Row(
                Btn("Back", InstanceSettings),
                Btn(
                    "Import",
                    async () =>
                    {
                        session.EnsureFilesIdle();
                        var files = await StorageProvider.OpenFilePickerAsync(
                            new() { AllowMultiple = true }
                        );
                        await session.Run(
                            "Import content",
                            ct =>
                                Task.Run(
                                    () =>
                                        ContentManager.Import(
                                            root,
                                            kind,
                                            world,
                                            files.Select(f => f.TryGetLocalPath()).OfType<string>()
                                        ),
                                    ct
                                )
                        );
                        await ContentPage(instance, kind, world);
                    }
                ),
                Btn(
                    "Open folder",
                    () =>
                    {
                        var directory = ContentManager.Folder(root, kind, world);
                        Directory.CreateDirectory(directory);
                        Open(directory);
                        return Task.CompletedTask;
                    }
                )
            )
        );
        if (kind == "datapack" && world == null)
        {
            var worlds = Choice(ContentManager.Worlds(root));
            body.Children.Add(Field("World", worlds));
            body.Children.Add(
                Btn(
                    "Manage data packs",
                    async () =>
                    {
                        if (worlds.SelectedItem is string selected)
                            await ContentPage(instance, kind, selected);
                    }
                )
            );
            await ShowInstancePage(instance, kind, body);
            return;
        }
        if (kind == "mod")
            body.Children.Add(Btn("Check for updates", () => CheckModUpdates(instance)));
        var loading = T("Loading…", 12);
        body.Children.Add(loading);
        await ShowInstancePage(instance, kind, body);
        var viewRevision = instanceViewRevision;
        var items = await Task.Run(() => ContentManager.ListFiles(root, kind, world));
        if (viewRevision != instanceViewRevision || route != "instance-settings")
            return;
        body.Children.Remove(loading);
        if (items.Count == 0)
        {
            body.Children.Add(T("No content found."));
            return;
        }
        if (kind == "mod")
        {
            var savedView = instanceViewRevision;
            var missing = await Task.Run(() => ContentManager.MissingDependencies(root));
            if (savedView != instanceViewRevision || route != "instance-settings")
                return;
            if (missing.Count > 0)
            {
                var summary = Stack(
                    T(
                        Ui("Missing dependencies: ") + string.Join(", ", missing.Select(m => m.Missing).Distinct()),
                        12,
                        true
                    )
                );
                summary.Spacing = 2;
                body.Children.Add(summary);
            }
        }
        var list = new ListBox
        {
            ItemsSource = items,
            MaxHeight = 540,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        list.Styles.Add(
            new global::Avalonia.Styling.Style(selector => selector.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(
                        ListBoxItem.HorizontalContentAlignmentProperty,
                        HorizontalAlignment.Stretch
                    ),
                },
            }
        );
        if (kind == "mod")
        {
            var category = Choice(
                new[]
                {
                    "All Mods",
                    "Recognized",
                    "context.installedcontent.unrecognized",
                    "Fabric",
                    "Quilt",
                    "Forge",
                    "NeoForge",
                }
            );
            category.SelectedIndex = Math.Clamp(installedModFilter, 0, 6);
            var search = Input(installedModQuery, "Search names, versions, or files");
            search.Name = "InstalledModSearch";
            CancellationTokenSource? filterRequest = null;
            body.DetachedFromVisualTree += (_, _) => filterRequest?.Cancel();
            var status = T("", 12);
            async Task ApplyFilter()
            {
                filterRequest?.Cancel();
                filterRequest?.Dispose();
                filterRequest = new CancellationTokenSource();
                var token = filterRequest.Token;
                var filter = Math.Clamp(category.SelectedIndex, 0, 6);
                var query = (search.Text ?? "").Trim();
                installedModQuery = query;
                installedModFilter = filter;
                if (query.Length == 0 && filter == 0)
                {
                    list.ItemsSource = items;
                    status.Text = "";
                    return;
                }
                status.Text = Ui("Loading…");
                try
                {
                    using var gate = new SemaphoreSlim(4);
                    var resolved = await Task.WhenAll(
                        items.Select(async item =>
                        {
                            await gate.WaitAsync(token);
                            try
                            {
                                var result = await ContentInspectionCache
                                    .GetAsync(
                                        ContentManager.Locate(root, kind, world, item.File),
                                        kind,
                                        item.Enabled,
                                        session.Store.Root
                                    )
                                    .WaitAsync(token);
                                return result.Item with { File = item.File };
                            }
                            catch (Exception ex) when (ex is IOException or InvalidDataException)
                            {
                                return item;
                            }
                            finally
                            {
                                gate.Release();
                            }
                        })
                    );
                    if (token.IsCancellationRequested || viewRevision != instanceViewRevision)
                        return;
                    var loader = new[] { "", "", "", "Fabric", "Quilt", "Forge", "NeoForge" }[
                        filter
                    ];
                    list.ItemsSource = resolved
                        .Where(item =>
                            (
                                filter == 0
                                || filter == 1 && item.Recognized
                                || filter == 2 && !item.Recognized
                                || filter >= 3
                                    && item.Loader.Contains(
                                        loader,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                            )
                            && (
                                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || item.Version.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || item.File.Contains(query, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                        .ToArray();
                    status.Text = "";
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        status.Text = ex.Message;
                        session.Log("ERROR", ex.ToString());
                    }
                }
            }
            category.SelectionChanged += async (_, _) => await ApplyFilter();
            search.KeyDown += async (_, e) =>
            {
                if (e.Key == global::Avalonia.Input.Key.Enter)
                {
                    e.Handled = true;
                    await ApplyFilter();
                }
            };
            var filters = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180,*,Auto"),
                ColumnSpacing = 8,
            };
            category.MinWidth = search.MinWidth = 0;
            filters.Children.Add(category);
            Grid.SetColumn(search, 1);
            filters.Children.Add(search);
            var find = Btn("Search", ApplyFilter);
            find.Name = "InstalledModSearchButton";
            Grid.SetColumn(find, 2);
            filters.Children.Add(find);
            body.Children.Add(filters);
            body.Children.Add(status);
            if (installedModQuery.Length > 0 || installedModFilter != 0)
                await ApplyFilter();
        }
        list.ItemTemplate = new FuncDataTemplate<ContentItem>(
            (item, _) =>
            {
                var path = ContentManager.Locate(root, kind, world, item.File);
                var title = T(item.Name, 16, true);
                title.MaxLines = 1;
                title.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
                var metadata = T(item.Version + " · " + item.Loader, 12);
                var description = T(
                    System.Text.RegularExpressions.Regex.Replace(item.Description, @"\s+", " "),
                    12
                );
                description.MaxLines = 1;
                description.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
                var icon = new Image
                {
                    Width = 40,
                    Height = 40,
                    IsVisible = false,
                };
                var summary = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 12,
                };
                summary.Children.Add(icon);
                var labels = Stack(title, metadata, description);
                Grid.SetColumn(labels, 1);
                summary.Children.Add(labels);
                var row = Card(
                    Stack(
                        summary,
                        Row(
                            Btn(
                                item.Enabled ? "Disable" : "Enable",
                                async () =>
                                {
                                    session.EnsureFilesIdle();
                                    ContentManager.Toggle(root, kind, world, item.File);
                                    await ContentPage(instance, kind, world);
                                }
                            ),
                            DeleteFile(path, () => ContentPage(instance, kind, world)),
                            Btn("Translate", () => TranslateMod(instance, root, world, item))
                        )
                    )
                );
                global::Avalonia.Media.Imaging.Bitmap? bitmap = null;
                var lifetime = 0;
                row.AttachedToVisualTree += async (_, _) =>
                {
                    var revision = ++lifetime;
                    try
                    {
                        var resolved = await ContentInspectionCache.GetAsync(
                            path,
                            kind,
                            item.Enabled,
                            session.Store.Root
                        );
                        if (revision != lifetime || !row.IsAttachedToVisualTree())
                            return;
                        title.Text = resolved.Item.Name;
                        metadata.Text = resolved.Item.Version + " · " + resolved.Item.Loader;
                        description.Text = System.Text.RegularExpressions.Regex.Replace(
                            resolved.Item.Description,
                            @"\s+",
                            " "
                        );
                        if (resolved.Icon is { Length: > 0 })
                        {
                            using var data = new MemoryStream(resolved.Icon);
                            bitmap = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(data, 80);
                            icon.Source = bitmap;
                            icon.IsVisible = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        session.Log("DEBUG", ex.Message);
                    }
                };
                row.DetachedFromVisualTree += (_, _) =>
                {
                    lifetime++;
                    icon.Source = null;
                    bitmap?.Dispose();
                    bitmap = null;
                };
                return row;
            }
        );
        body.Children.Add(list);
    }

    Button DeleteFile(string path, Func<Task> refresh) =>
        ConfirmationButton(
            "Delete",
            "Delete this file?",
            async () =>
            {
                session.EnsureFilesIdle();
                await Trash(path);
                await refresh();
            }
        );

    async Task TranslateMod(Instance instance, string root, string? world, ContentItem item)
    {
        var settings = session.Store.Config.Settings;
        if (!settings.ModTranslationEnabled)
        {
            await Confirm("Translate", Ui("Enable Mod translation in Settings first."));
            return;
        }
        var path = ContentManager.Locate(root, "mod", world, item.File);
        // 始终从 mod 通用的 en_us 语言文件翻译到启动器界面语言。
        const string source = "en_us";
        var target = settings.LauncherLanguage;
        var entries = await Task.Run(() => ModTranslation.ReadLang(path, source));
        if (entries == null || entries.Count == 0)
        {
            await Confirm("Translate", Ui("No lang file found in this mod."));
            return;
        }
        if (ModTranslation.ServiceLanguage(target) == "en")
        {
            await Confirm(
                "Translate",
                Ui("This mod is already in the selected language.")
            );
            return;
        }
        var modId =
            ContentManager.ModIds(path).FirstOrDefault()
            ?? Path.GetFileNameWithoutExtension(item.File.Replace(".disabled", ""));
        await session.Run(
            "Mod translation",
            async ct =>
            {
                session.ReportStatus(Ui("Translating mod…"));
                var translated = await ModTranslation.TranslateBatchAsync(
                    session.Core.Http,
                    entries,
                    source,
                    target,
                    ct
                );
                ModTranslation.WriteResourcePack(
                    root,
                    path,
                    modId,
                    translated,
                    source,
                    target
                );
                await session.Store.Save();
            }
        );
        await Confirm("Translate", Ui("Translation saved. Load the resource pack in-game."));
    }

    async Task CheckModUpdates(Instance instance)
    {
        session.EnsureFilesIdle();
        var updates = Stack();
        var failures = new List<string>();
        var game = session.Store.GameDirectory(instance);
        var records = Path.Combine(game, ".craft-mods-native.json");
        var curseForgeFiles = File.Exists(records)
            ? JsonNode
                .Parse(await File.ReadAllTextAsync(records))!
                .Items()
                .Where(record => CurseForgeCommunityService.IsProject(record.S("project")))
                .Select(record => record.S("file"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await session.Run(
            "Checking mod updates",
            async ct =>
            {
                var files = await Task.Run(
                    () => ContentManager.List(game, "mod").Where(item => item.Enabled).ToArray(),
                    ct
                );
                foreach (var item in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var update = curseForgeFiles.Contains(item.File)
                            ? await session.CurseForge.FindTrackedModUpdate(instance, item.File, ct)
                            : await session.Modrinth.FindModUpdate(
                                instance,
                                ContentManager.Locate(game, "mod", null, item.File),
                                ct
                            );
                        if (update == null)
                            continue;
                        var captured = update.Value;
                        var status = T("");
                        var completed = false;
                        var button = Btn(
                            "Update",
                            async () =>
                            {
                                if (completed)
                                    return;
                                session.EnsureFilesIdle();
                                await session.Run(
                                    "Update mod",
                                    async token =>
                                    {
                                        await session.Modrinth.UpdateMod(
                                            instance,
                                            item.File,
                                            captured.Project,
                                            captured.Version,
                                            token,
                                            CurseForgeCommunityService.IsProject(captured.Project)
                                                ? (temporary, inner) =>
                                                    session.CurseForge.InstallContent(
                                                        temporary,
                                                        captured.Project,
                                                        captured.Version,
                                                        "mod",
                                                        null,
                                                        inner
                                                    )
                                                : null
                                        );
                                        completed = true;
                                    }
                                );
                                if (completed)
                                    status.Text = Ui("Updated. Old files are in mod-backups.");
                            }
                        );
                        buttonAvailability.Add(button, () => !completed);
                        updates.Children.Add(
                            Card(Stack(T(item.Name, 15, true), T(captured.Name), button, status))
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(item.File + ": " + ex.Message);
                        session.Log("ERROR", ex.ToString());
                    }
                }
            }
        );
        if (updates.Children.Count == 0)
            updates.Children.Add(T("No compatible updates found."));
        foreach (var failure in failures)
            updates.Children.Add(T(failure, 12));
        pageContent.Content = Stack(Btn("Back", () => ContentPage(instance, "mod")), updates);
    }

    async Task WorldsPage(Instance instance)
    {
        var game = session.Store.GameDirectory(instance);
        var body = Stack(
            Row(
                Btn("Back", InstanceSettings),
                Btn("World backups", () => WorldBackupsPage(instance)),
                Btn(
                    "Import world ZIP",
                    async () =>
                    {
                        session.EnsureFilesIdle();
                        var file = await OpenFile("*.zip");
                        if (file == null)
                            return;
                        var name = Input(Path.GetFileNameWithoutExtension(file));
                        if (!await Dialog("World name", name, true))
                            return;
                        var path = Store.SafePath(game, "saves/" + name.Text!.Trim());
                        await session.Run(
                            "Import world",
                            ct => WorldArchive.Import(file, path, WorldArchiveRoot(file), ct)
                        );
                        await WorldsPage(instance);
                    }
                )
            )
        );
        foreach (var world in ContentManager.Worlds(game))
        {
            var folder = Store.SafePath(game, "saves/" + world);
            body.Children.Add(
                Card(
                    Stack(
                        T(world, 16, true),
                        Row(
                            Btn(
                                "Open folder",
                                () =>
                                {
                                    Open(folder);
                                    return Task.CompletedTask;
                                }
                            ),
                            Btn(
                                "Export",
                                async () =>
                                {
                                    session.EnsureFilesIdle();
                                    var target = await SaveFile(world, ".zip");
                                    if (target != null)
                                        await session.Run(
                                            "Export world",
                                            ct => WorldArchive.Export(folder, target, ct)
                                        );
                                }
                            ),
                            Btn(
                                "Back up",
                                async () =>
                                {
                                    session.EnsureFilesIdle();
                                    await BackupWorld(instance, world, folder);
                                }
                            ),
                            Btn("Data packs", () => ContentPage(instance, "datapack", world)),
                            DeleteFile(folder, () => WorldsPage(instance))
                        )
                    )
                )
            );
        }
        await ShowInstancePage(instance, "worlds", body);
    }

    static string WorldArchiveRoot(string file)
    {
        using var archive = ZipFile.OpenRead(file);
        if (archive.GetEntry("level.dat") != null)
            return "";
        var level =
            archive
                .Entries.Where(e =>
                    e.FullName.EndsWith("/level.dat") && e.FullName.Split('/').Length == 2
                )
                .SingleOrDefault()
            ?? throw new IOException("The ZIP must contain a Minecraft world with level.dat.");
        return level.FullName.Split('/')[0];
    }

    string schematicQuery = "";

    async Task SchematicsPage(Instance instance)
    {
        var directory = await Task.Run(() =>
            SchematicManager.EnsureDirectory(session.Store, instance)
        );
        if (directory == null)
        {
            await Dialog(
                "Schematics",
                T(
                    "No compatible instances. Install Create or a schematic mod in a modded instance first. You can still download files."
                ),
                false
            );
            return;
        }
        var body = Stack(
            Row(
                Btn("Back", InstanceSettings),
                Btn(
                    "Import",
                    async () =>
                    {
                        session.EnsureFilesIdle();
                        var files = await StorageProvider.OpenFilePickerAsync(
                            new() { AllowMultiple = true }
                        );
                        await session.Run(
                            "Import schematics",
                            ct =>
                                SchematicManager.Import(
                                    directory,
                                    files
                                        .Select(f => f.TryGetLocalPath())
                                        .OfType<string>()
                                        .ToArray(),
                                    ct
                                )
                        );
                        await SchematicsPage(instance);
                    }
                ),
                Btn(
                    "Open folder",
                    () =>
                    {
                        Open(directory);
                        return Task.CompletedTask;
                    }
                )
            )
        );
        var folders = new List<string>();
        var files = await Task.Run(() => SchematicManager.List(directory, folders));
        var search = Input(schematicQuery, "Search schematic files");
        var tree = Stack();
        body.Children.Add(search);
        body.Children.Add(tree);
        void Draw()
        {
            tree.Children.Clear();
            var query = (search.Text ?? "").Trim();
            schematicQuery = query;
            var filtered = files
                .Where(file =>
                    file.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            var groups = new Dictionary<string, StackPanel> { [""] = tree };
            foreach (
                var folder in folders
                    .Where(folder =>
                        query.Length == 0
                        || filtered.Any(file =>
                            file.RelativePath.StartsWith(
                                folder + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            )
            {
                var parent = Path.GetDirectoryName(folder) ?? "";
                var nested = Stack();
                var expander = new Expander
                {
                    Header = Path.GetFileName(folder),
                    Content = nested,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = query.Length > 0,
                };
                var folderCard = Card(expander);
                folderCard.Padding = new Thickness(11, 7);
                folderCard.MinHeight = 64;
                groups.GetValueOrDefault(parent, tree).Children.Add(folderCard);
                groups[folder] = nested;
            }
            foreach (var file in filtered)
            {
                var relative = file.RelativePath;
                var row = Row(
                    Btn(
                        "Export",
                        async () =>
                        {
                            var target = await SaveFile(
                                Path.GetFileName(relative),
                                Path.GetExtension(relative)
                            );
                            if (target != null)
                                await session.Run(
                                    "Export schematic",
                                    ct => SchematicManager.Export(directory, relative, target, ct)
                                );
                        }
                    ),
                    Btn(
                        "Rename",
                        async () =>
                        {
                            session.EnsureFilesIdle();
                            var name = Input(Path.GetFileNameWithoutExtension(relative));
                            if (await Dialog("Rename", name, true))
                            {
                                SchematicManager.Rename(directory, relative, name.Text!);
                                await SchematicsPage(instance);
                            }
                        }
                    ),
                    DeleteFile(
                        SchematicManager.ExistingFile(directory, relative),
                        () => SchematicsPage(instance)
                    )
                );
                var name = T(Path.GetFileName(relative), 14, true);
                name.MaxLines = 1;
                name.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(name, relative);
                var line = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                };
                var details = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        name,
                        T(
                            $"{Path.GetExtension(relative).ToUpperInvariant()} · {file.Size / 1024d:N1} KB · {file.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                            11
                        ),
                    },
                    VerticalAlignment = VerticalAlignment.Center,
                };
                line.Children.Add(details);
                Grid.SetColumn(row, 1);
                line.Children.Add(row);
                var fileCard = Card(line);
                fileCard.Padding = new Thickness(11, 7);
                fileCard.MinHeight = 64;
                groups
                    .GetValueOrDefault(Path.GetDirectoryName(relative) ?? "", tree)
                    .Children.Add(fileCard);
            }
        }
        search.TextChanged += (_, _) => Draw();
        Draw();
        await ShowInstancePage(instance, "schematics", body);
    }

    async Task ExportPackPage(Instance instance)
    {
        session.EnsureFilesIdle();
        var revision = ++generation;
        pageTitle.Text = Ui("Export modpack");
        var body = Stack();
        pageContent.Content = body;
        var name = Input(instance.Name);
        var version = Input("1.0.0");
        var format = Choice(new[] { "Modrinth (.mrpack)", "CurseForge (.zip)" });
        var resources = new CheckBox { Content = Ui("Include resource files"), IsChecked = true };
        body.Children.Add(Row(Btn("Back", InstanceSettings)));
        body.Children.Add(Field("Modpack name", name));
        body.Children.Add(Field("Modpack version", version));
        body.Children.Add(Field("Modpack format", format));
        body.Children.Add(resources);
        var tree = new TreeView { MaxHeight = 420 };
        tree.ItemTemplate = new FuncTreeDataTemplate<ExportNode>(
            (node, _) =>
            {
                var check = new CheckBox { Content = node.Label };
                check.Bind(
                    ToggleButton.IsCheckedProperty,
                    new Binding(nameof(ExportNode.Selected)) { Mode = BindingMode.TwoWay }
                );
                return check;
            },
            node => node.Children
        );
        body.Children.Add(Card(tree));
        var start = Btn(
            "Start packaging",
            async () =>
            {
                session.EnsureFilesIdle();
                var kind = format.SelectedIndex == 0 ? "modrinth" : "curseforge";
                var bundle = resources.IsChecked == true;
                var output = await SaveFile(
                    name.Text + "-" + version.Text,
                    kind == "modrinth" ? ".mrpack" : ".zip"
                );
                if (output == null)
                    return;
                var selected = tree.ItemsSource!.Cast<ExportNode>()
                    .SelectMany(n => n.Files())
                    .ToArray();
                await session.Run(
                    "Export modpack",
                    ct =>
                        Task.Run(
                            () =>
                                ModpackExporter.Export(
                                    session.Store.GameDirectory(instance),
                                    instance,
                                    name.Text ?? "",
                                    version.Text ?? "",
                                    selected,
                                    output,
                                    ct,
                                    overwrite: true,
                                    format: kind,
                                    includeResources: bundle,
                                    resolveReference: (relative, token) =>
                                        ModpackExportReferences.Resolve(
                                            session.Core,
                                            session.Store.GameDirectory(instance),
                                            relative,
                                            kind,
                                            token
                                        )
                                ),
                            ct
                        )
                );
            },
            true
        );
        start.HorizontalAlignment = HorizontalAlignment.Right;
        start.MinWidth = 160;
        start.MinHeight = 44;
        start.IsEnabled = false;
        body.Children.Add(start);
        var files = await Task.Run(() =>
            ModpackExporter.List(session.Store.GameDirectory(instance), CancellationToken.None)
        );
        if (revision != generation)
            return;
        var root = new List<ExportNode>();
        var nodes = new Dictionary<string, ExportNode>();
        foreach (var file in files)
        {
            ExportNode? parent = null;
            var path = "";
            foreach (var part in file.Split('/'))
            {
                path = path.Length == 0 ? part : path + "/" + part;
                if (!nodes.TryGetValue(path, out var node))
                {
                    var label = parent == null ? ExportLabel(part) : part;
                    node = new ExportNode(
                        path,
                        label,
                        file.Split('/')[0] is not ("saves" or "screenshots" or "servers.dat")
                    );
                    nodes.Add(path, node);
                    if (parent == null)
                        root.Add(node);
                    else
                        parent.Children.Add(node);
                }
                parent = node;
            }
            parent!.IsFile = true;
        }
        tree.ItemsSource = root;
        start.IsEnabled = true;
    }

    string ExportLabel(string part) =>
        part switch
        {
            "mods"
            or "config"
            or "defaultconfigs"
            or "resourcepacks"
            or "shaderpacks"
            or "saves"
            or "screenshots"
            or "schematics"
            or "tacz" => Ui("export.folder." + part),
            "servers.dat" => Ui("export.folder.servers"),
            "options.txt" => Ui("export.folder.options"),
            _ => part,
        };
}