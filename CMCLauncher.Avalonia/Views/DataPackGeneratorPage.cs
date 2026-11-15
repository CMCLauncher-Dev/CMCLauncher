using Avalonia.Controls;
using CMCLauncher.Core.Minecraft.DataPacks;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    DataPackOptions dataPackBasicDraft = new("", "", Namespace: "my_pack");

    Control DataPackGeneratorPage()
    {
        var heading = T("", 18, true);
        var name = Input(dataPackBasicDraft.Name);
        name.MaxLength = 100;
        var ns = Input(dataPackBasicDraft.Namespace);
        ns.MaxLength = 100;
        var version = Choice(DataPackGenerator.SupportedGameVersions.Reverse());
        version.SelectedIndex = -1;
        version.PlaceholderText = Ui("Select a game version");
        name.Tag = "datapack-name";
        ns.Tag = "datapack-namespace";
        version.Tag = "datapack-version";
        var format = T("");
        version.SelectionChanged += (_, _) =>
        {
            if (version.SelectedItem is string value)
            {
                var (major, minor) = DataPackGenerator.GetDataPackFormat(value);
                format.Text =
                    Ui("Data pack format") + ": " + major + (minor == 0 ? "" : "." + minor);
            }
        };
        var description = Input(dataPackBasicDraft.Description, null, true);
        description.MinHeight = 72;
        description.MaxLength = 4000;
        void SaveBasic() =>
            dataPackBasicDraft = new(
                name.Text ?? "",
                version.SelectedItem as string ?? "",
                description.Text ?? "",
                ns.Text ?? ""
            );
        name.TextChanged += (_, _) => SaveBasic();
        ns.TextChanged += (_, _) => SaveBasic();
        description.TextChanged += (_, _) => SaveBasic();
        version.SelectionChanged += (_, _) => SaveBasic();
        version.SelectedItem = dataPackBasicDraft.GameVersion;
        var basics = Stack(
            Field("Data pack name", name),
            Field("Namespace", ns),
            T(
                "Use lowercase letters, numbers, underscores, hyphens or dots. Choose your own namespace to avoid replacing vanilla content.",
                12
            ),
            Field("Minecraft version", version),
            format,
            Field("Description", description)
        );
        var entryEditor = BuildDataPackEntryEditor(
            () => version.SelectedItem as string ?? "",
            () => ns.Text ?? ""
        );
        var templates = entryEditor.View;
        var paths = new ComboBox
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        paths.Tag = "datapack-files";
        var content = Input("", null, true);
        content.IsReadOnly = true;
        content.Height = 180;
        content.MinHeight = 120;
        content.MaxHeight = 240;
        var zip = new CheckBox { Content = Ui("Export as ZIP"), IsChecked = true };
        var preview = Stack(
            T(
                "Review the generated files before exporting. Existing files will never be overwritten.",
                12
            ),
            Field("Generated files", paths),
            content
        );
        var status = T("");
        status.IsVisible = false;
        string? outputDirectory = null;
        var openOutput = Btn(
            "Open folder",
            () =>
            {
                if (outputDirectory != null)
                    Open(outputDirectory);
                return Task.CompletedTask;
            }
        );
        openOutput.IsVisible = false;
        var step = 0;
        IReadOnlyDictionary<string, string>? files = null;
        DataPackOptions? reviewed = null;
        DataPackOptions Read(bool includeTemplates) =>
            new(
                name.Text ?? "",
                version.SelectedItem as string ?? "",
                description.Text ?? "",
                ns.Text ?? ""
            )
            {
                Entries = includeTemplates ? entryEditor.Read() : Array.Empty<DataPackEntry>(),
            };
        void Error(Exception ex)
        {
            status.Text = Ui(
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
                status.Text +=
                    " " + string.Format(Ui("Line {0}, column {1}"), json.Line, json.Column);
            status.IsVisible = true;
        }
        StackPanel? body = null;
        WrapPanel navigation = null!;
        Button previous = null!,
            next = null!,
            export = null!;
        previous = Btn(
            "Previous",
            () =>
            {
                if (step <= 0)
                    return Task.CompletedTask;
                step--;
                Update();
                return Task.CompletedTask;
            }
        );
        next = Btn(
            "Next",
            () =>
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
                return Task.CompletedTask;
            },
            true
        );
        export = Btn(
            "Export data pack",
            async () =>
            {
                if (reviewed == null)
                    return;
                body!.IsEnabled = false;
                try
                {
                    var asZip = zip.IsChecked == true;
                    var destination = asZip
                        ? await SaveFile(reviewed.Name, ".zip")
                        : await Folder();
                    if (destination == null)
                        return;
                    var options = reviewed;
                    var target = await Task.Run(() =>
                        asZip
                            ? DataPackGenerator.ExportZip(destination, options)
                            : DataPackGenerator.CreateDataPack(destination, options)
                    );
                    outputDirectory = asZip ? Path.GetDirectoryName(target)! : target;
                    openOutput.IsVisible = true;
                    status.Text = Ui("Data pack exported") + ": " + target;
                    status.IsVisible = true;
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
                finally
                {
                    body.IsEnabled = true;
                }
            },
            true
        );
        paths.SelectionChanged += (_, _) =>
            content.Text = paths.SelectedItem is string path && files != null ? files[path] : "";
        navigation = Row(previous, next, zip, export);
        body = Stack(
            heading,
            basics,
            templates,
            preview,
            status,
            openOutput,
            navigation,
            T(
                "Install: put the exported ZIP or pack folder into your world's datapacks folder. Enter the world and run /reload, then /datapack list. Commands require permission. Back up your world before testing.",
                12
            ),
            T(
                "Test your own entry IDs with /function namespace:path, /advancement grant @s only namespace:path or /loot give @s loot namespace:path where supported. Full source is not automatically converted when changing game versions.",
                12
            )
        );
        buttonAvailability.Add(previous, () => step > 0);
        previous.Tag = "datapack-previous";
        next.Tag = "datapack-next";
        export.Tag = "datapack-export";
        void Update()
        {
            heading.Text = Ui(
                new[] { "1. Basic information", "2. Content editor", "3. Preview and export" }[step]
            );
            basics.IsVisible = step == 0;
            templates.IsVisible = step == 1;
            preview.IsVisible = step == 2;
            previous.IsEnabled = step > 0;
            previous.IsVisible = step > 0;
            next.IsVisible = step < 2;
            export.IsVisible = step == 2;
            zip.IsVisible = step == 2;
            body!.Children.Remove(navigation);
            body.Children.Insert(step == 2 ? 1 : body.Children.Count - 2, navigation);
            body.Children.Remove(status);
            body.Children.Insert(step == 2 ? 2 : 1, status);
            status.IsVisible = false;
            openOutput.IsVisible = false;
        }
        Update();
        return Stack(Btn("Back to toolbox", () => Navigate("toolbox")), Card(body));
    }
}
