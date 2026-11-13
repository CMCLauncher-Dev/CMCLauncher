using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Action<string> recycleInstanceDirectory = path =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
        );

    Func<ContentDialog, Task<ContentDialogResult>> showVersionDeletionDialog = async dialog =>
        await dialog.ShowAsync();

    string instanceDirectoryFilter = "";
    int instanceSort;

    // 两种布局使用相同筛选和排序，切换视图不会改变版本顺序。
    void Instances()
    {
        Header("切换版本", "");
        var directoryPaths = store
            .Config.Instances.Select(store.StorageDirectory)
            .Append(store.MinecraftRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();
        if (!directoryPaths.Contains(instanceDirectoryFilter, StringComparer.OrdinalIgnoreCase))
            instanceDirectoryFilter = "";
        var directories = new BelowComboBox
        {
            ItemsSource = new[] { Ui("All directories") }.Concat(directoryPaths).ToArray(),
            SelectedIndex =
                instanceDirectoryFilter.Length == 0
                    ? 0
                    : directoryPaths.FindIndex(p =>
                        string.Equals(
                            p,
                            instanceDirectoryFilter,
                            StringComparison.OrdinalIgnoreCase
                        )
                    ) + 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        directories.SelectionChanged += (_, _) =>
        {
            if (directories.SelectedIndex < 0)
                return;
            instanceDirectoryFilter =
                directories.SelectedIndex == 0 ? "" : directoryPaths[directories.SelectedIndex - 1];
            Render();
        };
        var directoryRow = new Grid { ColumnSpacing = 8 };
        directoryRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        directoryRow.Children.Add(directories);
        var directoryActions = Buttons(
            Action(
                "下载游戏",
                (_, _) =>
                {
                    page = "downloads";
                    Render();
                }
            ),
            Action("Import modpack", async (_, _) => await ImportPack())
        );
        Grid.SetColumn(directoryActions, 1);
        directoryRow.Children.Add(directoryActions);
        PageBody.Children.Add(directoryRow);
        var directoryTools = new Expander
        {
            Header = Ui("Manage Minecraft directories"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        directoryTools.Expanding += (_, _) =>
        {
            if (directoryTools.Content != null)
                return;
            var tools = MinecraftDirectoriesBody();
            tools.Children.Add(
                Buttons(
                    Action(
                        "Scan versions",
                        async (_, _) =>
                        {
                            await SetMinecraftFolder(store.MinecraftRoot);
                            Render();
                        }
                    ),
                    Action("Open folder", (_, _) => OpenFolder(store.MinecraftRoot)),
                    Action("Set folder", async (_, _) => await ImportLocal()),
                    Action("迁移其它启动器", async (_, _) => await MigrateForeignLauncher())
                )
            );
            directoryTools.Content = tools;
        };
        PageBody.Children.Add(directoryTools);
        var query = new TextBox { Text = instanceQuery };
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                instanceQuery = query.Text.Trim();
                Render();
            }
        };
        var filterIds = new[]
        {
            "全部实例",
            "收藏",
            "原版",
            "Fabric",
            "Forge",
            "NeoForge",
            "Quilt",
        };
        var filters = new[]
        {
            Ui("All versions"),
            Ui("Favorites"),
            Ui("Vanilla"),
            "Fabric",
            "Forge",
            "NeoForge",
            "Quilt",
        };
        var listMode = Ui("List");
        var tileModeName = Ui("Grid");
        instanceFilter = instanceFilter switch
        {
            "All versions" => "全部实例",
            "Favorites" => "收藏",
            "Vanilla" => "原版",
            _ => instanceFilter,
        };
        var filter = new BelowComboBox
        {
            ItemsSource = filters,
            SelectedIndex = Array.IndexOf(filterIds, instanceFilter),
            Width = 140,
        };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedIndex >= 0 && filterIds[filter.SelectedIndex] != instanceFilter)
            {
                instanceFilter = filterIds[filter.SelectedIndex];
                Render();
            }
        };
        var layout = new BelowComboBox
        {
            ItemsSource = new[] { listMode, tileModeName },
            SelectedIndex = store.Config.Settings.InstanceView == "tile" ? 1 : 0,
            Width = 120,
        };
        layout.SelectionChanged += async (_, _) =>
        {
            if (layout.SelectedIndex < 0)
                return;
            store.Config.Settings.InstanceView = layout.SelectedIndex == 1 ? "tile" : "list";
            await store.Save();
            Render();
        };
        var search = new Grid { ColumnSpacing = 8 };
        search.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var queryHost = new Grid();
        queryHost.Children.Add(query);
        // TextBox's native placeholder clips instead of trimming in a narrow column.
        var queryHint = Text("Search version name, description, or Minecraft version", 14);
        queryHint.TextWrapping = TextWrapping.NoWrap;
        queryHint.TextTrimming = TextTrimming.CharacterEllipsis;
        queryHint.VerticalAlignment = VerticalAlignment.Center;
        queryHint.Margin = new Thickness(12, 0, 12, 0);
        queryHint.IsHitTestVisible = false;
        queryHint.Opacity = .6;
        queryHint.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        query.TextChanged += (_, _) =>
            queryHint.Visibility =
                query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            query,
            Ui("Search version name, description, or Minecraft version")
        );
        queryHost.Children.Add(queryHint);
        search.Children.Add(queryHost);
        var find = Action(
            "搜索",
            (_, _) =>
            {
                instanceQuery = query.Text.Trim();
                Render();
            }
        );
        Grid.SetColumn(find, 1);
        search.Children.Add(find);
        Grid.SetColumn(filter, 2);
        search.Children.Add(filter);
        Grid.SetColumn(layout, 3);
        search.Children.Add(layout);
        PageBody.Children.Add(search);
        var sorting = new BelowComboBox
        {
            ItemsSource = new[]
            {
                Ui("Default (by loader)"),
                Ui("Recently played"),
                Ui("Name"),
                Ui("Minecraft version"),
            },
            SelectedIndex = instanceSort,
            Width = 190,
        };
        sorting.SelectionChanged += (_, _) =>
        {
            if (sorting.SelectedIndex < 0 || sorting.SelectedIndex == instanceSort)
                return;
            instanceSort = sorting.SelectedIndex;
            Render();
        };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(sorting, 4);
        search.Children.Add(sorting);
        var allFilter = instanceFilter is "全部实例" or "All versions";
        var favoriteFilter = instanceFilter is "收藏" or "Favorites";
        var loaderFilter = Ui(instanceFilter);
        var instances = store
            .Config.Instances.Where(i =>
                (
                    instanceDirectoryFilter.Length == 0
                    || string.Equals(
                        store.StorageDirectory(i),
                        instanceDirectoryFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                && (
                    allFilter
                    || (favoriteFilter ? i.Favorite : LoaderCategory(i.Loader) == loaderFilter)
                )
                && (
                    instanceQuery.Length == 0
                    || (i.Name + " " + i.Description + " " + i.GameVersion).Contains(
                        instanceQuery,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .OrderByDescending(i => i.Favorite)
            .ThenByDescending(i => instanceSort == 1 ? i.LastPlayed : null)
            .ThenBy(i => instanceSort == 3 ? i.GameVersion : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tileMode = store.Config.Settings.InstanceView == "tile";
        Grid? tileGrid = null;
        var tileIndex = 0;
        if (instances.Count == 0)
            PageBody.Children.Add(
                Text(
                    Ui(
                        "No matching versions. Download a game or select an existing .minecraft folder."
                    ),
                    13
                )
            );
        // Favorites have a dedicated, stable section before every sorted regular section.
        var sectionOrder = instances
            .Where(i => i.Favorite)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(instances.Where(i => !i.Favorite));
        foreach (
            var group in sectionOrder.GroupBy(i =>
                i.Favorite ? Ui("Favorites")
                : instanceSort == 0 ? LoaderCategory(i.Loader)
                : ""
            )
        )
        {
            if (tileMode)
            {
                tileGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
                var responsiveTiles = tileGrid;
                responsiveTiles.SizeChanged += (_, e) =>
                {
                    var columns = Math.Max(1, (int)((e.NewSize.Width + 8) / 220));
                    if (columns == responsiveTiles.ColumnDefinitions.Count)
                        return;
                    responsiveTiles.ColumnDefinitions.Clear();
                    for (var column = 0; column < columns; column++)
                        responsiveTiles.ColumnDefinitions.Add(new ColumnDefinition());
                    responsiveTiles.RowDefinitions.Clear();
                    for (var index = 0; index < responsiveTiles.Children.Count; index++)
                    {
                        if (index % columns == 0)
                            responsiveTiles.RowDefinitions.Add(
                                new RowDefinition { Height = GridLength.Auto }
                            );
                        Grid.SetRow(
                            (FrameworkElement)responsiveTiles.Children[index],
                            index / columns
                        );
                        Grid.SetColumn(
                            (FrameworkElement)responsiveTiles.Children[index],
                            index % columns
                        );
                    }
                };
                tileIndex = 0;
            }
            PageBody.Children.Add(
                Text(
                    (group.Key.Length > 0 ? group.Key + " · " : "")
                        + UiFormat("{0} versions", group.Count()),
                    13
                )
            );
            foreach (var instance in group)
            {
                var row = new Grid { ColumnSpacing = 12, MinHeight = 50 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                );
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(InstanceIcon(instance));
                var identity = new StackPanel
                {
                    Spacing = 3,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                identity.Children.Add(
                    Text(
                        instance.Name + (instance == selected ? " · " + Ui("Current") : ""),
                        14,
                        true
                    )
                );
                identity.Children.Add(
                    Text(
                        instance.Description.Length > 0
                            ? instance.Description
                            : $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)} {instance.LoaderVersion}",
                        12
                    )
                );
                if (instance.Description.Length > 0)
                {
                    var detail = Text(
                        $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)} {instance.LoaderVersion}",
                        11
                    );
                    detail.Opacity = .65;
                    identity.Children.Add(detail);
                }
                foreach (var line in identity.Children.OfType<TextBlock>())
                {
                    line.TextWrapping = TextWrapping.NoWrap;
                    line.TextTrimming = TextTrimming.CharacterEllipsis;
                }
                if (store.Config.Instances.Count(i => i.Name == instance.Name) > 1)
                    identity.Children.Add(Text(store.StorageDirectory(instance), 11));
                if (runningInstanceId == instance.Id && game is { HasExited: false })
                    identity.Children.Add(Text("Running", 11));
                if (!Directory.Exists(store.GameDirectory(instance)))
                    identity.Children.Add(Text("Directory unavailable", 11));
                Grid.SetColumn(identity, 1);
                row.Children.Add(identity);
                var favorite = new Button
                {
                    Content = new FontIcon
                    {
                        Glyph = instance.Favorite ? "\uE735" : "\uE734",
                        FontSize = 15,
                    },
                    Width = 30,
                    Height = 28,
                    Padding = new Thickness(0),
                    MinWidth = 30,
                };
                favorite.Click += async (_, _) =>
                {
                    instance.Favorite = !instance.Favorite;
                    await store.Save();
                    Render();
                };
                ToolTipService.SetToolTip(
                    favorite,
                    Ui(instance.Favorite ? "取消收藏" : "收藏版本")
                );
                var menu = new MenuFlyout();
                var settingsItem = new MenuFlyoutItem { Text = Ui("Settings") };
                settingsItem.Click += async (_, _) =>
                {
                    await SelectInstance(instance);
                    page = "version-settings";
                    kind = "overview";
                    Render();
                };
                var openItem = new MenuFlyoutItem { Text = Ui("Open folder") };
                openItem.Click += (_, _) => OpenFolder(store.GameDirectory(instance));
                var more = new Button
                {
                    Content = "⋯",
                    MinWidth = 32,
                    Flyout = menu,
                };
                ToolTipService.SetToolTip(more, Ui("More"));
                var deleteItem = new MenuFlyoutItem
                {
                    Text = Ui("Delete version"),
                    IsEnabled = task == null,
                };
                deleteItem.Foreground = Brush("ValidationError");
                deleteItem.Click += async (_, _) =>
                {
                    try
                    {
                        await ConfirmDeleteInstance(instance, more);
                    }
                    catch (Exception ex)
                    {
                        await Error(ex);
                    }
                };
                menu.Items.Add(settingsItem);
                menu.Items.Add(openItem);
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(deleteItem);
                var actions = Buttons(favorite, more);
                actions.AddHandler(
                    UIElement.TappedEvent,
                    new Microsoft.UI.Xaml.Input.TappedEventHandler((_, e) => e.Handled = true),
                    true
                );
                foreach (
                    var button in actions
                        .Children.OfType<Button>()
                        .Where(b => b.Content is not FontIcon)
                )
                {
                    button.MinWidth = 56;
                    button.MinHeight = 28;
                    button.Padding = new Thickness(8, 4, 8, 4);
                }
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
                if (tileMode)
                {
                    var icon = (FrameworkElement)row.Children[0];
                    row.Children.Clear();
                    row.ColumnDefinitions.Clear();
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row.RowDefinitions.Add(
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                    );
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row.RowSpacing = 10;
                    row.MinHeight = 0;
                    actions.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(actions, 0);
                    row.Children.Add(actions);
                    icon.Width = 64;
                    icon.Height = 64;
                    icon.HorizontalAlignment = HorizontalAlignment.Center;
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(icon, 1);
                    row.Children.Add(icon);
                    Grid.SetColumn(identity, 0);
                    Grid.SetRow(identity, 2);
                    identity.HorizontalAlignment = HorizontalAlignment.Stretch;
                    foreach (var text in identity.Children.OfType<TextBlock>())
                    {
                        text.TextAlignment = TextAlignment.Center;
                        text.TextWrapping = TextWrapping.NoWrap;
                        text.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                    row.Children.Add(identity);
                    more.MinWidth = 30;
                    more.Padding = new Thickness(4, 0, 4, 0);
                }
                else
                    AdaptCard(row, identity, actions);
                var card = CompactCard(row);
                if (tileMode)
                {
                    // Width comes from the responsive grid; height follows it to keep a true square.
                    card.SizeChanged += (_, e) =>
                    {
                        if (
                            e.NewSize.Width > 0 && Math.Abs(card.Height - e.NewSize.Width) > .5
                            || double.IsNaN(card.Height)
                        )
                            card.Height = e.NewSize.Width;
                    };
                }
                card.Tag = "instance-card";
                if (instance == selected)
                    card.BorderBrush = Brush(
                        lightTheme ? ThemeTokens.LightAccent : ThemeTokens.DarkAccent
                    );
                ToolTipService.SetToolTip(card, store.GameDirectory(instance));
                card.Tapped += async (_, _) =>
                {
                    if (page != "instances")
                        return;
                    await SelectInstance(instance);
                    page = "home";
                    Render();
                };
                if (tileMode && tileGrid != null)
                {
                    card.HorizontalAlignment = HorizontalAlignment.Stretch;
                    var rowIndex = tileIndex / 2;
                    while (tileGrid.RowDefinitions.Count <= rowIndex)
                        tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(card, rowIndex);
                    Grid.SetColumn(card, tileIndex % 2);
                    tileGrid.Children.Add(card);
                    tileIndex++;
                }
                else
                    PageBody.Children.Add(card);
            }
            if (tileMode && tileGrid != null)
                PageBody.Children.Add(tileGrid);
        }
    }

    string? DeletableInstanceDirectory(Instance instance) =>
        store.DeletableInstanceDirectory(instance);

    async Task ConfirmDeleteInstance(Instance instance, FrameworkElement anchor)
    {
        if (task != null)
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.wait_for_the_current_task_to_finish_or_cancel_it_before_deleting"
                )
            );
        var inspection = store.InspectVersionDeletion(instance);
        var directory = inspection.DeletableDirectory;
        var deleteFiles = new CheckBox
        {
            Content = Ui("Also delete version files (move to Recycle Bin)"),
            IsChecked = directory != null && Directory.Exists(directory),
            IsEnabled = directory != null && Directory.Exists(directory),
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text(UiFormat("Remove “{0}” from the launcher.", instance.Name), 14));
        content.Children.Add(deleteFiles);
        if (directory == null)
        {
            var related = string.Join(", ", inspection.RelatedVersions);
            var reason = inspection.Reason switch
            {
                VersionDeletionBlockReason.Dependency => UiFormat(
                    "Version files are kept because these versions reference this profile or client JAR: {0}. Remove or migrate those versions first.",
                    related
                ),
                VersionDeletionBlockReason.SharedDirectory => UiFormat(
                    "This version directory overlaps with: {0}. Only the launcher entry will be removed.",
                    related
                ),
                VersionDeletionBlockReason.UnreadableProfile => UiFormat(
                    "Cannot verify version references because a profile could not be read: {0}. Version files will be kept.",
                    related
                ),
                VersionDeletionBlockReason.UnsafePath => Ui(
                    "The version path could not be verified safely. Version files will be kept."
                ),
                _ => Ui(
                    "This path is not an individual version folder. Shared Minecraft files will be kept."
                ),
            };
            content.Children.Add(Text(reason, 12));
            if (inspection.Directory != null)
                content.Children.Add(Text(inspection.Directory, 12));
        }
        else
        {
            var path = Text(directory, 12);
            path.Opacity = .7;
            content.Children.Add(path);
        }
        if (dialogOpen)
            return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Delete version?"),
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = Math.Clamp(((FrameworkElement)Content).ActualHeight - 240, 120, 480),
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            PrimaryButtonText = Ui("Delete version"),
            CloseButtonText = Ui("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.PrimaryButtonStyle = new Style(typeof(Button))
        {
            BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"],
            Setters =
            {
                new Setter(Control.ForegroundProperty, versionDeleteText),
                new Setter(Control.BackgroundProperty, versionDeleteBackground),
                new Setter(Control.BorderBrushProperty, versionDeleteText),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(5)),
            },
        };
        dialog.Opened += (_, _) =>
        {
            var primary = Descendants(dialog)
                .OfType<Button>()
                .FirstOrDefault(button => button.Name == "PrimaryButton");
            if (primary != null)
                VersionDeleteButton(primary);
        };
        if (store.Config.Settings.ColorPreset != "native")
        {
            dialog.Background = ComboSurfaceBrush;
            dialog.Foreground = ComboTextBrush;
            dialog.Resources["ContentDialogBackground"] = ComboSurfaceBrush;
            dialog.Resources["ContentDialogForeground"] = ComboTextBrush;
        }
        dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await showVersionDeletionDialog(dialog);
        }
        finally
        {
            dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary)
            return;
        if (task != null)
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.wait_for_the_current_task_to_finish_or_cancel_it_before_deleting"
                )
            );
        if (
            deleteFiles.IsChecked == true
            && !string.Equals(
                directory,
                DeletableInstanceDirectory(instance),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.the_version_directory_has_changed_please_confirm_deletion_again"
                )
            );
        await DeleteInstance(instance, deleteFiles.IsChecked == true ? directory : null);
    }

    // 先保存配置再移入回收站；出错时恢复列表位置和选中项。
    async Task DeleteInstance(Instance instance, string? directory)
    {
        var index = store.Config.Instances.IndexOf(instance);
        if (index < 0)
            return;
        var previousSelected = selected;
        var previousSelectedId = store.Config.SelectedInstanceId;
        store.Config.Instances.RemoveAt(index);
        if (selected?.Id == instance.Id)
        {
            selected = store.Config.Instances.FirstOrDefault();
            world = null;
        }
        store.Config.SelectedInstanceId = selected?.Id;
        try
        {
            await store.Save();
            if (directory != null && Directory.Exists(directory))
                recycleInstanceDirectory(directory);
        }
        catch
        {
            store.Config.Instances.Insert(Math.Min(index, store.Config.Instances.Count), instance);
            selected = previousSelected;
            store.Config.SelectedInstanceId = previousSelectedId;
            await store.Save();
            throw;
        }
        Report(Ui("Version deleted") + "：" + instance.Name);
        ShowToast(Ui("Version deleted") + " · " + instance.Name);
        if (page == "version-settings")
            page = selected == null ? "home" : "instances";
        Render();
    }

    UIElement InstanceIcon(Instance instance)
    {
        var candidates = new[]
        {
            instance.IconPath,
            Path.Combine(store.GameDirectory(instance), "icon.png"),
            Path.Combine(store.GameDirectory(instance), "PCL", "Logo.png"),
        };
        foreach (var file in candidates)
            if (file != null && File.Exists(file))
                return new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(file))),
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                };
        if (instance.Loader == "vanilla")
        {
            var grass = Path.Combine(AppContext.BaseDirectory, "assets", "grass-block.png");
            if (File.Exists(grass))
                return new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(grass))),
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                };
        }
        var logo = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "loaders",
            instance.Loader + ".png"
        );
        if (File.Exists(logo))
            return new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(logo))),
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform,
            };
        var (color, wordmark) = instance.Loader switch
        {
            "fabric" => ("#A4927C", "F"),
            "forge" => ("#547DA5", "FORGE"),
            "neoforge" => ("#C56B3D", "NEO"),
            "quilt" => ("#916BB7", "Q"),
            _ => ("#657080", "MC"),
        };
        return new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(RawColor(color)),
            Child = new TextBlock
            {
                Text = wordmark,
                FontSize = wordmark.Length > 2 ? 8 : 21,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    async Task<string?> PickMinecraftFolder()
    {
        // unpackaged 下 FolderPicker 抛 E_FAIL，改用 Win32 经典对话框。
        return await Task.FromResult(FolderPickerNative.PickFolder(this));
    }

    // 扫描只登记已有版本，不移动原目录；单个损坏版本不会中断其他版本导入。
    async Task SetMinecraftFolder(string path)
    {
        await Run(async ct =>
        {
            await core.RefreshImportedMetadata(ct);
            var scan = await core.ScanLocalInstances(path, ct, AddLog);
            var root = scan.Root;
            var pending = scan.Instances;
            var failed = scan.Failed;
            var previousRoot = store.Config.Settings.MinecraftRoot;
            var addedDirectory = !store.Config.MinecraftDirectories.Any(d =>
                string.Equals(
                    Path.GetFullPath(d.Path).TrimEnd('\\', '/'),
                    Path.GetFullPath(root).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                ? new MinecraftDirectory { Path = Path.GetFullPath(root) }
                : null;
            if (addedDirectory != null)
                store.Config.MinecraftDirectories.Add(addedDirectory);
            store.Config.Settings.MinecraftRoot = root;
            store.Config.Instances.AddRange(pending);
            try
            {
                await store.Save();
            }
            catch
            {
                store.Config.Settings.MinecraftRoot = previousRoot;
                if (addedDirectory != null)
                    store.Config.MinecraftDirectories.Remove(addedDirectory);
                foreach (var instance in pending)
                    store.Config.Instances.Remove(instance);
                throw;
            }
            Report(
                UiFormat("Minecraft folder set, found {0} new versions", pending.Count)
                    + (
                        failed > 0
                            ? UiFormat(
                                "，{0} versions failed to read, see the log for details",
                                failed
                            )
                            : ""
                    )
            );
        });
    }

    // 名称修改只影响展示；新图标复制到配置目录，避免依赖用户原图片的位置。
    Func<ContentDialog, Task<ContentDialogResult>> showInstanceEditDialog = async dialog =>
        await dialog.ShowAsync();

    async Task EditInstance(string field)
    {
        if (selected is not { } instance || dialogOpen)
            return;
        var title = field switch
        {
            "name" => "Edit name",
            "description" => "Edit description",
            "icon" => "Edit icon",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var name = new TextBox
        {
            Header = Ui("Version name"),
            Text = instance.Name,
            MaxLength = 80,
        };
        var description = new TextBox
        {
            Header = Ui("Description"),
            Text = instance.Description,
            MaxLength = 240,
            AcceptsReturn = true,
        };
        var iconPath = instance.IconPath;
        var iconLabel = Text(iconPath == null ? "使用默认图标" : Path.GetFileName(iconPath), 12);
        var chooseIcon = new Button { Content = Ui("Choose image") };
        chooseIcon.Click += async (_, _) =>
        {
            var picker = new DesktopFileOpenPicker();
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".ico" })
                picker.FileTypeFilter.Add(ext);
            DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                iconPath = file.Path;
                iconLabel.Text = file.Name;
            }
        };
        var reset = new Button { Content = Ui("Restore default icon") };
        reset.Click += (_, _) =>
        {
            iconPath = null;
            iconLabel.Text = Ui("Use default icon");
        };
        var panel = new StackPanel { Spacing = 10 };
        if (field == "name")
            panel.Children.Add(name);
        else if (field == "description")
            panel.Children.Add(description);
        else
        {
            panel.Children.Add(iconLabel);
            panel.Children.Add(Buttons(chooseIcon, reset));
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui(title),
            Content = panel,
            PrimaryButtonText = Ui("Save"),
            CloseButtonText = Ui("Cancel"),
        };
        if (field == "name")
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
            name.TextChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
        }
        dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await showInstanceEditDialog(dialog);
        }
        finally
        {
            dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary)
            return;
        if (field == "name" && string.IsNullOrWhiteSpace(name.Text))
            throw new Exception("版本名称不能为空");
        if (field == "icon" && iconPath != null && iconPath != instance.IconPath)
        {
            if (new FileInfo(iconPath).Length > 8 * 1024 * 1024)
                throw new Exception("图标请使用 8 MB 以内的图片");
            var destination = Store.SafePath(
                store.Root,
                "instance-icons/"
                    + instance.Id
                    + "-"
                    + Guid.NewGuid().ToString("N")
                    + Path.GetExtension(iconPath)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(iconPath, destination);
            iconPath = destination;
        }
        if (field == "name")
            instance.Name = name.Text.Trim();
        else if (field == "description")
            instance.Description = description.Text.Trim();
        else
            instance.IconPath = iconPath;
        await store.Save();
        Render();
    }
}
