using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task ModpackExportPage()
    {
        Header("Export modpack", "");
        if (selected is not { } instance)
            return;
        var generation = renderVersion;
        var root = store.GameDirectory(instance);
        var nameBox = new TextBox
        {
            Header = Ui("Modpack name"),
            Text = instance.Name,
            MaxLength = 100,
        };
        var versionBox = new TextBox
        {
            Header = Ui("Modpack version"),
            Text = "1.0.0",
            MaxLength = 50,
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.Children.Add(nameBox);
        Grid.SetColumn(versionBox, 1);
        row.Children.Add(versionBox);
        PageBody.Children.Add(row);
        var formatBox = new ComboBox
        {
            Header = Ui("Modpack format"),
            ItemsSource = new[] { "Modrinth (.mrpack)", "CurseForge (.zip)" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var includeResources = new CheckBox
        {
            Content = Ui("Include resource files"),
            IsChecked = true,
        };
        PageBody.Children.Add(formatBox);
        PageBody.Children.Add(includeResources);
        PageBody.Children.Add(
            Text("Bundled mods, resource packs and shaders do not need downloading on import.", 12)
        );
        PageBody.Children.Add(Text("Select content to export", 16, true));
        PageBody.Children.Add(
            Text(
                "Select files to export. Saves, screenshots and server lists are not selected by default. Check redistribution permissions before sharing.",
                12
            )
        );
        var tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Multiple,
            CanDragItems = false,
            CanReorderItems = false,
        };
        tree.MaxHeight = Math.Clamp(PageViewport.ActualHeight * .5, 180, 420);
        tree.HorizontalAlignment = HorizontalAlignment.Stretch;
        PageBody.Children.Add(Card(tree));
        var status = new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Message = Ui("Loading files…"),
            Severity = InfoBarSeverity.Informational,
        };
        PageBody.Children.Add(status);
        var filesByNode = new Dictionary<TreeViewNode, string>();
        var nodes = new Dictionary<string, TreeViewNode>(StringComparer.Ordinal);
        var packButton = Action(
            "Start packaging",
            async (_, _) =>
            {
                EnsureGameFilesIdle();
                if (
                    string.IsNullOrWhiteSpace(nameBox.Text)
                    || string.IsNullOrWhiteSpace(versionBox.Text)
                )
                {
                    status.Message = Ui("Modpack name and version are required.");
                    status.Severity = InfoBarSeverity.Warning;
                    status.IsOpen = true;
                    return;
                }
                var chosen = new HashSet<string>(StringComparer.Ordinal);
                void Collect(TreeViewNode node)
                {
                    if (filesByNode.TryGetValue(node, out var file))
                        chosen.Add(file);
                    foreach (var child in node.Children)
                        Collect(child);
                }
                foreach (var node in tree.SelectedNodes)
                    Collect(node);
                var picker = new FileSavePicker
                {
                    SuggestedFileName = instance.Name + "-" + versionBox.Text.Trim(),
                };
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var format = formatBox.SelectedIndex == 0 ? "modrinth" : "curseforge";
                var bundle = includeResources.IsChecked == true;
                picker.FileTypeChoices.Add(
                    format == "modrinth" ? "Modrinth modpack" : "CurseForge modpack",
                    [format == "modrinth" ? ".mrpack" : ".zip"]
                );
                var destination = await picker.PickSaveFileAsync();
                if (destination == null)
                    return;
                var name = nameBox.Text.Trim();
                var version = versionBox.Text.Trim();
                await Run(
                    ct =>
                        Task.Run(
                            () =>
                                ModpackExporter.Export(
                                    root,
                                    instance,
                                    name,
                                    version,
                                    chosen,
                                    destination.Path,
                                    ct,
                                    overwrite: true,
                                    format: format,
                                    includeResources: bundle,
                                    resolveReference: (relative, token) =>
                                        ModpackExportReferences.Resolve(
                                            core,
                                            root,
                                            relative,
                                            format,
                                            token
                                        )
                                ),
                            ct
                        ),
                    "Export modpack"
                );
            },
            primary: true
        );
        packButton.IsEnabled = false;
        packButton.HorizontalAlignment = HorizontalAlignment.Right;
        packButton.MinHeight = 44;
        packButton.MinWidth = 160;
        packButton.FontSize = 14;
        packButton.Padding = new Thickness(20, 8, 20, 8);
        packButton.Margin = new Thickness(0, 12, 0, 0);
        PageBody.Children.Add(packButton);
        // Mount the page before scanning so navigation never waits for the filesystem.
        if (pageFrameReady)
            NavigatePage(PageBody);
        else
            pendingPageBody = PageBody;
        resourceSearch = new CancellationTokenSource();
        var scanToken = resourceSearch.Token;
        try
        {
            var files = await Task.Run(() => ModpackExporter.List(root, scanToken), scanToken);
            var loaded = 0;
            if (generation != renderVersion)
                return;
            foreach (var file in files)
            {
                scanToken.ThrowIfCancellationRequested();
                if (++loaded % 128 == 0)
                {
                    await Task.Delay(1, scanToken);
                    if (generation != renderVersion)
                        return;
                }
                TreeViewNode? parent = null;
                var path = "";
                foreach (var part in file.Split('/'))
                {
                    path = path.Length == 0 ? part : path + "/" + part;
                    if (!nodes.TryGetValue(path, out var node))
                    {
                        var label =
                            parent == null
                                ? part switch
                                {
                                    "mods" => "export.folder.mods",
                                    "config" => "export.folder.config",
                                    "defaultconfigs" => "export.folder.defaultconfigs",
                                    "resourcepacks" => "export.folder.resourcepacks",
                                    "shaderpacks" => "export.folder.shaderpacks",
                                    "saves" => "export.folder.saves",
                                    "screenshots" => "export.folder.screenshots",
                                    "schematics" => "export.folder.schematics",
                                    "servers.dat" => "export.folder.servers",
                                    "options.txt" => "export.folder.options",
                                    "optionsof.txt" => "export.folder.optifine",
                                    "optionsshaders.txt" => "export.folder.shaderoptions",
                                    "tacz" => "export.folder.tacz",
                                    _ => part,
                                }
                                : part;
                        node = new TreeViewNode { Content = label == part ? part : Ui(label) };
                        nodes.Add(path, node);
                        if (parent == null)
                            tree.RootNodes.Add(node);
                        else
                            parent.Children.Add(node);
                    }
                    parent = node;
                }
                filesByNode[parent!] = file;
            }
            loaded = 0;
            foreach (var file in filesByNode)
            {
                scanToken.ThrowIfCancellationRequested();
                if (++loaded % 128 == 0)
                {
                    await Task.Delay(1, scanToken);
                    if (generation != renderVersion)
                        return;
                }
                var first = file.Value.Split('/')[0];
                if (first is not ("saves" or "screenshots" or "servers.dat"))
                    tree.SelectedNodes.Add(file.Key);
            }
            status.IsOpen = false;
            packButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (scanToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != renderVersion)
                return;
            status.Severity = InfoBarSeverity.Error;
            status.Message = ex.Message;
        }
    }
}
