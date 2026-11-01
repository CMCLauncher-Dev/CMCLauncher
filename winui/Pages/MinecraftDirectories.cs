using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
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
