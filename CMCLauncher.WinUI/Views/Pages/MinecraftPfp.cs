using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Func<string, CancellationToken, Task<byte[]>> fetchPfpSkin = null!;

    async Task<byte[]> FindPfpSkin(string name, CancellationToken ct)
    {
        var profile = await core.Json(
            "https://api.minecraftservices.com/minecraft/profile/lookup/name/"
                + Uri.EscapeDataString(name),
            ct
        );
        if (!Guid.TryParse(profile.S("id"), out var id))
            throw new InvalidOperationException(Ui("Minecraft Java player not found."));
        return await FetchSkin(id.ToString("N")).WaitAsync(ct)
            ?? throw new InvalidOperationException(
                Ui("Could not load this player's skin. Try again or upload a PNG skin.")
            );
    }

    async Task MinecraftPfpPage(byte[]? initialSkin = null)
    {
        Header("MinecraftPFP", Ui("Create a profile picture from your Minecraft skin."));
        var generation = renderVersion;
        byte[]? skin = initialSkin;
        byte[]? png = null;
        int width = initialSkin == null ? 0 : 64,
            height = initialSkin == null ? 0 : 64,
            revision = 0;
        var preview = new Image
        {
            Tag = "pfp-preview",
            Width = 192,
            Height = 192,
            Stretch = Stretch.Uniform,
        };
        var status = Text(
            "Upload a PNG skin to start. Standard, legacy, and HD skins are supported.",
            12
        );
        var sizes = new BelowComboBox
        {
            Header = Ui("Export size"),
            ItemsSource = new[] { "128 × 128", "256 × 256", "512 × 512", "1024 × 1024" },
            SelectedIndex = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var hat = new CheckBox { Content = Ui("Include outer skin layer"), IsChecked = true };
        var headOnly = new CheckBox { Content = Ui("Head only"), IsChecked = false };
        var shadow = new CheckBox { Content = Ui("Soft shadow"), IsChecked = true };
        var slim = new CheckBox { Content = Ui("Slim arms (Alex model)"), IsChecked = false };
        var padding = new Slider
        {
            Header = Ui("Padding (%)"),
            Minimum = 0,
            Maximum = 40,
            StepFrequency = 1,
            Value = 12,
        };
        var backgrounds = new BelowComboBox
        {
            Tag = "pfp-background",
            Header = Ui("Background"),
            ItemsSource = new[] { Ui("Transparent"), Ui("Light"), Ui("Dark"), Ui("Custom color") },
            SelectedIndex = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var custom = new TextBox
        {
            Header = Ui("Background color (#RRGGBB)"),
            Text = "#00B4A6",
            Visibility = Visibility.Visible,
        };
        var gradient = new CheckBox { Content = Ui("Gradient background"), Tag = "pfp-gradient" };
        var gradientColor = new TextBox
        {
            Header = Ui("Gradient end color (#RRGGBB)"),
            Text = "#005C64",
        };
        var gradientAngle = new Slider
        {
            Header = Ui("Gradient angle (°)"),
            Minimum = 0,
            Maximum = 360,
            Value = 90,
            StepFrequency = 1,
        };
        var gradientOptions = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        gradientOptions.Children.Add(gradientColor);
        gradientOptions.Children.Add(gradientAngle);
        var export = Action(
            "Export PNG",
            async (_, _) =>
            {
                if (png == null)
                    return;
                // Capture the rendered file before opening the picker, so later changes
                // cannot make the exported image differ from the preview the user chose.
                var bytes = png;
                var picker = new DesktopFileSavePicker { SuggestedFileName = "MinecraftPFP" };
                picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
                DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                if (await picker.PickSaveFileAsync() is not { } file)
                    return;
                await FileIO.WriteBytesAsync(file, bytes);
                ShowToast(Ui("Avatar exported"));
            },
            true
        );
        export.IsEnabled = false;
        export.Tag = "pfp-export";

        async Task Refresh()
        {
            var current = ++revision;
            png = null;
            export.IsEnabled = false;
            custom.Visibility =
                backgrounds.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            gradientOptions.Visibility =
                gradient.IsChecked == true && backgrounds.SelectedIndex != 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (skin == null)
                return;
            try
            {
                byte[]? background = backgrounds.SelectedIndex switch
                {
                    1 => [245, 245, 245],
                    2 => [32, 32, 32],
                    _ => null,
                };
                if (backgrounds.SelectedIndex == 3)
                {
                    var hex = custom.Text.Trim();
                    if (
                        hex.Length != 7
                        || hex[0] != '#'
                        || !int.TryParse(
                            hex[1..],
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out var rgb
                        )
                    )
                        throw new InvalidDataException(Ui("Enter a color in #RRGGBB format."));
                    background = [(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)];
                }
                var size = new[] { 128, 256, 512, 1024 }[Math.Max(0, sizes.SelectedIndex)];
                byte[]? endColor = null;
                if (gradient.IsChecked == true && background != null)
                {
                    var hex = gradientColor.Text.Trim();
                    if (
                        hex.Length != 7
                        || hex[0] != '#'
                        || !int.TryParse(
                            hex[1..],
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out var rgb
                        )
                    )
                        throw new InvalidDataException(Ui("Enter a color in #RRGGBB format."));
                    endColor = [(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)];
                }
                var angle = gradientAngle.Value;
                var source = skin;
                var sourceWidth = width;
                var sourceHeight = height;
                var overlay = hat.IsChecked == true;
                var portrait = headOnly.IsChecked != true;
                var addShadow = shadow.IsChecked == true;
                var slimArms = slim.IsChecked == true;
                var border = (int)padding.Value;
                await Task.Delay(60);
                if (generation != renderVersion || current != revision)
                    return;
                var pixels = await Task.Run(() =>
                    CMCLauncher.MinecraftPfp.Render(
                        source,
                        sourceWidth,
                        sourceHeight,
                        size,
                        overlay,
                        border,
                        background,
                        portrait,
                        addShadow,
                        slimArms,
                        endColor,
                        angle
                    )
                );
                if (generation != renderVersion || current != revision)
                    return;
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    (uint)size,
                    (uint)size,
                    96,
                    96,
                    pixels
                );
                await encoder.FlushAsync();
                stream.Seek(0);
                var image = new BitmapImage();
                await image.SetSourceAsync(stream);
                stream.Seek(0);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                var bytes = new byte[(int)stream.Size];
                reader.ReadBytes(bytes);
                if (generation != renderVersion || current != revision)
                    return;
                preview.Source = image;
                png = bytes;
                export.IsEnabled = true;
                status.Text = UiFormat("{0} × {0} PNG ready to export", size);
            }
            catch (Exception ex)
            {
                if (generation == renderVersion && current == revision)
                    status.Text = Ui(ex.Message);
            }
        }

        async Task LoadSkin(Windows.Storage.Streams.IRandomAccessStream input)
        {
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, input);
            var w = (int)decoder.PixelWidth;
            var h = (int)decoder.PixelHeight;
            if (w < 64 || w > 1024 || w % 64 != 0 || h != w && h != w / 2)
                throw new InvalidDataException(
                    Ui(
                        "Use a 64×64 or 64×32 Minecraft skin, or an HD multiple up to 1024 pixels wide."
                    )
                );
            var pixels = (
                await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage
                )
            ).DetachPixelData();
            if (generation != renderVersion)
                return;
            skin = pixels;
            width = w;
            height = h;
            await Refresh();
        }

        var username = new TextBox
        {
            Header = Ui("Minecraft Java username"),
            PlaceholderText = "Steve",
            MaxLength = 16,
            Tag = "pfp-username",
        };
        var skinLoading = false;
        var fetch = Action(
            "Load player skin",
            async (sender, _) =>
            {
                var name = username.Text.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
                    throw new InvalidOperationException(
                        Ui(
                            "Enter a valid Minecraft Java username (3–16 letters, numbers, or underscores)."
                        )
                    );
                var button = (Button)sender;
                if (skinLoading)
                    return;
                skinLoading = true;
                button.IsEnabled = false;
                using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var ct = request.Token;
                RoutedEventHandler cancel = (_, _) => request.Cancel();
                username.Unloaded += cancel;
                try
                {
                    status.Text = Ui("Loading player skin…");
                    var bytes = await fetchPfpSkin(name, ct);
                    if (generation != renderVersion)
                        return;
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    await LoadSkin(stream);
                }
                catch (OperationCanceledException)
                {
                    if (generation == renderVersion)
                        status.Text = Ui("Skin loading canceled or timed out.");
                }
                catch (Exception ex)
                {
                    if (generation == renderVersion)
                        status.Text = Ui(
                            "Could not load this player's skin. Try again or upload a PNG skin."
                        );
                    await Error(ex);
                }
                finally
                {
                    username.Unloaded -= cancel;
                    skinLoading = false;
                    button.IsEnabled = true;
                }
            }
        );
        fetch.Tag = "pfp-fetch";
        var upload = Action(
            "Upload skin",
            async (_, _) =>
            {
                var picker = new DesktopFileOpenPicker();
                picker.FileTypeFilter.Add(".png");
                DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                if (
                    await picker.PickSingleFileAsync() is not { } file
                    || generation != renderVersion
                )
                    return;
                try
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    if (properties.Size > 4 * 1024 * 1024)
                        throw new InvalidDataException(Ui("Skin files must be smaller than 4 MB."));
                    using var input = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(
                        BitmapDecoder.PngDecoderId,
                        input
                    );
                    var w = (int)decoder.PixelWidth;
                    var h = (int)decoder.PixelHeight;
                    if (w < 64 || w > 1024 || w % 64 != 0 || h != w && h != w / 2)
                        throw new InvalidDataException(
                            Ui(
                                "Use a 64×64 or 64×32 Minecraft skin, or an HD multiple up to 1024 pixels wide."
                            )
                        );
                    var pixels = (
                        await decoder.GetPixelDataAsync(
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Straight,
                            new BitmapTransform(),
                            ExifOrientationMode.IgnoreExifOrientation,
                            ColorManagementMode.DoNotColorManage
                        )
                    ).DetachPixelData();
                    if (generation != renderVersion)
                        return;
                    skin = pixels;
                    width = w;
                    height = h;
                    await Refresh();
                }
                catch (Exception ex)
                {
                    if (generation == renderVersion)
                        await Error(ex);
                }
            }
        );
        var controls = new StackPanel { Spacing = 14 };
        controls.Children.Add(username);
        controls.Children.Add(fetch);
        controls.Children.Add(Buttons(upload, export));
        controls.Children.Add(status);
        controls.Children.Add(sizes);
        controls.Children.Add(hat);
        controls.Children.Add(headOnly);
        controls.Children.Add(shadow);
        controls.Children.Add(slim);
        controls.Children.Add(padding);
        controls.Children.Add(backgrounds);
        controls.Children.Add(custom);
        controls.Children.Add(gradient);
        controls.Children.Add(gradientOptions);
        var picture = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        picture.Children.Add(Text("Avatar preview", 16, true));
        picture.Children.Add(
            new Border
            {
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = ComboBorderBrush,
                Background = ComboSurfaceBrush,
                CornerRadius = new CornerRadius(8),
                Child = preview,
            }
        );
        var layout = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var previewCard = Card(picture);
        var controlsCard = Card(controls);
        previewCard.VerticalAlignment = VerticalAlignment.Top;
        controlsCard.VerticalAlignment = VerticalAlignment.Top;
        layout.Children.Add(previewCard);
        layout.Children.Add(controlsCard);
        layout.SizeChanged += (_, _) =>
        {
            var wide = layout.ActualWidth >= 640;
            Grid.SetColumnSpan(previewCard, wide ? 1 : 2);
            Grid.SetColumnSpan(controlsCard, wide ? 1 : 2);
            Grid.SetColumn(controlsCard, wide ? 1 : 0);
            Grid.SetRow(controlsCard, wide ? 0 : 1);
        };
        PageBody.Children.Add(layout);
        sizes.SelectionChanged += async (_, _) => await Refresh();
        backgrounds.SelectionChanged += async (_, _) => await Refresh();
        hat.Checked += async (_, _) => await Refresh();
        hat.Unchecked += async (_, _) => await Refresh();
        headOnly.Checked += async (_, _) => await Refresh();
        headOnly.Unchecked += async (_, _) => await Refresh();
        shadow.Checked += async (_, _) => await Refresh();
        shadow.Unchecked += async (_, _) => await Refresh();
        slim.Checked += async (_, _) => await Refresh();
        slim.Unchecked += async (_, _) => await Refresh();
        padding.ValueChanged += async (_, _) => await Refresh();
        custom.TextChanged += async (_, _) => await Refresh();
        gradient.Checked += async (_, _) =>
        {
            if (backgrounds.SelectedIndex == 0)
                backgrounds.SelectedIndex = 3;
            await Refresh();
        };
        gradient.Unchecked += async (_, _) => await Refresh();
        gradientColor.TextChanged += async (_, _) => await Refresh();
        gradientAngle.ValueChanged += async (_, _) => await Refresh();
        if (skin != null)
            await Refresh();
    }
}
