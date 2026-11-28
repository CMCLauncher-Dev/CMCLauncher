using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control SeedMapPage()
    {
        SeedMap? map = null;
        WriteableBitmap? bitmap = null;
        var seed = Input("", "Number or text");
        seed.Tag = "seed-map-seed";
        var version = new ComboBox
        {
            ItemsSource = SeedMap.Versions,
            SelectedIndex = SeedMap.Versions.Count - 1,
        };
        var dimension = new ComboBox
        {
            ItemsSource = new[] { Ui("Overworld"), Ui("Nether"), Ui("The End") },
            SelectedIndex = 0,
            Tag = "seed-map-dimension",
        };
        var structure = new ComboBox
        {
            ItemsSource = SeedMap.StructureNames.Select(Ui).ToArray(),
            SelectedIndex = 2,
        };
        var x = Input("0");
        var z = Input("0");
        x.Width = z.Width = 92;
        var status = T("Biome colors are sampled at Y 64.");
        var pointer = T("");
        var coordinates = new ComboBox { Tag = "seed-map-structures" };
        var result = Stack(T("Structures in view"), coordinates);
        result.IsVisible = false;
        var image = new Image { Stretch = Stretch.Fill, Tag = "seed-map-image" };
        var translation = new TranslateTransform();
        image.RenderTransform = translation;
        var viewport = new Border
        {
            Height = 220,
            Background = Brushes.Transparent,
            ClipToBounds = true,
            Child = image,
        };
        var tipTitle = T("", 14, true);
        var tipCoordinates = T("", 13);
        var teleportCommand = "";
        var copyTeleport = Btn(
            "Copy TP command",
            async () =>
            {
                if (Clipboard == null)
                    return;
                await Clipboard.SetTextAsync(teleportCommand);
                ShowToast(Ui("Copied"));
            }
        );
        var closeTip = Btn("Close", () => Task.CompletedTask);
        var tipDetails = Stack(tipTitle, tipCoordinates);
        tipDetails.Spacing = 2;
        var structureTip = Card(Stack(tipDetails, Row(copyTeleport, closeTip)));
        structureTip.HorizontalAlignment = HorizontalAlignment.Center;
        structureTip.VerticalAlignment = VerticalAlignment.Top;
        structureTip.Margin = new Thickness(8);
        structureTip.IsVisible = false;
        closeTip.Click += (_, _) => structureTip.IsVisible = false;
        var mapArea = new Grid();
        mapArea.Children.Add(viewport);
        mapArea.Children.Add(structureTip);
        var navigation = new Grid { ColumnSpacing = 5, IsVisible = false };
        var content = new StackPanel { Spacing = 10 };
        var busy = false;
        var updatingFilters = false;
        int[] structureOptions = [];
        SeedMapDimension SelectedDimension() =>
            dimension.SelectedIndex switch
            {
                1 => SeedMapDimension.Nether,
                2 => SeedMapDimension.End,
                _ => SeedMapDimension.Overworld,
            };
        void UpdateStructureOptions(int preferred)
        {
            updatingFilters = true;
            structureOptions = SeedMap
                .StructureOptions(version.SelectedIndex, SelectedDimension())
                .ToArray();
            structure.ItemsSource = structureOptions
                .Select(index => Ui(SeedMap.StructureNames[index]))
                .ToArray();
            structure.SelectedIndex = Array.IndexOf(structureOptions, preferred);
            if (structure.SelectedIndex < 0)
                structure.SelectedIndex = Array.IndexOf(structureOptions, SeedMap.AllStructures);
            updatingFilters = false;
        }
        UpdateStructureOptions(2);
        var attached = true;
        content.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            image.Source = null;
            bitmap?.Dispose();
            bitmap = null;
        };

        var search = Input("", "Name or coordinates");
        search.IsEnabled = false;
        var filtered = new List<SeedMapStructure>();
        string StructureLabel(SeedMapStructure item) =>
            $"{Ui(SeedMap.StructureNames[item.Type])} · X {item.X} · Z {item.Z}";

        void RefreshStructures()
        {
            var selected = coordinates.SelectedItem as string;
            filtered.Clear();
            var terms = (search.Text ?? "").Split(
                [' ', ',', '，'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (map is { } current)
                filtered.AddRange(
                    current.Structures.Where(item =>
                        terms.All(term =>
                            StructureLabel(item)
                                .Contains(term, StringComparison.CurrentCultureIgnoreCase)
                            || SeedMap
                                .StructureNames[item.Type]
                                .Contains(term, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                );
            var labels = filtered.Select(StructureLabel).ToArray();
            coordinates.ItemsSource = labels;
            coordinates.SelectedIndex = Array.IndexOf(labels, selected);
            result.IsVisible = filtered.Count > 0;
        }
        search.TextChanged += (_, _) => RefreshStructures();

        async Task ShowMap(SeedMapPoint center, int scale)
        {
            if (busy)
                return;
            var seedText = seed.Text;
            if (string.IsNullOrWhiteSpace(seedText))
            {
                status.Text = Ui("Enter a world seed.");
                return;
            }
            var selectedVersion = version.SelectedIndex;
            var selectedDimension = SelectedDimension();
            var selectedStructure = structureOptions.ElementAtOrDefault(structure.SelectedIndex);
            if (!SeedMap.SupportsStructure(selectedVersion, selectedStructure, selectedDimension))
            {
                status.Text = Ui("That structure is unavailable in the selected version.");
                return;
            }
            var worldSeed = SeedMap.ParseSeed(seedText);
            busy = true;
            structureTip.IsVisible = false;
            version.IsEnabled = dimension.IsEnabled = structure.IsEnabled = false;
            content.IsHitTestVisible = false;
            status.Text = Ui("Generating seed map…");
            try
            {
                var (sampleWidth, sampleHeight) = SampleDimensions();
                var next = await Task.Run(() =>
                    new SeedMap(
                        worldSeed,
                        selectedVersion,
                        selectedStructure,
                        center,
                        scale,
                        sampleWidth,
                        sampleHeight,
                        selectedDimension,
                        highDetail: true
                    )
                );
                if (!attached)
                    return;
                var nextBitmap = new WriteableBitmap(
                    new PixelSize(next.RasterWidth, next.RasterHeight),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888,
                    AlphaFormat.Unpremul
                );
                using (var frame = nextBitmap.Lock())
                    for (var y = 0; y < next.RasterHeight; y++)
                        Marshal.Copy(
                            next.Pixels,
                            y * next.RasterWidth * 4,
                            IntPtr.Add(frame.Address, y * frame.RowBytes),
                            next.RasterWidth * 4
                        );
                translation.X = translation.Y = 0;
                image.Source = nextBitmap;
                bitmap?.Dispose();
                bitmap = nextBitmap;
                map = next;
                x.Text = next.Center.X.ToString(CultureInfo.InvariantCulture);
                z.Text = next.Center.Z.ToString(CultureInfo.InvariantCulture);
                pointer.Text = "";
                viewport.IsVisible = navigation.IsVisible = true;
                search.IsEnabled = selectedStructure != 0;
                RefreshStructures();
                status.Text =
                    $"{Ui("Blocks per sample")} {next.BlocksPerSample} · {Ui("Biome colors are sampled at Y 64.")}"
                    + (
                        selectedStructure == 0
                            ? ""
                            : $" · {Ui("Predicted structures")} {next.Structures.Count}"
                    );
            }
            catch (Exception ex)
                when (ex
                        is DllNotFoundException
                            or BadImageFormatException
                            or EntryPointNotFoundException
                            or InvalidOperationException
                )
            {
                session.Log("ERROR", ex.ToString());
                status.Text = Ui("Could not generate the seed map.");
            }
            finally
            {
                content.IsHitTestVisible = true;
                busy = false;
                version.IsEnabled = dimension.IsEnabled = structure.IsEnabled = true;
            }
        }

        structure.SelectionChanged += async (_, _) =>
        {
            if (!updatingFilters && map is { } current && structure.SelectedIndex >= 0)
                await ShowMap(current.Center, current.Scale);
        };
        dimension.SelectionChanged += async (_, _) =>
        {
            UpdateStructureOptions(SeedMap.AllStructures);
            if (map is { } current)
                await ShowMap(current.Center, current.Scale);
        };
        version.SelectionChanged += async (_, _) =>
        {
            UpdateStructureOptions(structureOptions.ElementAtOrDefault(structure.SelectedIndex));
            if (map is { } current)
                await ShowMap(current.Center, current.Scale);
        };
        var generate = Btn(
            "Show map",
            async () =>
            {
                if (
                    int.TryParse(
                        x.Text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var centerX
                    )
                    && int.TryParse(
                        z.Text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var centerZ
                    )
                )
                    await ShowMap(new(centerX, centerZ), map?.Scale ?? SeedMap.DefaultScale);
                else
                    status.Text = Ui("Enter whole numbers for X and Z.");
            },
            true
        );
        generate.Tag = "seed-map-show";
        generate.VerticalAlignment = VerticalAlignment.Bottom;
        void AddMapButton(
            string label,
            string symbol,
            Func<SeedMap, (SeedMapPoint Center, int Scale)> move
        )
        {
            var button = Btn(
                label,
                async () =>
                {
                    if (map is { } current)
                    {
                        var (center, scale) = move(current);
                        if (center != current.Center || scale != current.Scale)
                            await ShowMap(center, scale);
                    }
                }
            );
            button.Content = symbol;
            button.MinWidth = 0;
            button.Padding = new Thickness(0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(button, Ui(label));
            global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(label));
            Grid.SetColumn(button, navigation.Children.Count);
            navigation.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            navigation.Children.Add(button);
        }
        AddMapButton(
            "Pan west",
            "←",
            m => (m.Center with { X = m.Center.X - m.Scale * m.Width / 2 }, m.Scale)
        );
        AddMapButton(
            "Pan north",
            "↑",
            m => (m.Center with { Z = m.Center.Z - m.Scale * m.Height / 2 }, m.Scale)
        );
        AddMapButton(
            "Pan south",
            "↓",
            m => (m.Center with { Z = m.Center.Z + m.Scale * m.Height / 2 }, m.Scale)
        );
        AddMapButton(
            "Pan east",
            "→",
            m => (m.Center with { X = m.Center.X + m.Scale * m.Width / 2 }, m.Scale)
        );
        AddMapButton("Zoom out", "−", m => (m.Center, Math.Min(SeedMap.MaxScale, m.Scale * 2)));
        AddMapButton("Zoom in", "+", m => (m.Center, Math.Max(SeedMap.MinScale, m.Scale / 2)));
        global::Avalonia.Input.IPointer? activePointer = null;
        Point dragStart = default;
        var dragged = false;
        viewport.PointerPressed += (_, e) =>
        {
            if (
                map == null
                || busy
                || activePointer != null
                || !e.GetCurrentPoint(viewport).Properties.IsLeftButtonPressed
            )
                return;
            e.Pointer.Capture(viewport);
            activePointer = e.Pointer;
            dragStart = e.GetPosition(viewport);
            dragged = false;
            e.Handled = true;
        };
        viewport.PointerMoved += (_, e) =>
        {
            if (map is not { } current || busy)
                return;
            var point = e.GetPosition(viewport);
            if (activePointer == e.Pointer)
            {
                var dx = point.X - dragStart.X;
                var dy = point.Y - dragStart.Y;
                dragged |= dx * dx + dy * dy >= 16;
                if (dragged)
                {
                    translation.X = dx;
                    translation.Y = dy;
                    return;
                }
            }
            var horizontal = point.X / viewport.Bounds.Width;
            var vertical = point.Y / viewport.Bounds.Height;
            var cell = current.PositionAt(horizontal, vertical);
            pointer.Text = current.StructureAt(horizontal, vertical) is { } hovered
                ? StructureLabel(hovered)
                : $"X {cell.X} · Z {cell.Z} · {current.BiomeAt(horizontal, vertical)}";
        };
        viewport.PointerReleased += async (_, e) =>
        {
            if (activePointer != e.Pointer || map is not { } current)
                return;
            activePointer = null;
            var point = e.GetPosition(viewport);
            e.Pointer.Capture(null);
            e.Handled = true;
            var dx = point.X - dragStart.X;
            var dy = point.Y - dragStart.Y;
            if (dragged || dx * dx + dy * dy >= 16)
            {
                await ShowMap(
                    current.PanBy(dx / viewport.Bounds.Width, dy / viewport.Bounds.Height),
                    current.Scale
                );
                translation.X = translation.Y = 0;
            }
            else if (
                current.StructureAt(
                    point.X / viewport.Bounds.Width,
                    point.Y / viewport.Bounds.Height
                ) is
                { } marker
            )
            {
                if (!filtered.Contains(marker))
                {
                    search.Text = "";
                    RefreshStructures();
                }
                coordinates.SelectedIndex = filtered.IndexOf(marker);
                tipTitle.Text = Ui(SeedMap.StructureNames[marker.Type]);
                tipCoordinates.Text = $"X {marker.X} · Z {marker.Z}";
                teleportCommand = SeedMap.TeleportCommand(marker, current.Dimension);
                structureTip.IsVisible = true;
            }
            else
                structureTip.IsVisible = false;
        };
        viewport.PointerCaptureLost += (_, _) =>
        {
            if (activePointer == null)
                return;
            activePointer = null;
            translation.X = translation.Y = 0;
        };
        viewport.PointerWheelChanged += async (_, e) =>
        {
            if (busy || activePointer != null || map is not { } current)
                return;
            var delta = e.Delta.Y;
            if (delta == 0)
                return;
            e.Handled = true;
            var scale =
                delta > 0
                    ? Math.Max(SeedMap.MinScale, current.Scale / 2)
                    : Math.Min(SeedMap.MaxScale, current.Scale * 2);
            if (scale != current.Scale)
            {
                var point = e.GetPosition(viewport);
                await ShowMap(
                    current.ZoomAt(
                        point.X / viewport.Bounds.Width,
                        point.Y / viewport.Bounds.Height,
                        scale
                    ),
                    scale
                );
            }
        };
        (int Width, int Height) SampleDimensions()
        {
            var ratio =
                viewport.Bounds.Width > 0 && viewport.Bounds.Height > 0
                    ? viewport.Bounds.Width / viewport.Bounds.Height
                    : 2;
            var samplesWide = Math.Clamp((int)Math.Round(128 * ratio), 16, 640);
            return (samplesWide, Math.Clamp((int)Math.Round(samplesWide / ratio), 16, 512));
        }

        void ResizeViewport() => viewport.Height = Math.Max(120, Bounds.Height - 330);
        EventHandler<SizeChangedEventArgs> availableSizeChanged = (_, _) => ResizeViewport();
        content.AttachedToVisualTree += (_, _) =>
        {
            SizeChanged += availableSizeChanged;
            ResizeViewport();
        };
        content.DetachedFromVisualTree += (_, _) => SizeChanged -= availableSizeChanged;
        var resizeRevision = 0;
        viewport.SizeChanged += async (_, _) =>
        {
            var revision = ++resizeRevision;
            await Task.Delay(150);
            while (revision == resizeRevision && busy && attached && route == "seed-map")
                await Task.Delay(50);
            if (
                revision != resizeRevision
                || !(attached && route == "seed-map")
                || map is not { } current
            )
                return;
            if (SampleDimensions() != (current.Width, current.Height))
                await ShowMap(current.Center, current.Scale);
        };
        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
        };
        var fields = new Control[]
        {
            Field("World seed", seed),
            Field("Java Edition version", version),
            Field("Dimension", dimension),
            Field("X", x),
            Field("Z", z),
            generate,
        };
        for (var column = 0; column < fields.Length; column++)
        {
            Grid.SetColumn(fields[column], column);
            fields[column].VerticalAlignment = VerticalAlignment.Bottom;
            top.Children.Add(fields[column]);
        }
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 555;
            Grid.SetColumnSpan(fields[0], narrow ? 2 : 1);
            Grid.SetColumn(fields[1], narrow ? 2 : 1);
            Grid.SetColumn(fields[2], narrow ? 3 : 2);
            for (var index = 3; index < fields.Length; index++)
            {
                Grid.SetRow(fields[index], narrow ? 1 : 0);
                Grid.SetColumn(fields[index], narrow ? index - 2 : index);
            }
        };
        var canvas = Stack(navigation, mapArea, pointer);
        canvas.Spacing = 6;
        var bottom = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 12,
        };
        var searchField = Field("Search structures", search);
        Grid.SetColumn(result, 1);
        result.MaxWidth = 240;
        Grid.SetColumn(searchField, 2);
        bottom.Children.Add(Field("Structures", structure));
        bottom.Children.Add(result);
        bottom.Children.Add(searchField);
        foreach (var control in new Control[] { top, status, canvas, bottom })
            content.Children.Add(control);
        return Card(content);
    }
}
