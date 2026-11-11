using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using CMCLauncher.Avalonia.ViewModels;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
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
            pageContent.Content = body;
            return;
        }
        if (kind == "mod")
            body.Children.Add(Btn("Check for updates", () => CheckModUpdates(instance)));
        var items = await Task.Run(() => ContentManager.List(root, kind, world));
        foreach (var item in items)
        {
            var path = ContentManager.Locate(root, kind, world, item.File);
            body.Children.Add(
                Card(
                    Stack(
                        T(item.Name, 16, true),
                        T(item.Version + " · " + item.Loader, 12),
                        T(item.Description, 12),
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
                            DeleteFile(path, () => ContentPage(instance, kind, world))
                        )
                    )
                )
            );
        }
        if (items.Count == 0)
            body.Children.Add(T("No content found."));
        pageContent.Content = body;
    }

    Button DeleteFile(string path, Func<Task> refresh)
    {
        var button = new Button { Content = Ui("Delete") };
        var flyout = new Flyout();
        flyout.Content = Stack(
            T("Delete this file?"),
            Row(
                DangerButton(
                    "Delete",
                    async () =>
                    {
                        session.EnsureFilesIdle();
                        await Trash(path);
                        flyout.Hide();
                        await refresh();
                    }
                ),
                Btn(
                    "Cancel",
                    () =>
                    {
                        flyout.Hide();
                        return Task.CompletedTask;
                    }
                )
            )
        );
        button.Flyout = flyout;
        return button;
    }

    async Task CheckModUpdates(Instance instance)
    {
        session.EnsureFilesIdle();
        var updates = Stack();
        await session.Run(
            "Check for updates",
            async ct =>
            {
                foreach (
                    var item in await Task.Run(
                        () =>
                            ContentManager
                                .List(session.Store.GameDirectory(instance), "mod")
                                .Where(i => i.Enabled)
                                .ToArray(),
                        ct
                    )
                )
                {
                    var cf = await session.CurseForge.FindTrackedModUpdate(instance, item.File, ct);
                    var mr =
                        cf == null
                            ? await session.Modrinth.FindModUpdate(
                                instance,
                                ContentManager.Locate(
                                    session.Store.GameDirectory(instance),
                                    "mod",
                                    null,
                                    item.File
                                ),
                                ct
                            )
                            : null;
                    var update = cf ?? mr;
                    if (update == null)
                        continue;
                    var captured = update.Value;
                    updates.Children.Add(
                        Card(
                            Stack(
                                T(item.Name, 15, true),
                                T(captured.Name),
                                Btn(
                                    "Update",
                                    async () =>
                                    {
                                        session.EnsureFilesIdle();
                                        await session.Run(
                                            "Update mod",
                                            token =>
                                                session.Modrinth.UpdateMod(
                                                    instance,
                                                    item.File,
                                                    captured.Project,
                                                    captured.Version,
                                                    token,
                                                    cf != null
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
                                                )
                                        );
                                    }
                                )
                            )
                        )
                    );
                }
            }
        );
        if (updates.Children.Count == 0)
            updates.Children.Add(T("No compatible updates found."));
        pageContent.Content = Stack(Btn("Back", () => ContentPage(instance, "mod")), updates);
    }

    Task WorldsPage(Instance instance)
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
        pageContent.Content = body;
        return Task.CompletedTask;
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

    async Task SchematicsPage(Instance instance)
    {
        var directory = SchematicManager.EnsureDirectory(session.Store, instance);
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
        var groups = new Dictionary<string, StackPanel> { [""] = body };
        foreach (var folder in folders.OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)))
        {
            var parent = Path.GetDirectoryName(folder) ?? "";
            var nested = Stack();
            var expander = new Expander
            {
                Header = Path.GetFileName(folder),
                Content = nested,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            groups.GetValueOrDefault(parent, body).Children.Add(Card(expander));
            groups[folder] = nested;
        }
        foreach (var file in files)
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
            groups
                .GetValueOrDefault(Path.GetDirectoryName(relative) ?? "", body)
                .Children.Add(
                    Card(
                        Stack(
                            T(Path.GetFileName(relative), 14, true),
                            T($"{file.Size / 1024.0:N1} KiB", 11),
                            row
                        )
                    )
                );
        }
        pageContent.Content = body;
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
