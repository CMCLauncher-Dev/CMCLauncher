using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Task<Control> InstancesPage()
    {
        var search = Input("", "Search version name, description or Minecraft version");
        var loader = Choice(
            new[] { "All versions", "Favorites", "vanilla", "fabric", "quilt", "forge", "neoforge" }
        );
        var sort = Choice(
            new[] { "Default (by loader)", "Recently played", "Name", "Minecraft version" }
        );
        var view = Choice(
            new[] { "List", "Tiles" },
            session.Store.Config.Settings.InstanceView is "tile" or "tiles" ? "Tiles" : "List"
        );
        view.Name = "InstanceLayout";
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
                    && (
                        loader.SelectedIndex == 0
                        || (
                            loader.SelectedIndex == 1
                                ? i.Favorite
                                : i.Loader.Equals(
                                    loader.SelectedItem?.ToString(),
                                    StringComparison.OrdinalIgnoreCase
                                )
                        )
                    )
                    && (
                        directory.SelectedIndex == 0
                        || session.Store.StorageDirectory(i) == directory.SelectedItem?.ToString()
                    )
                )
                .ToArray();
            var ordered = sort.SelectedIndex switch
            {
                1 => entries
                    .OrderByDescending(i => i.LastPlayed)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
                3 => entries
                    .OrderBy(i => i.GameVersion, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
                _ => entries.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            };
            void Group(string title, IEnumerable<Instance> items)
            {
                var array = items.ToArray();
                if (array.Length == 0)
                    return;
                if (title.Length > 0)
                    results.Children.Add(T(title, 17, true));
                var tiles = new WrapPanel();
                foreach (var item in array)
                {
                    var titleText = T(item.Name, 16, true);
                    titleText.MaxLines = 1;
                    titleText.TextTrimming = TextTrimming.CharacterEllipsis;
                    var metadata = T($"Minecraft {item.GameVersion} · {item.Loader}", 12);
                    metadata.MaxLines = 1;
                    metadata.TextTrimming = TextTrimming.CharacterEllipsis;
                    var identity = new StackPanel
                    {
                        Spacing = 3,
                        Children = { titleText, metadata },
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    if (item.Description.Length > 0)
                    {
                        var description = T(item.Description, 11);
                        description.MaxLines = 1;
                        description.TextTrimming = TextTrimming.CharacterEllipsis;
                        identity.Children.Add(description);
                    }
                    var favorite = HomeTool(
                        item.Favorite ? "★" : "☆",
                        "Favorites",
                        async () =>
                        {
                            item.Favorite = !item.Favorite;
                            await session.Save();
                            Draw();
                        }
                    );
                    favorite.Width = favorite.MinWidth = 30;
                    favorite.Height = favorite.MinHeight = 28;
                    var flyout = new Flyout();
                    var more = HomeTool("⋯", "More", () => Task.CompletedTask);
                    var menu = Stack(
                        Btn(
                            "Settings",
                            async () =>
                            {
                                flyout.Hide();
                                session.Selected = item;
                                await session.Save();
                                await InstanceSettings();
                            }
                        ),
                        Btn(
                            "Open folder",
                            () =>
                            {
                                flyout.Hide();
                                Open(session.Store.GameDirectory(item));
                                return Task.CompletedTask;
                            }
                        ),
                        DangerButton(
                            "Delete version",
                            async () =>
                            {
                                flyout.Hide();
                                await DeleteInstance(item);
                            }
                        )
                    );
                    flyout.Content = menu;
                    more.Flyout = flyout;
                    var actions = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { favorite, more },
                        HorizontalAlignment = HorizontalAlignment.Right,
                    };
                    var tile = view.SelectedIndex == 1;
                    var artwork = InstanceArtwork(item, tile ? 64 : 40);
                    var row = tile
                        ? new Grid
                        {
                            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                            RowSpacing = 10,
                        }
                        : new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("40,*,Auto"),
                            ColumnSpacing = 12,
                        };
                    if (tile)
                    {
                        row.Children.Add(actions);
                        artwork.HorizontalAlignment = HorizontalAlignment.Center;
                        artwork.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(artwork, 1);
                        row.Children.Add(artwork);
                        Grid.SetRow(identity, 2);
                        row.Children.Add(identity);
                        foreach (var label in identity.Children.OfType<TextBlock>())
                            label.TextAlignment = TextAlignment.Center;
                    }
                    else
                    {
                        row.Children.Add(artwork);
                        Grid.SetColumn(identity, 1);
                        row.Children.Add(identity);
                        Grid.SetColumn(actions, 2);
                        row.Children.Add(actions);
                    }
                    var border = Card(row);
                    border.Padding = new Thickness(11, 7);
                    border.BorderThickness = new Thickness(1);
                    if (session.Selected?.Id == item.Id)
                        border.BorderBrush = Accent;
                    border.Focusable = true;
                    border.Tag = "instance-card";
                    ToolTip.SetTip(border, session.Store.GameDirectory(item));
                    async Task Choose()
                    {
                        session.Selected = item;
                        await session.Save();
                        await Navigate("home");
                    }
                    border.AddHandler(
                        global::Avalonia.Input.Gestures.TappedEvent,
                        async (_, e) =>
                        {
                            if (
                                e.Source is global::Avalonia.Visual visual
                                && global::Avalonia
                                    .VisualTree.VisualExtensions.GetVisualAncestors(visual)
                                    .TakeWhile(parent => parent != border)
                                    .Any(parent => parent is Button)
                            )
                                return;
                            try
                            {
                                await Choose();
                            }
                            catch (Exception ex)
                            {
                                await Error(ex);
                            }
                        }
                    );
                    border.KeyDown += async (_, e) =>
                    {
                        if (
                            e.Key
                                is global::Avalonia.Input.Key.Enter
                                    or global::Avalonia.Input.Key.Space
                            && e.Source == border
                        )
                        {
                            e.Handled = true;
                            try
                            {
                                await Choose();
                            }
                            catch (Exception ex)
                            {
                                await Error(ex);
                            }
                        }
                    };
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
            var regular = ordered.Where(i => !i.Favorite);
            if (sort.SelectedIndex == 0)
                foreach (
                    var group in regular.GroupBy(i => i.Loader, StringComparer.OrdinalIgnoreCase)
                )
                    Group(
                        group.Key.ToLowerInvariant() switch
                        {
                            "vanilla" => "Vanilla",
                            "fabric" => "Fabric",
                            "quilt" => "Quilt",
                            "forge" => "Forge",
                            "neoforge" => "NeoForge",
                            _ => group.Key,
                        },
                        group
                    );
            else
                Group("", regular);
            if (entries.Length == 0)
                results.Children.Add(T("No instances found."));
        }
        search.TextChanged += (_, _) => Draw();
        loader.SelectionChanged += (_, _) => Draw();
        directory.SelectionChanged += (_, _) => Draw();
        sort.SelectionChanged += (_, _) => Draw();
        view.SelectionChanged += async (_, _) =>
        {
            session.Store.Config.Settings.InstanceView = view.SelectedIndex == 1 ? "tile" : "list";
            await session.Save();
            Draw();
        };
        search.MinWidth = loader.MinWidth = sort.MinWidth = view.MinWidth = 0;
        var filterBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,Auto,*,*,*"),
            ColumnSpacing = 8,
        };
        var find = Btn(
            "Search",
            () =>
            {
                Draw();
                return Task.CompletedTask;
            }
        );
        var filters = new Control[] { search, find, loader, view, sort };
        for (var i = 0; i < filters.Length; i++)
        {
            Grid.SetColumn(filters[i], i);
            filterBar.Children.Add(filters[i]);
        }
        Draw();
        return Task.FromResult<Control>(
            Stack(
                Row(
                    directory,
                    Btn("Download game", () => Navigate("downloads")),
                    Btn("Import modpack", ImportPack),
                    Btn("Manage Minecraft directories", () => DirectoriesPage()),
                    Btn(
                        "Scan versions",
                        () =>
                            ImportLocalFrom(
                                directory.SelectedIndex > 0
                                    ? directory.SelectedItem?.ToString()
                                    : session.Store.MinecraftRoot
                            )
                    ),
                    Btn("Import local instances", ImportLocal),
                    Btn("Migrate other launchers", MigrateForeign)
                ),
                filterBar,
                results
            )
        );
    }

    Task ImportLocal() => ImportLocalFrom(null);

    async Task ImportLocalFrom(string? directory)
    {
        var path = directory ?? await Folder();
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

    async Task InstanceSettings()
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
                                    await InstanceSettings();
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
            Card(
                Stack(
                    T("Version management", 16, true),
                    Row(
                        Btn("Export modpack", () => ExportPackPage(instance)),
                        DangerButton("Delete version", () => DeleteInstance(instance))
                    )
                )
            )
        );
        if (body.Children[0] is Border { Child: StackPanel summary })
        {
            var details = new StackPanel { Spacing = 6 };
            foreach (var text in summary.Children.OfType<TextBlock>().Take(3).ToArray())
            {
                summary.Children.Remove(text);
                details.Children.Add(text);
            }
            var identity = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("56,*"),
                ColumnSpacing = 14,
            };
            identity.Children.Add(InstanceArtwork(instance, 56));
            Grid.SetColumn(details, 1);
            identity.Children.Add(details);
            summary.Children.Insert(0, identity);
            if (summary.Children.OfType<WrapPanel>().FirstOrDefault() is { } actions)
                foreach (
                    var open in actions
                        .Children.OfType<Button>()
                        .Where(button => button.Content?.ToString() == Ui("Open folder"))
                        .ToArray()
                )
                    actions.Children.Remove(open);
        }
        body.Children.Insert(
            1,
            Card(
                Stack(
                    T("Game directory", 15, true),
                    new SelectableTextBlock
                    {
                        Text = session.Store.GameDirectory(instance),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                    },
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
        );
        await ShowInstancePage(instance, "overview", body);
    }

    async Task DeleteInstance(Instance instance)
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
            info.Children.Add(T("Shared or dependent game files will be kept."));
        if (!await Dialog("Delete version?", info, true, danger: true))
            return;
        if (delete.IsChecked == true && inspection.DeletableDirectory is { } folder)
            await Trash(folder);
        session.Store.Config.Instances.Remove(instance);
        if (session.Selected == instance)
            session.Selected = session.Store.Config.Instances.FirstOrDefault();
        await session.Save();
        await Navigate("instances");
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
                            var destination = Stack(
                                Field("Migration destination", path),
                                Btn(
                                    "Choose parent folder",
                                    async () =>
                                    {
                                        var parent = await Folder();
                                        if (parent != null)
                                            path.Text = Path.Combine(parent, ".minecraft");
                                    }
                                )
                            );
                            if (!await Dialog("Migrate directory", destination, true))
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
