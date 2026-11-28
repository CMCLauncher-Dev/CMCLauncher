using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void SeedMapPage()
    {
        Header("Seed map", "Explore biomes and predicted structures in Java Edition.");
        SeedMap? map = null;
        var seed = new TextBox
        {
            Header = Ui("World seed"),
            PlaceholderText = Ui("Number or text"),
            Tag = "seed-map-seed",
        };
        var version = new ComboBox
        {
            Header = Ui("Java Edition version"),
            ItemsSource = SeedMap.Versions,
            SelectedIndex = SeedMap.Versions.Count - 1,
        };
        var dimension = new ComboBox
        {
            Header = Ui("Dimension"),
            ItemsSource = new[] { Ui("Overworld"), Ui("Nether"), Ui("The End") },
            SelectedIndex = 0,
            Tag = "seed-map-dimension",
        };
        var structure = new ComboBox
        {
            Header = Ui("Structures"),
            ItemsSource = SeedMap.StructureNames.Select(Ui).ToArray(),
            SelectedIndex = 2,
        };
        var x = new TextBox
        {
            Header = "X",
            Text = "0",
            Width = 98,
        };
        var z = new TextBox
        {
            Header = "Z",
            Text = "0",
            Width = 98,
        };
        var status = Text("Biome colors are sampled at Y 64.", 13);
        status.TextWrapping = TextWrapping.Wrap;
        var pointer = Text("", 13);
        var result = new ComboBox
        {
            Header = Ui("Structures in view"),
            Visibility = Visibility.Collapsed,
            Tag = "seed-map-structures",
        };
        var image = new Image { Stretch = Stretch.Fill, Tag = "seed-map-image" };
        var translation = new TranslateTransform();
        image.RenderTransform = translation;
        var viewport = new Grid
        {
            Height = 220,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) },
        };
        viewport.Children.Add(image);
        var structureTip = new InfoBar
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            IsClosable = true,
        };
        var teleportCommand = "";
        structureTip.ActionButton = Action(
            "Copy TP command",
            (_, _) =>
            {
                if (
                    !DesktopClipboard.TrySetText(
                        WinRT.Interop.WindowNative.GetWindowHandle(this),
                        teleportCommand,
                        out var failure
                    )
                )
                    throw new IOException(failure);
                ShowToast(Ui("Copied"));
            },
            independent: true
        );
        var mapArea = new Grid();
        mapArea.Children.Add(viewport);
        mapArea.Children.Add(structureTip);
        var content = new StackPanel { Spacing = 10 };
        var navigation = new Grid { ColumnSpacing = 5, Visibility = Visibility.Collapsed };
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

        var search = new TextBox
        {
            Header = Ui("Search structures"),
            PlaceholderText = Ui("Name or coordinates"),
            IsEnabled = false,
        };
        search.Tag = "seed-map-search";
        structure.Tag = "seed-map-filter";
        search.IsEnabled = false;
        var filtered = new List<SeedMapStructure>();
        string StructureLabel(SeedMapStructure item) =>
            $"{Ui(SeedMap.StructureNames[item.Type])} · X {item.X} · Z {item.Z}";

        void RefreshStructures()
        {
            var selected = result.SelectedItem as string;
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
            result.ItemsSource = labels;
            result.SelectedIndex = Array.IndexOf(labels, selected);
            result.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => RefreshStructures();

        async Task ShowMap(SeedMapPoint center, int scale)
        {
            if (busy)
                return;
            if (string.IsNullOrWhiteSpace(seed.Text))
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

            var worldSeed = SeedMap.ParseSeed(seed.Text);
            busy = true;
            structureTip.IsOpen = false;
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
                if (page != "seed-map")
                    return;
                var bitmap = new WriteableBitmap(next.RasterWidth, next.RasterHeight);
                using (var stream = bitmap.PixelBuffer.AsStream())
                    stream.Write(next.Pixels);
                bitmap.Invalidate();
                translation.X = translation.Y = 0;
                image.Source = bitmap;
                map = next;
                x.Text = next.Center.X.ToString(CultureInfo.InvariantCulture);
                z.Text = next.Center.Z.ToString(CultureInfo.InvariantCulture);
                pointer.Text = "";
                viewport.Visibility = navigation.Visibility = Visibility.Visible;
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
                RuntimeLog.Write("ERROR", ex.ToString());
                status.Text = Ui("Could not generate the seed map.");
            }
            finally
            {
                content.IsHitTestVisible = true;
                busy = false;
                version.IsEnabled = dimension.IsEnabled = structure.IsEnabled = true;
            }
        }

        SeedMapPoint? ParseCenter()
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
                return new(centerX, centerZ);
            status.Text = Ui("Enter whole numbers for X and Z.");
            return null;
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
        var generate = Action(
            "Show map",
            async (_, _) =>
            {
                if (ParseCenter() is { } center)
                    await ShowMap(center, map?.Scale ?? SeedMap.DefaultScale);
            },
            primary: true,
            independent: true
        );
        generate.Tag = "seed-map-show";
        generate.VerticalAlignment = VerticalAlignment.Bottom;
        void AddMapButton(
            string label,
            string symbol,
            Func<SeedMap, (SeedMapPoint Center, int Scale)> move
        )
        {
            var button = Action(
                label,
                async (_, _) =>
                {
                    if (map is { } current)
                    {
                        var (center, scale) = move(current);
                        if (center != current.Center || scale != current.Scale)
                            await ShowMap(center, scale);
                    }
                },
                independent: true
            );
            button.Content = symbol;
            button.Tag = "seed-map-" + label;
            button.MinWidth = 0;
            button.Padding = new Thickness(0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTipService.SetToolTip(button, Ui(label));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Ui(label));
            Grid.SetColumn(button, navigation.Children.Count);
            navigation.ColumnDefinitions.Add(new ColumnDefinition());
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
        uint? activePointer = null;
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
            if (!viewport.CapturePointer(e.Pointer))
                return;
            activePointer = e.Pointer.PointerId;
            dragStart = e.GetCurrentPoint(viewport).Position;
            dragged = false;
            e.Handled = true;
        };
        viewport.PointerMoved += (_, e) =>
        {
            if (map is not { } current || busy)
                return;
            var point = e.GetCurrentPoint(viewport).Position;
            if (activePointer == e.Pointer.PointerId)
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
            var horizontal = point.X / viewport.ActualWidth;
            var vertical = point.Y / viewport.ActualHeight;
            var cell = current.PositionAt(horizontal, vertical);
            pointer.Text = current.StructureAt(horizontal, vertical) is { } hovered
                ? StructureLabel(hovered)
                : $"X {cell.X} · Z {cell.Z} · {current.BiomeAt(horizontal, vertical)}";
        };
        viewport.PointerReleased += async (_, e) =>
        {
            if (activePointer != e.Pointer.PointerId || map is not { } current)
                return;
            activePointer = null;
            var point = e.GetCurrentPoint(viewport).Position;
            viewport.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            var dx = point.X - dragStart.X;
            var dy = point.Y - dragStart.Y;
            if (dragged || dx * dx + dy * dy >= 16)
            {
                await ShowMap(
                    current.PanBy(dx / viewport.ActualWidth, dy / viewport.ActualHeight),
                    current.Scale
                );
                translation.X = translation.Y = 0;
            }
            else if (
                current.StructureAt(
                    point.X / viewport.ActualWidth,
                    point.Y / viewport.ActualHeight
                ) is
                { } marker
            )
            {
                if (!filtered.Contains(marker))
                {
                    search.Text = "";
                    RefreshStructures();
                }
                result.SelectedIndex = filtered.IndexOf(marker);
                structureTip.Title = Ui(SeedMap.StructureNames[marker.Type]);
                structureTip.Message = $"X {marker.X} · Z {marker.Z}";
                teleportCommand = SeedMap.TeleportCommand(marker, current.Dimension);
                structureTip.IsOpen = true;
            }
            else
                structureTip.IsOpen = false;
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
            var delta = e.GetCurrentPoint(viewport).Properties.MouseWheelDelta;
            if (delta == 0)
                return;
            e.Handled = true;
            var scale =
                delta > 0
                    ? Math.Max(SeedMap.MinScale, current.Scale / 2)
                    : Math.Min(SeedMap.MaxScale, current.Scale * 2);
            if (scale != current.Scale)
            {
                var point = e.GetCurrentPoint(viewport).Position;
                await ShowMap(
                    current.ZoomAt(
                        point.X / viewport.ActualWidth,
                        point.Y / viewport.ActualHeight,
                        scale
                    ),
                    scale
                );
            }
        };
        (int Width, int Height) SampleDimensions()
        {
            var ratio =
                viewport.ActualWidth > 0 && viewport.ActualHeight > 0
                    ? viewport.ActualWidth / viewport.ActualHeight
                    : 2;
            var samplesWide = Math.Clamp((int)Math.Round(128 * ratio), 16, 640);
            return (samplesWide, Math.Clamp((int)Math.Round(samplesWide / ratio), 16, 512));
        }

        void ResizeViewport() => viewport.Height = Math.Max(120, PageViewport.ActualHeight - 245);
        SizeChangedEventHandler availableSizeChanged = (_, _) => ResizeViewport();
        content.Loaded += (_, _) =>
        {
            PageViewport.SizeChanged += availableSizeChanged;
            ResizeViewport();
        };
        content.Unloaded += (_, _) => PageViewport.SizeChanged -= availableSizeChanged;
        viewport.SizeChanged += (_, e) =>
        {
            ((RectangleGeometry)viewport.Clip).Rect = new Rect(
                0,
                0,
                e.NewSize.Width,
                e.NewSize.Height
            );
        };
        var resizeRevision = 0;
        viewport.SizeChanged += async (_, _) =>
        {
            var revision = ++resizeRevision;
            await Task.Delay(150);
            while (
                revision == resizeRevision && busy && page == "seed-map" && content.XamlRoot != null
            )
                await Task.Delay(50);
            if (
                revision != resizeRevision
                || !(page == "seed-map" && content.XamlRoot != null)
                || map is not { } current
            )
                return;
            if (SampleDimensions() != (current.Width, current.Height))
                await ShowMap(current.Center, current.Scale);
        };
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        for (var column = 1; column < 6; column++)
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var topFields = new FrameworkElement[] { seed, version, dimension, x, z, generate };
        for (var column = 0; column < topFields.Length; column++)
        {
            Grid.SetColumn(topFields[column], column);
            topFields[column].VerticalAlignment = VerticalAlignment.Bottom;
            top.Children.Add(topFields[column]);
        }
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 555;
            Grid.SetColumnSpan(topFields[0], narrow ? 2 : 1);
            Grid.SetColumn(topFields[1], narrow ? 2 : 1);
            Grid.SetColumn(topFields[2], narrow ? 3 : 2);
            for (var index = 3; index < topFields.Length; index++)
            {
                Grid.SetRow(topFields[index], narrow ? 1 : 0);
                Grid.SetColumn(topFields[index], narrow ? index - 2 : index);
            }
        };
        var canvas = new StackPanel { Spacing = 6 };
        canvas.Children.Add(navigation);
        canvas.Children.Add(mapArea);
        canvas.Children.Add(pointer);
        var bottom = new Grid { ColumnSpacing = 12 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(result, 1);
        result.MaxWidth = 240;
        Grid.SetColumn(search, 2);
        bottom.Children.Add(structure);
        bottom.Children.Add(result);
        bottom.Children.Add(search);
        foreach (var control in new FrameworkElement[] { top, status, canvas, bottom })
            content.Children.Add(control);
        PageBody.Children.Add(Card(content));
    }
}
