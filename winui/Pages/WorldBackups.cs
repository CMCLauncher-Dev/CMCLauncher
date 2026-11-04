using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    string WorldBackupDirectory(Instance instance) =>
        Store.SafePath(store.Root, "world-backups/" + instance.Id);

    async Task BackupWorld(Instance instance, string name, string folder)
    {
        EnsureGameFilesIdle();
        var root = WorldBackupDirectory(instance);
        Directory.CreateDirectory(root);
        var file = Store.SafePath(
            root,
            name
                + "-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-"
                + Guid.NewGuid().ToString("N")[..6]
                + ".zip"
        );
        await Run(ct => WorldArchive.Export(folder, file, ct), "Back up world");
    }

    async Task ShowWorldBackups(Instance instance)
    {
        var root = WorldBackupDirectory(instance);
        Directory.CreateDirectory(root);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(
            Text(
                "Backups are stored outside the game directory. Restoring creates a new world and never overwrites the original.",
                13
            )
        );
        body.Children.Add(Action("Open folder", (_, _) => OpenFolder(root)));
        var status = Text("", 12);
        body.Children.Add(status);
        foreach (
            var file in Directory
                .GetFiles(root, "*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
        )
        {
            body.Children.Add(Text(Path.GetFileName(file), 12));
            body.Children.Add(
                Action(
                    "Restore as a new world",
                    async (_, _) =>
                    {
                        try
                        {
                            EnsureGameFilesIdle();
                            var name =
                                "Restored-"
                                + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                                + "-"
                                + Guid.NewGuid().ToString("N")[..6];
                            var destination = Store.SafePath(
                                store.GameDirectory(instance),
                                "saves/" + name
                            );
                            await Run(
                                ct =>
                                    WorldArchive.Import(
                                        file,
                                        destination,
                                        Path.GetFileNameWithoutExtension(file),
                                        ct
                                    ),
                                "Restore world backup"
                            );
                            status.Text = destination;
                        }
                        catch (Exception ex)
                        {
                            status.Text =
                                ex is OperationCanceledException ? Ui("Canceled") : ex.Message;
                        }
                    }
                )
            );
        }
        if (!Directory.EnumerateFiles(root, "*.zip").Any())
            body.Children.Add(Text("No world backups yet.", 13));
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("World backups"),
            Content = new ScrollViewer { Content = body, MaxHeight = 480 },
            CloseButtonText = Ui("Close"),
        };
        dialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            dialogOpen = false;
        }
    }
}
