using CMCLauncher.Core.Minecraft.DataPacks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    DataPackOptions dataPackBasicDraft = new("", "", Namespace: "my_pack");

    void DataPackGeneratorPage()
    {
        Header("Data pack generator", "");
        var body = new StackPanel { Spacing = 12 };
        var wizard = new ContentControl
        {
            Content = body,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var heading = Text("", 18, true);
        body.Children.Add(heading);
        var name = new TextBox
        {
            Header = Ui("Data pack name"),
            Text = dataPackBasicDraft.Name,
            MaxLength = 100,
            PlaceholderText = Ui("Data pack name"),
        };
        var ns = new TextBox
        {
            Header = Ui("Namespace"),
            Text = dataPackBasicDraft.Namespace,
            MaxLength = 100,
        };
        var version = new ComboBox
        {
            Header = Ui("Minecraft version"),
            ItemsSource = DataPackGenerator.SupportedGameVersions.Reverse().ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = Ui("Select a game version"),
        };
        name.Tag = "datapack-name";
        ns.Tag = "datapack-namespace";
        version.Tag = "datapack-version";
        var format = Text("");
        version.SelectionChanged += (_, _) =>
        {
            if (version.SelectedItem is string value)
            {
                var (major, minor) = DataPackGenerator.GetDataPackFormat(value);
                format.Text =
                    Ui("Data pack format") + ": " + major + (minor == 0 ? "" : "." + minor);
            }
        };
        var description = new TextBox
        {
            Header = Ui("Description"),
            Text = dataPackBasicDraft.Description,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxLength = 4000,
        };
        void SaveBasic() =>
            dataPackBasicDraft = new(
                name.Text,
                version.SelectedItem as string ?? "",
                description.Text,
                ns.Text
            );
        name.TextChanged += (_, _) => SaveBasic();
        ns.TextChanged += (_, _) => SaveBasic();
        description.TextChanged += (_, _) => SaveBasic();
        version.SelectionChanged += (_, _) => SaveBasic();
        version.SelectedItem = dataPackBasicDraft.GameVersion;
        var basics = new StackPanel { Spacing = 10 };
        foreach (
            var item in new UIElement[]
            {
                name,
                ns,
                Text(
                    "Use lowercase letters, numbers, underscores, hyphens or dots. Choose your own namespace to avoid replacing vanilla content.",
                    12
                ),
                version,
                format,
                description,
            }
        )
            basics.Children.Add(item);
        var entryEditor = BuildDataPackEntryEditor(
            () => version.SelectedItem as string ?? "",
            () => ns.Text
        );
        var templates = entryEditor.View;
        var paths = new ComboBox
        {
            Header = Ui("Generated files"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        paths.Tag = "datapack-files";
        var content = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 180,
            MinHeight = 120,
            MaxHeight = 240,
        };
        var preview = new StackPanel { Spacing = 10 };
        preview.Children.Add(
            Text(
                "Review the generated files before exporting. Existing files will never be overwritten.",
                12
            )
        );
        preview.Children.Add(paths);
        preview.Children.Add(content);
        var zip = new CheckBox { Content = Ui("Export as ZIP"), IsChecked = true };

        body.Children.Add(basics);
        body.Children.Add(templates);
        body.Children.Add(preview);
        var status = new InfoBar { IsClosable = true };
        body.Children.Add(status);
        string? outputDirectory = null;
        var openOutput = Action(
            "Open folder",
            (_, _) =>
            {
                if (outputDirectory != null)
                    OpenFolder(outputDirectory);
            }
        );
        openOutput.Visibility = Visibility.Collapsed;
        body.Children.Add(openOutput);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var step = 0;
        IReadOnlyDictionary<string, string>? files = null;
        DataPackOptions? reviewed = null;
        DataPackOptions Read(bool includeTemplates) =>
            new(name.Text, version.SelectedItem as string ?? "", description.Text, ns.Text)
            {
                Entries = includeTemplates ? entryEditor.Read() : Array.Empty<DataPackEntry>(),
            };
        void Error(Exception ex)
        {
            status.Message = Ui(
                ex is ArgumentException argument
                    ? argument.ParamName switch
                    {
                        "packName" => "Enter a valid pack directory name.",
                        "packNamespace" =>
                            "Enter a valid namespace using lowercase letters, numbers, underscores, hyphens or dots.",
                        "gameVersion" => "Select a supported game version.",
                        _ => ex.Message,
                    }
                    : ex.Message
            );
            if (ex is DataPackJsonException json)
                status.Message +=
                    " " + string.Format(Ui("Line {0}, column {1}"), json.Line, json.Column);
            status.Severity = InfoBarSeverity.Error;
            status.IsOpen = true;
        }
        Button previous = null!,
            next = null!,
            export = null!;
        previous = Action(
            "Previous",
            (_, _) =>
            {
                if (step <= 0)
                    return;
                step--;
                Update();
            }
        );
        next = Action(
            "Next",
            (_, _) =>
            {
                try
                {
                    if (version.SelectedItem == null || string.IsNullOrWhiteSpace(name.Text))
                        throw new ArgumentException("Enter a pack name and select a game version.");
                    var options = Read(step == 1);
                    var generated = DataPackGenerator.Preview(options);
                    if (step == 1)
                    {
                        reviewed = options;
                        files = generated;
                        paths.ItemsSource = files.Keys.ToArray();
                        paths.SelectedIndex = 0;
                    }
                    step++;
                    Update();
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
            },
            true
        );
        export = Action(
            "Export data pack",
            async (_, _) =>
            {
                if (reviewed == null)
                    return;
                wizard.IsEnabled = false;
                try
                {
                    string? destination;
                    var asZip = zip.IsChecked == true;
                    if (asZip)
                    {
                        var picker = new DesktopFileSavePicker
                        {
                            SuggestedFileName = reviewed.Name,
                        };
                        DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                        picker.FileTypeChoices.Add("ZIP", new List<string> { ".zip" });
                        destination = (await picker.PickSaveFileAsync())?.Path;
                    }
                    else
                        destination = FolderPickerNative.PickFolder(this);
                    if (destination == null)
                        return;
                    var options = reviewed;
                    var target = await Task.Run(() =>
                        asZip
                            ? DataPackGenerator.ExportZip(destination, options)
                            : DataPackGenerator.CreateDataPack(destination, options)
                    );
                    outputDirectory = asZip ? Path.GetDirectoryName(target)! : target;
                    openOutput.Visibility = Visibility.Visible;
                    status.Message = Ui("Data pack exported") + ": " + target;
                    status.Severity = InfoBarSeverity.Success;
                    status.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
                finally
                {
                    wizard.IsEnabled = true;
                }
            },
            true
        );
        paths.SelectionChanged += (_, _) =>
            content.Text = paths.SelectedItem is string path && files != null ? files[path] : "";
        navigation.Children.Add(previous);
        navigation.Children.Add(next);
        navigation.Children.Add(zip);
        navigation.Children.Add(export);
        body.Children.Add(navigation);
        body.Children.Add(
            Text(
                "Install: put the exported ZIP or pack folder into your world's datapacks folder. Enter the world and run /reload, then /datapack list. Commands require permission. Back up your world before testing.",
                12
            )
        );
        body.Children.Add(
            Text(
                "Test your own entry IDs with /function namespace:path, /advancement grant @s only namespace:path or /loot give @s loot namespace:path where supported. Full source is not automatically converted when changing game versions.",
                12
            )
        );
        previous.Tag = "datapack-previous";
        next.Tag = "datapack-next";
        export.Tag = "datapack-export";
        void Update()
        {
            heading.Text = Ui(
                new[] { "1. Basic information", "2. Content editor", "3. Preview and export" }[step]
            );
            basics.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            templates.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            preview.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            previous.IsEnabled = step > 0;
            previous.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
            next.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
            export.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            zip.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            body.Children.Remove(navigation);
            body.Children.Insert(step == 2 ? 1 : body.Children.Count - 2, navigation);
            body.Children.Remove(status);
            body.Children.Insert(step == 2 ? 2 : 1, status);
            status.IsOpen = false;
            openOutput.Visibility = Visibility.Collapsed;
        }
        Update();
        PageBody.Children.Add(Card(wizard));
    }
}
