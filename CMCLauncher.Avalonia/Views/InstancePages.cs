using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control HomePage()
    {
        var instances = Choice(session.Store.Config.Instances, session.Selected);
        var accounts = Choice(session.Store.Config.Accounts, session.Account);
        instances.SelectionChanged += async (_, _) =>
        {
            session.Selected = instances.SelectedItem as Instance;
            await session.Save();
        };
        accounts.SelectionChanged += async (_, _) =>
        {
            session.Account = accounts.SelectedItem as Account;
            await session.Save();
        };
        return Stack(
            Card(
                Stack(
                    T("Minecraft Java Edition", 23, true),
                    Field("Instance", instances),
                    Field("Account", accounts),
                    Row(
                        Btn(
                            "Launch",
                            () => session.Run("Launch game", session.LaunchSelected),
                            true
                        ),
                        Btn("Version settings", () => InstanceSettings()),
                        Btn("Download game", () => Navigate("downloads")),
                        Btn("Import modpack", ImportPack)
                    )
                )
            ),
            Card(Stack(T("Launcher notes", 16, true), T(session.Store.Config.Settings.HomeNote)))
        );
    }

    Task<Control> InstancesPage()
    {
        var search = Input("", "Search version name, description or Minecraft version");
        var loader = Choice(new[] { "All", "vanilla", "fabric", "quilt", "forge", "neoforge" });
        var sort = Choice(new[] { "Default", "Name", "Last played", "Minecraft version" });
        var view = Choice(
            new[] { "List", "Tiles" },
            session.Store.Config.Settings.InstanceView == "tiles" ? "Tiles" : "List"
        );
        var roots = new MinecraftDirectoryManager(session.Store)
            .List()
            .Select(r => r.Path)
            .ToArray();
        var directory = Choice(new[] { "All directories" }.Concat(roots));
        var results = Stack();
        void Draw()
        {
            results.Children.Clear();
            var entries = session
                .Store.Config.Instances.Where(i =>
                    (
                        string.IsNullOrWhiteSpace(search.Text)
                        || (i.Name + " " + i.Description + " " + i.GameVersion).Contains(
                            search.Text,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && (loader.SelectedIndex == 0 || i.Loader == loader.SelectedItem?.ToString())
                    && (
                        directory.SelectedIndex == 0
                        || session.Store.StorageDirectory(i) == directory.SelectedItem?.ToString()
                    )
                )
                .ToArray();
            var ordered = sort.SelectedIndex switch
            {
                1 => entries.OrderBy(i => i.Name),
                2 => entries.OrderByDescending(i => i.LastPlayed),
                3 => entries.OrderByDescending(i => i.GameVersion),
                _ => entries.OrderBy(i => i.Loader).ThenBy(i => i.Name),
            };
            void Group(string title, IEnumerable<Instance> items)
            {
                var array = items.ToArray();
                if (array.Length == 0)
                    return;
                results.Children.Add(T(title, 17, true));
                var tiles = new WrapPanel();
                foreach (var item in array)
                {
                    var titleText = T(item.Name, 17, true);
                    titleText.MaxLines = 1;
                    titleText.TextTrimming = TextTrimming.CharacterEllipsis;
                    var card = Stack(
                        titleText,
                        T($"Minecraft {item.GameVersion} · {item.Loader}"),
                        T(item.Description, 12),
                        Row(
                            Btn(
                                item.Favorite ? "★" : "☆",
                                async () =>
                                {
                                    item.Favorite = !item.Favorite;
                                    await session.Save();
                                    Draw();
                                }
                            ),
                            Btn(
                                "Settings",
                                async () =>
                                {
                                    session.Selected = item;
                                    await session.Save();
                                    await InstanceSettings();
                                }
                            ),
                            Btn(
                                "Launch",
                                async () =>
                                {
                                    session.Selected = item;
                                    await session.Save();
                                    await session.Run("Launch game", session.LaunchSelected);
                                },
                                true
                            )
                        )
                    );
                    var border = Card(card);
                    if (view.SelectedIndex == 1)
                    {
                        border.Width = 260;
                        border.Height = 260;
                        border.Margin = new Thickness(0, 0, 12, 12);
                        tiles.Children.Add(border);
                    }
                    else
                        results.Children.Add(border);
                }
                if (view.SelectedIndex == 1)
                    results.Children.Add(tiles);
            }
            Group("Favorites", ordered.Where(i => i.Favorite).OrderBy(i => i.Name));
            Group("Instances", ordered.Where(i => !i.Favorite));
            if (entries.Length == 0)
                results.Children.Add(T("No instances found."));
        }
        search.TextChanged += (_, _) => Draw();
        loader.SelectionChanged += (_, _) => Draw();
        directory.SelectionChanged += (_, _) => Draw();
        sort.SelectionChanged += (_, _) => Draw();
        view.SelectionChanged += async (_, _) =>
        {
            session.Store.Config.Settings.InstanceView = view.SelectedIndex == 1 ? "tiles" : "list";
            await session.Save();
            Draw();
        };
        Draw();
        return Task.FromResult<Control>(
            Stack(
                Row(
                    directory,
                    Btn("Download game", () => Navigate("downloads")),
                    Btn("Import modpack", ImportPack),
                    Btn("Manage Minecraft directories", () => DirectoriesPage()),
                    Btn("Import local instances", ImportLocal),
                    Btn("Migrate other launchers", MigrateForeign)
                ),
                Field("Search", search),
                Row(loader, sort, view),
                results
            )
        );
    }

    async Task ImportLocal()
    {
        var path = await Folder();
        if (path == null)
            return;
        await session.Run(
            "Import local instances",
            async ct =>
            {
                var scan = await session.Core.ScanLocalInstances(path, ct);
                foreach (var instance in scan.Instances)
                    if (
                        !session.Store.Config.Instances.Any(i =>
                            Path.GetFullPath(session.Store.GameDirectory(i))
                            == Path.GetFullPath(session.Store.GameDirectory(instance))
                        )
                    )
                        session.Store.Config.Instances.Add(instance);
                await session.Save();
            }
        );
        await Navigate("instances");
    }

    async Task MigrateForeign()
    {
        var path = await Folder();
        if (path == null)
            return;
        await session.Run(
            "Migrate other launchers",
            async ct =>
            {
                var scan = await Task.Run(() => session.Core.ScanForeignInstances(path, ct), ct);
                var migrated = await session.Core.MigrateForeignInstances(scan.Instances, ct);
                session.Store.Config.Instances.AddRange(migrated.Migrated);
                foreach (var skipped in migrated.Skipped)
                    session.Log("WARN", skipped);
                await session.Save();
            }
        );
        await Navigate("instances");
    }

    async Task ImportPack()
    {
        session.EnsureFilesIdle();
        var path = await OpenFile("*.mrpack", "*.zip");
        if (path == null)
            return;
        await session.Run(
            "Import modpack",
            async ct =>
            {
                var instance = await session.Modrinth.InstallPack(path, ct, ResolvePackName);
                await session.AddInstance(instance);
            }
        );
        await Navigate("instances");
    }

    async Task ResolvePackName(Instance instance, CancellationToken ct)
    {
        var input = Input(instance.Name);
        if (!await Dialog("Choose a new name for this modpack", Field("Name", input), true))
            throw new OperationCanceledException(ct);
        instance.Name = input.Text?.Trim() ?? "";
        session.Store.EnsureUniqueInstanceName(instance);
    }

    Task InstanceSettings()
    {
        if (session.Selected is not { } instance)
            throw new IOException(Ui("Select an instance."));
        pageTitle.Text = instance.Name;
        var body = Stack();
        async Task Edit(string title, string current, Action<string> apply, bool multiline = false)
        {
            var value = Input(current, multiline: multiline);
            if (await Dialog(title, value, true))
            {
                apply(value.Text ?? "");
                await session.Save();
                await InstanceSettings();
            }
        }
        body.Children.Add(
            Card(
                Stack(
                    T(instance.Name, 22, true),
                    T(
                        $"Minecraft {instance.GameVersion} · {instance.Loader} {instance.LoaderVersion}"
                    ),
                    T(instance.Description),
                    Row(
                        Btn(
                            "Edit name",
                            () =>
                                Edit(
                                    "Edit name",
                                    instance.Name,
                                    text => instance.Name = text.Trim()
                                )
                        ),
                        Btn(
                            "Edit description",
                            () =>
                                Edit(
                                    "Edit description",
                                    instance.Description,
                                    text => instance.Description = text,
                                    true
                                )
                        ),
                        Btn(
                            "Edit icon",
                            async () =>
                            {
                                var path = await OpenFile("*.png", "*.jpg", "*.webp");
                                if (path != null)
                                {
                                    instance.IconPath = path;
                                    await session.Save();
                                }
                            }
                        ),
                        Btn(
                            "Open folder",
                            () =>
                            {
                                Directory.CreateDirectory(session.Store.GameDirectory(instance));
                                Open(session.Store.GameDirectory(instance));
                                return Task.CompletedTask;
                            }
                        )
                    )
                )
            )
        );
        body.Children.Add(
            Row(
                Btn("Saves", () => WorldsPage(instance)),
                Btn("Mods", () => ContentPage(instance, "mod")),
                Btn("Resource packs", () => ContentPage(instance, "resourcepack")),
                Btn("Shaders", () => ContentPage(instance, "shader")),
                Btn("Data packs", () => ContentPage(instance, "datapack")),
                Btn("Schematics", () => SchematicsPage(instance))
            )
        );
        body.Children.Add(
            Card(
                Stack(
                    T("Version management", 16, true),
                    Row(
                        Btn("Export modpack", () => ExportPackPage(instance)),
                        DangerButton(
                            "Delete version",
                            async () =>
                            {
                                session.EnsureFilesIdle();
                                var inspection = session.Store.InspectVersionDeletion(instance);
                                var delete = new CheckBox
                                {
                                    Content = Ui("Also move version files to the recycle bin"),
                                    IsEnabled = inspection.DeletableDirectory != null,
                                };
                                var info = Stack(T(instance.Name), delete);
                                if (inspection.DeletableDirectory == null)
                                    info.Children.Add(
                                        T("Shared or dependent game files will be kept.")
                                    );
                                if (!await Dialog("Delete version?", info, true))
                                    return;
                                if (
                                    delete.IsChecked == true
                                    && inspection.DeletableDirectory is { } folder
                                )
                                    await Trash(folder);
                                session.Store.Config.Instances.Remove(instance);
                                session.Selected = session.Store.Config.Instances.FirstOrDefault();
                                await session.Save();
                                await Navigate("instances");
                            }
                        )
                    )
                )
            )
        );
        pageContent.Content = body;
        return Task.CompletedTask;
    }

    Button DangerButton(string text, Func<Task> action)
    {
        var button = Btn(text, action);
        button.Foreground = new SolidColorBrush(Color.Parse("#E85A64"));
        button.BorderBrush = button.Foreground;
        return button;
    }

    Task Trash(string path) => LauncherPlatform.Current.MoveToTrash(path);

    Task DirectoriesPage()
    {
        var manager = new MinecraftDirectoryManager(session.Store);
        var body = Stack(
            Row(
                Btn("Back", () => Navigate("instances")),
                Btn(
                    "Create directory",
                    async () =>
                    {
                        var path = Input("", "New directory path");
                        if (!await Dialog("Create directory", path, true))
                            return;
                        await session.Run(
                            "Create directory",
                            async _ =>
                            {
                                await manager.Create(path.Text!);
                                await manager.Select(path.Text!);
                            }
                        );
                        await DirectoriesPage();
                    }
                ),
                Btn(
                    "Use existing directory",
                    async () =>
                    {
                        var path = await Folder();
                        if (path != null)
                        {
                            await manager.Select(path);
                            await DirectoriesPage();
                        }
                    }
                )
            )
        );
        foreach (var record in manager.List())
        {
            var buttons = Row(
                Btn(
                    "Use for new installations",
                    async () =>
                    {
                        await manager.Select(record.Path);
                        await DirectoriesPage();
                    }
                ),
                Btn(
                    "Open folder",
                    () =>
                    {
                        Open(record.Path);
                        return Task.CompletedTask;
                    }
                )
            );
            if (manager.CanMigrate(record))
                buttons.Children.Add(
                    Btn(
                        "Migrate directory",
                        async () =>
                        {
                            session.EnsureFilesIdle();
                            var path = Input("", "Choose a new destination that does not exist");
                            if (!await Dialog("Migrate directory", path, true))
                                return;
                            await session.Run(
                                "Migrate directory",
                                async ct =>
                                {
                                    await manager.Migrate(record, path.Text!, ct);
                                }
                            );
                            await DirectoriesPage();
                        }
                    )
                );
            body.Children.Add(Card(Stack(T(record.Path, 14, true), buttons)));
        }
        pageContent.Content = body;
        return Task.CompletedTask;
    }

    async Task FirstRun()
    {
        var choice = Choice(
            new[]
            {
                DesktopUpdateInstaller.PackageManaged
                    ? "User data directory"
                    : "Launcher directory",
                "AppData",
                "Custom directory",
            }
        );
        var custom = Input("", "Minecraft directory");
        if (!await Dialog("Choose where to create .minecraft", Stack(choice, custom), true))
            return;
        var path = choice.SelectedIndex switch
        {
            0 => Path.Combine(
                DesktopUpdateInstaller.PackageManaged ? session.Store.Root : session.UserDirectory,
                ".minecraft"
            ),
            1 => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft"
            ),
            _ => custom.Text ?? "",
        };
        var manager = new MinecraftDirectoryManager(session.Store);
        if (!Directory.Exists(path))
            await manager.Create(path);
        await manager.Select(path);
        session.Store.Config.Settings.MinecraftDirectorySetupPending = false;
        await session.Save();
        if (
            await Confirm(
                "Import existing instances",
                "Do you want to import instances from another launcher?"
            )
        )
            await ImportLocal();
    }
}
