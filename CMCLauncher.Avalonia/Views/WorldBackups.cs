using Avalonia.Controls;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    string BackupDirectory(Instance instance) =>
        Store.SafePath(session.Store.Root, "world-backups/" + instance.Id);

    async Task BackupWorld(Instance instance, string world, string folder)
    {
        session.EnsureFilesIdle();
        var root = BackupDirectory(instance);
        Directory.CreateDirectory(root);
        var destination = Store.SafePath(
            root,
            world
                + "-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-"
                + Guid.NewGuid().ToString("N")[..6]
                + ".zip"
        );
        await session.Run("Back up world", ct => WorldArchive.Export(folder, destination, ct));
    }

    Task WorldBackupsPage(Instance instance)
    {
        var root = BackupDirectory(instance);
        Directory.CreateDirectory(root);
        var body = Stack(
            Btn("Back", () => WorldsPage(instance)),
            Btn(
                "Open folder",
                () =>
                {
                    Open(root);
                    return Task.CompletedTask;
                }
            )
        );
        foreach (
            var file in Directory
                .GetFiles(root, "*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
        )
            body.Children.Add(
                Card(
                    Stack(
                        T(Path.GetFileName(file), 15, true),
                        T(new FileInfo(file).Length / 1048576.0 + " MiB", 12),
                        Row(
                            Btn(
                                "Restore as a new world",
                                async () =>
                                {
                                    session.EnsureFilesIdle();
                                    var name = Input(
                                        "Restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                                    );
                                    if (!await Dialog("World name", name, true))
                                        return;
                                    var destination = Store.SafePath(
                                        session.Store.GameDirectory(instance),
                                        "saves/" + name.Text!.Trim()
                                    );
                                    await session.Run(
                                        "Restore world backup",
                                        ct =>
                                            WorldArchive.Import(
                                                file,
                                                destination,
                                                WorldArchiveRoot(file),
                                                ct
                                            )
                                    );
                                    await WorldsPage(instance);
                                }
                            ),
                            DeleteFile(file, () => WorldBackupsPage(instance))
                        )
                    )
                )
            );
        if (!Directory.EnumerateFiles(root, "*.zip").Any())
            body.Children.Add(T("No world backups yet."));
        pageContent.Content = body;
        return Task.CompletedTask;
    }
}
