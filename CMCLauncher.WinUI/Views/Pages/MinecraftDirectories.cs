using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task SetupMinecraftDirectory()
    {
        var path = new TextBox
        {
            Header = Ui("Minecraft directory"),
            Text = Path.Combine(
                Program.WritableDirectory == Program.UserDirectory
                    ? Program.UserDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft"
            ),
        };
        var body = new StackPanel { Spacing = 12, MinWidth = 320 };
        body.Children.Add(
            Text("Choose where CMCLauncher will create .minecraft for new game installations.", 13)
        );
        var location = new BelowComboBox
        {
            ItemsSource = new[] { Ui("Launcher directory"), Ui("AppData") },
            SelectedIndex = Program.WritableDirectory == Program.UserDirectory ? 0 : 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        location.SelectionChanged += (_, _) =>
        {
            path.Text = Path.Combine(
                location.SelectedIndex == 1
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Program.UserDirectory,
                ".minecraft"
            );
        };
        body.Children.Add(location);
        body.Children.Add(path);
        var browse = new Button { Content = Ui("Choose parent folder") };
        browse.Click += async (_, _) =>
        {
            if (await PickMinecraftFolder() is { } parent)
                path.Text = Path.Combine(parent, ".minecraft");
        };
        body.Children.Add(browse);
        var error = Text("", 12);
        error.Foreground = Brush("ValidationError");
        error.Visibility = Visibility.Collapsed;
        body.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Set up your Minecraft directory"),
            Content = body,
            PrimaryButtonText = Ui("Next"),
            CloseButtonText = Ui("Later"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true;
            var deferral = e.GetDeferral();
            try
            {
                var destination = Path.GetFullPath(path.Text.Trim());
                if (Path.GetFileName(destination) != ".minecraft")
                    throw new IOException(Ui("Choose a new folder named .minecraft."));
                var manager = new MinecraftDirectoryManager(store);
                var owned = manager
                    .List()
                    .FirstOrDefault(r =>
                        string.Equals(
                            Path.GetFullPath(r.Path),
                            destination,
                            StringComparison.OrdinalIgnoreCase
                        ) && manager.CanMigrate(r)
                    );
                // Existing AppData Minecraft folders stay external; selecting one never claims ownership.
                if (owned == null && !Directory.Exists(destination))
                    await manager.Create(destination);
                await manager.Select(destination);
                store.Config.Settings.MinecraftDirectorySetupPending = false;
                try
                {
                    await store.Save();
                }
                catch
                {
                    store.Config.Settings.MinecraftDirectorySetupPending = true;
                    throw;
                }
                e.Cancel = false;
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                deferral.Complete();
            }
        };
        ContentDialogResult result;
        dialogOpen = true;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary)
            return;
        var migration = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Import instances from another launcher?"),
            Content = Text(
                "Choose an existing PCL/HMCL Minecraft folder, or migrate Modrinth App and MultiMC/Prism instances. You can also do this later.",
                13
            ),
            PrimaryButtonText = Ui("PCL / HMCL"),
            SecondaryButtonText = Ui("Modrinth / MultiMC / Prism"),
            CloseButtonText = Ui("Skip"),
        };
        dialogOpen = true;
        try
        {
            result = await migration.ShowAsync();
        }
        finally
        {
            dialogOpen = false;
        }
        try
        {
            if (result == ContentDialogResult.Primary)
                await ImportLocal();
            else if (result == ContentDialogResult.Secondary)
                await MigrateForeignLauncher();
        }
        catch (Exception ex)
        {
            await Error(ex);
        }
    }

    Button? directoryDrawerOpener;

    void OpenMinecraftDirectoryDrawer(Button opener)
    {
        OpenCommunityFilterDrawer(
            MinecraftDirectoriesBody(),
            opener,
            () => Task.CompletedTask,
            () => { }
        );
        CommunityFilterDrawerTitle.Text = Ui("Manage Minecraft directories");
        CommunityFilterDrawerHint.Text = "";
        CommunityFilterDrawerFooter.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(CommunityFilterCloseButton, Ui("Close"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            CommunityFilterCloseButton,
            Ui("Close")
        );
    }

    async Task RefreshMinecraftDirectoryDrawer()
    {
        if (page != "instances")
            return;
        var preserve =
            CommunityFilterDrawer.IsPaneOpen
            && ReferenceEquals(communityFilterReturnFocus, directoryDrawerOpener);
        var offset = CommunityFilterDrawerScroll.VerticalOffset;
        await RenderAsync(preserveDirectoryDrawer: preserve);
        if (!preserve || !CommunityFilterDrawer.IsPaneOpen || page != "instances")
            return;
        communityFilterReturnFocus = directoryDrawerOpener;
        CommunityFilterDrawerBody.Content = MinecraftDirectoriesBody();
        CommunityFilterDrawerScroll.UpdateLayout();
        CommunityFilterDrawerScroll.ChangeView(null, offset, null, true);
    }

    void EnsureMinecraftDirectoriesIdle()
    {
        if (task != null || AnyGameLaunching || runningGames.Any)
            throw new InvalidOperationException(
                Ui("Close the game and finish active tasks before changing Minecraft directories.")
            );
    }

    async Task<string?> ChooseMinecraftDestination(
        string title,
        string? source = null,
        bool keepSource = false
    )
    {
        if (dialogOpen)
            return null;
        var path = new TextBox
        {
            Header = Ui(source == null ? "New directory path" : "Migration destination"),
            PlaceholderText = Ui("Enter a new directory that does not exist yet"),
            MinHeight = 36,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var fields = new StackPanel { Spacing = 12, MinWidth = 320 };
        if (source != null)
        {
            fields.Children.Add(Text(source));
            fields.Children.Add(
                Text(
                    keepSource
                        ? "Copy this directory and use the copy for recognized instances. The source is kept."
                        : "Close other launchers first. Files will be copied and verified before the old directory is removed."
                )
            );
        }
        fields.Children.Add(path);
        var browse = new Button { Content = Ui("Choose parent folder") };
        browse.Click += async (_, _) =>
        {
            if (await PickMinecraftFolder() is { } parent)
                path.Text = Path.Combine(parent, ".minecraft");
        };
        fields.Children.Add(browse);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Ui(title),
            Content = fields,
            PrimaryButtonText = Ui(source == null ? "Create directory" : "Migrate directory"),
            CloseButtonText = Ui("Cancel"),
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close,
        };
        path.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(path.Text);
        dialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? path.Text.Trim()
                : null;
        }
        finally
        {
            dialogOpen = false;
        }
    }

    StackPanel MinecraftDirectoriesBody()
    {
        var manager = new MinecraftDirectoryManager(store);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(
            Buttons(
                Action(
                    "Create directory",
                    async (_, _) =>
                    {
                        EnsureMinecraftDirectoriesIdle();
                        if (
                            await ChooseMinecraftDestination("Create directory")
                            is not { } destination
                        )
                            return;
                        EnsureMinecraftDirectoriesIdle();
                        await manager.Create(destination);
                        await RefreshMinecraftDirectoryDrawer();
                    }
                ),
                Action(
                    "Migrate other launchers",
                    async (_, _) =>
                    {
                        EnsureMinecraftDirectoriesIdle();
                        await MigrateForeignLauncher(fromDirectoryDrawer: true);
                    }
                )
            )
        );
        body.Children.Add(
            Text(
                "Selecting a directory changes where new games are installed. Existing instances keep their own paths.",
                12
            )
        );

        foreach (var directory in manager.List())
        {
            var current = string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.MinecraftRoot)),
                StringComparison.OrdinalIgnoreCase
            );
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(Text(directory.Path, 13, true));
            row.Children.Add(
                Text(
                    current ? "Default installation directory" : "Registered Minecraft directory",
                    11
                )
            );
            var actions = new FlowPanel();
            actions.Children.Add(Action("Open folder", (_, _) => OpenFolder(directory.Path)));
            actions.Children.Add(
                Action(
                    "Scan versions",
                    async (_, _) =>
                    {
                        EnsureMinecraftDirectoriesIdle();
                        await SetMinecraftFolder(directory.Path, selectDefault: false);
                        await RefreshMinecraftDirectoryDrawer();
                    }
                )
            );
            if (!current && Directory.Exists(directory.Path))
                actions.Children.Add(
                    Action(
                        "Use for new installations",
                        async (_, _) =>
                        {
                            EnsureMinecraftDirectoriesIdle();
                            await manager.Select(directory.Path);
                            await RefreshMinecraftDirectoryDrawer();
                        }
                    )
                );
            var keepSource = !manager.CanMigrate(directory);
            if (!keepSource || manager.CanCopyForMigration(directory))
                actions.Children.Add(
                    Action(
                        "Migrate directory",
                        async (_, _) =>
                        {
                            EnsureMinecraftDirectoriesIdle();
                            if (
                                await ChooseMinecraftDestination(
                                    "Migrate this Minecraft directory?",
                                    directory.Path,
                                    keepSource
                                )
                                is not { } destination
                            )
                                return;
                            EnsureMinecraftDirectoriesIdle();
                            var cleaned = false;
                            await Run(
                                async ct =>
                                    cleaned = await manager.Migrate(
                                        directory,
                                        destination,
                                        ct,
                                        keepSource
                                    ),
                                Ui("Migrate directory")
                            );
                            await RefreshMinecraftDirectoryDrawer();
                            if (!cleaned)
                                await Error(
                                    new IOException(
                                        Ui(
                                            "Migration finished, but some old files could not be removed. The new directory is active."
                                        )
                                    )
                                );
                        }
                    )
                );
            else
                row.Children.Add(
                    Text("External or unverified directory: migration is unavailable.", 11)
                );
            if (manager.CanRemove(directory))
                actions.Children.Add(
                    Action(
                        "Remove directory",
                        async (_, _) =>
                        {
                            EnsureMinecraftDirectoriesIdle();
                            if (dialogOpen)
                                return;
                            var trash = new CheckBox
                            {
                                Content = Ui("Also move the folder to the recycle bin"),
                                Visibility = manager.CanTrash(directory)
                                    ? Visibility.Visible
                                    : Visibility.Collapsed,
                            };
                            var fields = new StackPanel { Spacing = 10 };
                            fields.Children.Add(Text(directory.Path));
                            fields.Children.Add(trash);
                            var dialog = new ContentDialog
                            {
                                XamlRoot = Content.XamlRoot,
                                Title = Ui("Remove directory"),
                                Content = fields,
                                PrimaryButtonText = Ui("Remove"),
                                CloseButtonText = Ui("Cancel"),
                                DefaultButton = ContentDialogButton.Close,
                            };
                            dialogOpen = true;
                            try
                            {
                                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                                    return;
                            }
                            finally
                            {
                                dialogOpen = false;
                            }
                            EnsureMinecraftDirectoriesIdle();
                            await manager.Remove(directory, trash.IsChecked == true);
                            await RefreshMinecraftDirectoryDrawer();
                        }
                    )
                );
            row.Children.Add(actions);
            body.Children.Add(CompactCard(row));
        }
        return body;
    }
}
