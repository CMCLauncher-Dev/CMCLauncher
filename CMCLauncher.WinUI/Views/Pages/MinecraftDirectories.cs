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

    void MinecraftDirectories(StackPanel host)
    {
        var expander = new Expander
        {
            Header = Ui("Manage Minecraft directories"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        expander.Expanding += (_, _) => expander.Content ??= MinecraftDirectoriesBody();
        host.Children.Add(expander);
    }

    StackPanel MinecraftDirectoriesBody()
    {
        var manager = new MinecraftDirectoryManager(store);
        void EnsureIdle()
        {
            if (task != null || launching || game is { HasExited: false })
                throw new InvalidOperationException(
                    Ui(
                        "Close the game and finish active tasks before changing Minecraft directories."
                    )
                );
        }
        var body = new StackPanel { Spacing = 12 };
        var createPath = new TextBox
        {
            Header = Ui("New Minecraft directory"),
            PlaceholderText = @"D:\Games\Minecraft\.minecraft",
        };
        body.Children.Add(createPath);
        body.Children.Add(
            Buttons(
                Action(
                    "Choose parent folder",
                    async (_, _) =>
                    {
                        if (await PickMinecraftFolder() is { } parent)
                            createPath.Text = Path.Combine(parent, ".minecraft");
                    }
                ),
                Action(
                    "Create directory",
                    async (_, _) =>
                    {
                        EnsureIdle();
                        await manager.Create(createPath.Text);
                        await RenderAsync();
                    }
                ),
                Action(
                    "Add existing directory",
                    async (_, _) =>
                    {
                        EnsureIdle();
                        await ImportLocal();
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
            var row = new StackPanel { Spacing = 8 };
            var current = Path.GetFullPath(directory.Path)
                .TrimEnd('\\', '/')
                .Equals(
                    Path.GetFullPath(store.MinecraftRoot).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase
                );
            row.Children.Add(Text(directory.Path, 13, true));
            row.Children.Add(
                Text(
                    current ? "Default installation directory" : "Registered Minecraft directory",
                    11
                )
            );
            var use = Action(
                current ? "Current directory" : "Use for new installations",
                async (_, _) =>
                {
                    EnsureIdle();
                    await manager.Select(directory.Path);
                    await RenderAsync();
                }
            );
            use.IsEnabled = !current && Directory.Exists(directory.Path);
            row.Children.Add(
                Buttons(use, Action("Open folder", (_, _) => OpenFolder(directory.Path)))
            );
            if (manager.CanMigrate(directory))
            {
                var destination = new TextBox
                {
                    Header = Ui("Migration destination"),
                    PlaceholderText = Ui("Enter a new directory that does not exist yet"),
                };
                var migration = new StackPanel { Spacing = 10 };
                migration.Children.Add(destination);
                migration.Children.Add(
                    Buttons(
                        Action(
                            "Choose parent folder",
                            async (_, _) =>
                            {
                                if (await PickMinecraftFolder() is { } parent)
                                    destination.Text = Path.Combine(parent, ".minecraft");
                            }
                        ),
                        Action(
                            "Migrate directory",
                            async (_, _) =>
                            {
                                EnsureIdle();
                                if (dialogOpen)
                                    return;
                                dialogOpen = true;
                                try
                                {
                                    var dialog = new ContentDialog
                                    {
                                        XamlRoot = Content.XamlRoot,
                                        Title = Ui("Migrate this Minecraft directory?"),
                                        Content =
                                            directory.Path
                                            + "\n→ "
                                            + destination.Text
                                            + "\n\n"
                                            + Ui(
                                                "Close other launchers first. Files will be copied and verified before the old directory is removed."
                                            ),
                                        PrimaryButtonText = Ui("Migrate directory"),
                                        CloseButtonText = Ui("Cancel"),
                                        DefaultButton = ContentDialogButton.Close,
                                    };
                                    if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                                        return;
                                }
                                finally
                                {
                                    dialogOpen = false;
                                }
                                EnsureIdle();
                                var cleaned = false;
                                await Run(
                                    async ct =>
                                        cleaned = await manager.Migrate(
                                            directory,
                                            destination.Text,
                                            ct
                                        ),
                                    Ui("Migrate directory")
                                );
                                await RenderAsync();
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
                    )
                );
                row.Children.Add(
                    new Expander
                    {
                        Header = Ui("Migrate directory"),
                        Content = migration,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    }
                );
            }
            else
                row.Children.Add(
                    Text("External or unverified directory: migration is unavailable.", 11)
                );
            body.Children.Add(CompactCard(row));
        }
        return body;
    }
}
