using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void EnsureGameFilesIdle()
    {
        if (game is { HasExited: false } || launching)
            throw new InvalidOperationException(
                Ui("Close the game before updating mods or restoring worlds.")
            );
    }

    async Task CheckModUpdates(Instance instance)
    {
        EnsureGameFilesIdle();
        var updates = new List<(string File, string Project, string Version, string Name)>();
        var failures = new List<string>();
        var recordsPath = Path.Combine(store.GameDirectory(instance), ".craft-mods-native.json");
        var cfFiles = File.Exists(recordsPath)
            ? JsonNode
                .Parse(await File.ReadAllTextAsync(recordsPath))!
                .Items()
                .Where(r => CurseForgeCommunityService.IsProject(r.S("project")))
                .Select(r => r.S("file"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await Run(
            async ct =>
            {
                foreach (
                    var item in ContentManager
                        .ListFiles(store.GameDirectory(instance), "mod")
                        .Where(i => i.Enabled)
                )
                {
                    Report(Ui("Checking mod updates") + " · " + item.File);
                    try
                    {
                        var path = ContentManager.Locate(
                            store.GameDirectory(instance),
                            "mod",
                            null,
                            item.File
                        );
                        var update = cfFiles.Contains(item.File)
                            ? await curseforge.FindTrackedModUpdate(instance, item.File, ct)
                            : await modrinth.FindModUpdate(instance, path, ct);
                        if (update is { } found)
                            updates.Add((item.File, found.Project, found.Version, found.Name));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(item.File + ": " + ex.Message);
                    }
                }
            },
            "Checking mod updates"
        );
        var list = new StackPanel { Spacing = 10 };
        list.Children.Add(
            Text(
                "Checks compatible Modrinth versions by file hash and tracked CurseForge installations. Disabled, unrecognized and up-to-date mods remain unchanged.",
                13
            )
        );
        if (updates.Count == 0)
            list.Children.Add(Text("No compatible updates found.", 13));
        foreach (var failure in failures)
            list.Children.Add(Text(failure, 12));
        foreach (var update in updates)
        {
            var status = Text("", 12);
            var completed = false;
            var button = Action(
                "Update",
                async (_, _) =>
                {
                    if (completed)
                        return;
                    try
                    {
                        EnsureGameFilesIdle();
                        await Run(
                            ct =>
                                modrinth.UpdateMod(
                                    instance,
                                    update.File,
                                    update.Project,
                                    update.Version,
                                    ct,
                                    CurseForgeCommunityService.IsProject(update.Project)
                                        ? (temporary, token) =>
                                            curseforge.InstallContent(
                                                temporary,
                                                update.Project,
                                                update.Version,
                                                "mod",
                                                null,
                                                token
                                            )
                                        : null
                                ),
                            "Update mod"
                        );
                        completed = true;
                        status.Text = Ui("Updated. Old files are in mod-backups.");
                    }
                    catch (Exception ex)
                    {
                        status.Text =
                            ex is OperationCanceledException ? Ui("Canceled") : ex.Message;
                    }
                }
            );
            list.Children.Add(Text(update.File + " → " + update.Name, 13));
            list.Children.Add(button);
            list.Children.Add(status);
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Mod updates"),
            Content = new ScrollViewer { Content = list, MaxHeight = 480 },
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
