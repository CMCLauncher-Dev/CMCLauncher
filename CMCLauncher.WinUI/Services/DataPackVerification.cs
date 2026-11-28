using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal async Task VerifyDataPackUi(string directory)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
            File.AppendAllText(
                Path.Combine(directory, "passed.txt"),
                message + Environment.NewLine
            );
        }
        await VerifyWindowClientFrame(Check, directory);
        await VerifyOptionsRecoveryUi(Check, directory);
        page = "toolbox";
        expandedNavigation = "more";
        await RenderAsync();
        await VerifyToolboxFilters(Check, LauncherPlatform.Current.SupportsMemoryOptimization);
        await VerifySeedMapUi(Check, directory);
        foreach (var theme in new[] { "light", "dark" })
        {
            store.Config.Settings.Theme = theme;
            await SwitchLanguage("zh_cn");
            ApplyTheme();
            dataPackBasicDraft = new("", "", Namespace: "my_pack");
            dataPackEntryDraft = CMCLauncher
                .Core.Minecraft.DataPacks.DataPackEntryEditor.StarterEntries()
                .ToList();
            page = "datapack-generator";
            expandedNavigation = "more";
            await RenderAsync();
            T Find<T>(string tag)
                where T : FrameworkElement =>
                Descendants(PageBody).OfType<T>().SingleOrDefault(c => Equals(c.Tag, tag)) ?? throw new InvalidOperationException("Missing control: " + tag);
            void Click(string tag) =>
                (
                    (IInvokeProvider)
                        new ButtonAutomationPeer(Find<Button>(tag)).GetPattern(
                            PatternInterface.Invoke
                        )
                ).Invoke();
            Check(
                !Find<Button>("datapack-previous").IsEnabled
                    && Find<Button>("datapack-export").Visibility == Visibility.Collapsed,
                theme + ": wizard starts on basic information"
            );
            Click("datapack-next");
            Check(
                Descendants(PageBody)
                    .OfType<InfoBar>()
                    .Any(i => i.IsOpen && i.Severity == InfoBarSeverity.Error),
                theme + ": incomplete input produces an inline error"
            );
            Find<TextBox>("datapack-name").Text = "UI fixture";
            Find<ComboBox>("datapack-version").SelectedItem = "1.21.2";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-previous").IsEnabled
                    && Find<Button>("datapack-next").Visibility == Visibility.Visible,
                theme + ": template step is reachable"
            );
            await Task.Delay(100);
            await Capture(Path.Combine(directory, "datapack-" + theme + "-editor.png"));

            Find<TextBox>("datapack-entry-id").Text = "tools/custom";
            Find<TextBox>("datapack-field-result").Text = "minecraft:diamond";
            Find<TextBox>("datapack-field-count").Text = "2";
            Click("datapack-entry-duplicate");
            Check(
                Find<ComboBox>("datapack-entries").Items.Count == 6,
                theme + ": entry can be duplicated"
            );
            Click("datapack-entry-remove");
            Click("datapack-entry-add");
            Click("datapack-entry-remove");
            Find<ComboBox>("datapack-entries").SelectedIndex = 0;
            Find<CheckBox>("datapack-entry-raw").IsChecked = true;
            Check(
                Find<TextBox>("datapack-entry-source").Visibility == Visibility.Visible,
                theme + ": complete source editor expands"
            );
            Find<TextBox>("datapack-entry-source").Text = "{broken";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Collapsed,
                theme + ": invalid JSON cannot advance"
            );
            Find<TextBox>("datapack-entry-source").Text =
                "{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[\"minecraft:dirt\"],\"result\":{\"id\":\"minecraft:diamond\",\"count\":2}}";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Visible
                    && Find<ComboBox>("datapack-files").Items.Count == 8,
                theme + ": review contains all eight actual generated files"
            );
            await Capture(Path.Combine(directory, "datapack-" + theme + "-preview.png"));
            Click("datapack-previous");
            Click("datapack-previous");
            Check(
                Find<TextBox>("datapack-name").Text == "UI fixture"
                    && Find<Button>("datapack-previous").Visibility == Visibility.Collapsed,
                theme + ": going back preserves inputs"
            );
            Find<TextBox>("datapack-namespace").Text = "../bad";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Collapsed
                    && Descendants(PageBody).OfType<InfoBar>().Any(i => i.IsOpen),
                theme + ": invalid namespace cannot advance"
            );
            await Capture(Path.Combine(directory, "datapack-" + theme + "-validation.png"));
        }
    }

    async Task VerifySeedMapUi(Action<bool, string> check, string directory)
    {
        page = "seed-map";
        expandedNavigation = "more";
        await RenderAsync();
        T Find<T>(string tag) where T : FrameworkElement =>
            Descendants(PageBody).OfType<T>().Single(control => Equals(control.Tag, tag));
        void Click(string tag) =>
            ((IInvokeProvider)new ButtonAutomationPeer(Find<Button>(tag)).GetPattern(PatternInterface.Invoke)).Invoke();

        var reference = new SeedMap(42, SeedMap.Versions.Count - 1, 2, new(0, 0), SeedMap.DefaultScale);
        check(SeedMap.ParseSeed("abc") == 96354 && SeedMap.ParseSeed(long.MinValue.ToString()) == long.MinValue,
            "seed map parses Java text seeds and signed 64-bit numeric seeds");
        check(reference.Structures.Count > 0 && reference.BiomeAt(.5, .5).Length > 0,
            "seed map engine finds biomes and villages around the origin");
        var marker = reference.Structures[0];
        var mapX = (marker.X - reference.Origin.X) / (double)(SeedMap.Size * reference.Scale);
        var mapZ = (marker.Z - reference.Origin.Z) / (double)(SeedMap.Size * reference.Scale);
        check(reference.StructureAt(mapX, mapZ) == marker, "map marker exposes the predicted structure coordinates");
        var teleportTarget = new SeedMapStructure(-24, 96, 2);
        check(SeedMap.TeleportCommand(teleportTarget, SeedMapDimension.Overworld) == "/execute in minecraft:overworld run tp @s -24 ~ 96"
            && SeedMap.TeleportCommand(teleportTarget, SeedMapDimension.Nether) == "/execute in minecraft:the_nether run tp @s -24 ~ 96"
            && SeedMap.TeleportCommand(teleportTarget, SeedMapDimension.End) == "/execute in minecraft:the_end run tp @s -24 ~ 96",
            "structure TP commands use the map dimension and preserve the player's Y coordinate");

        Find<TextBox>("seed-map-seed").Text = "42";
        Click("seed-map-show");
        var image = Find<Image>("seed-map-image");
        for (var attempt = 0; attempt < 100 && image.Source == null; attempt++)
            await Task.Delay(50);
        var locations = Find<ComboBox>("seed-map-structures");
        var raster = image.Source as Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap;
        check(raster != null && raster.PixelWidth > raster.PixelHeight,
            "seed map renders a wide viewport");
        check(raster!.PixelHeight >= 256, "seed map renders at doubled sample resolution");
        reference = new SeedMap(42, SeedMap.Versions.Count - 1, 2, new(0, 0), SeedMap.DefaultScale,
            raster!.PixelWidth / SeedMap.DisplayDetail, raster.PixelHeight / SeedMap.DisplayDetail);
        marker = reference.Structures[0];
        check(locations.Items.Count == reference.Structures.Count,
            "seed map displays the raster and lists predicted village positions");
        locations.SelectedIndex = 0;
        check(locations.SelectedItem?.ToString()?.Contains($"X {marker.X} · Z {marker.Z}") == true,
            "the structure list shows precise X and Z coordinates");
        var firstImage = image.Source;
        Click("seed-map-Zoom in");
        for (var attempt = 0; attempt < 100 && ReferenceEquals(firstImage, image.Source); attempt++)
            await Task.Delay(50);
        check(!ReferenceEquals(firstImage, image.Source), "zoom redraws the seed map");
        var zoomedImage = image.Source;
        Find<ComboBox>("seed-map-filter").SelectedIndex = SeedMap.AllStructures;
        for (var attempt = 0; attempt < 150 && ReferenceEquals(zoomedImage, image.Source); attempt++)
            await Task.Delay(50);
        var allCount = locations.Items.Count;
        check(!ReferenceEquals(zoomedImage, image.Source) && allCount > 1,
            "all structures draws multiple predicted locations");
        Find<TextBox>("seed-map-search").Text = locations.Items[0].ToString()!.Split(" · ")[0];
        for (var attempt = 0; attempt < 40 && locations.Items.Count == allCount; attempt++)
            await Task.Delay(25);
        check(locations.Items.Count > 0 && locations.Items.Count < allCount,
            "structure search narrows the results by name");
        await Capture(Path.Combine(directory, "seed-map.png"));

        Find<TextBox>("seed-map-search").Text = "";
        var dimension = Find<ComboBox>("seed-map-dimension");
        var filter = Find<ComboBox>("seed-map-filter");
        var overworldImage = image.Source;
        dimension.SelectedIndex = 1;
        for (var attempt = 0; attempt < 150 && ReferenceEquals(overworldImage, image.Source); attempt++)
            await Task.Delay(50);
        check(!ReferenceEquals(overworldImage, image.Source)
            && filter.Items.Contains(Ui("Nether fortress"))
            && !filter.Items.Contains(Ui("Village")),
            "Nether dimension redraws the map and offers Nether structures");
        await Capture(Path.Combine(directory, "seed-map-nether.png"));

        var netherImage = image.Source;
        dimension.SelectedIndex = 2;
        for (var attempt = 0; attempt < 150 && ReferenceEquals(netherImage, image.Source); attempt++)
            await Task.Delay(50);
        check(!ReferenceEquals(netherImage, image.Source)
            && filter.Items.Contains(Ui("End city"))
            && !filter.Items.Contains(Ui("Nether fortress")),
            "End dimension redraws the map and offers End structures");
    }
}
