using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control PfpPage()
    {
        byte[]? pixels = null;
        var width = 0;
        var height = 0;
        var renderRevision = 0;
        WriteableBitmap? rendered = null;
        byte[]? exportBytes = null;
        Button export = null!;
        var username = Input("", "Minecraft Java username");
        var info = T("");
        var preview = new Image
        {
            Width = 192,
            Height = 192,
            Tag = "pfp-preview",
            Stretch = Stretch.Uniform,
        };
        var size = Choice(new[] { 128, 256, 512, 1024 }, 512);
        var padding = new Slider
        {
            Minimum = 0,
            Maximum = 40,
            Value = 12,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
        };
        var headOnly = new CheckBox { Content = Ui("Head only") };
        var overlay = new CheckBox { Content = Ui("Include outer skin layer"), IsChecked = true };
        var shadow = new CheckBox { Content = Ui("Soft shadow"), IsChecked = true };
        var slim = new CheckBox { Content = Ui("Slim arms (Alex model)") };
        var background = Choice(
            new[] { "Transparent", "Light", "Dark", "Custom color" },
            "Custom color"
        );
        background.Tag = "pfp-background";
        var gradient = new CheckBox { Content = Ui("Gradient background"), Tag = "pfp-gradient" };
        var start = Input("#00B4A6");
        var end = Input("#005C64");
        var angle = new Slider
        {
            Minimum = 0,
            Maximum = 360,
            Value = 90,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
        };
        var customField = Field("Background color (#RRGGBB)", start);
        var gradientOptions = Stack(
            Field("Gradient end color (#RRGGBB)", end),
            Field("Gradient angle (°)", angle)
        );
        gradientOptions.Tag = "pfp-gradient-options";
        gradientOptions.IsVisible = false;
        async Task Refresh()
        {
            var revision = ++renderRevision;
            exportBytes = null;
            export.IsEnabled = false;
            customField.IsVisible = background.SelectedIndex == 3;
            gradientOptions.IsVisible = gradient.IsChecked == true && background.SelectedIndex != 0;
            if (pixels == null)
                return;
            byte[]? first = null,
                second = null;
            if (background.SelectedIndex != 0)
            {
                var hex = background.SelectedIndex switch
                {
                    1 => "#F5F5F5",
                    2 => "#202020",
                    _ => start.Text,
                };
                if (hex?.Length != 7 || hex[0] != '#' || !Color.TryParse(hex, out var color))
                    throw new IOException(Ui("Enter a color in #RRGGBB format."));
                first = [color.B, color.G, color.R];
            }
            if (gradient.IsChecked == true && background.SelectedIndex != 0)
            {
                if (
                    end.Text?.Length != 7
                    || end.Text[0] != '#'
                    || !Color.TryParse(end.Text, out var color)
                )
                    throw new IOException(Ui("Enter a color in #RRGGBB format."));
                second = [color.B, color.G, color.R];
            }
            var dimension = (int)(size.SelectedItem ?? 512);
            var portrait = headOnly.IsChecked != true;
            var layer = overlay.IsChecked == true;
            var inset = (int)padding.Value;
            var shadows = shadow.IsChecked == true;
            var isSlim = slim.IsChecked == true;
            var degrees = angle.Value;
            var source = pixels;
            var w = width;
            var h = height;
            var data = await Task.Run(() =>
                MinecraftPfp.Render(
                    source,
                    w,
                    h,
                    dimension,
                    layer,
                    inset,
                    first,
                    portrait,
                    shadows,
                    isSlim,
                    second,
                    degrees
                )
            );
            if (revision != renderRevision)
                return;
            var bitmap = new WriteableBitmap(
                new PixelSize(dimension, dimension),
                new Vector(96, 96),
                PixelFormats.Bgra8888,
                AlphaFormat.Unpremul
            );
            using (var framebuffer = bitmap.Lock())
                for (var y = 0; y < dimension; y++)
                    Marshal.Copy(
                        data,
                        y * dimension * 4,
                        IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                        dimension * 4
                    );
            var old = rendered;
            rendered = bitmap;
            preview.Source = bitmap;
            old?.Dispose();
            using var encoded = new MemoryStream();
            bitmap.Save(encoded);
            exportBytes = encoded.ToArray();
            export.IsEnabled = true;
            info.Text = "";
        }
        async Task Load(byte[] png)
        {
            if (
                png.Length > 8 * 1024 * 1024
                || png.Length < 24
                || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            )
                throw new IOException("Use a PNG Minecraft skin.");
            var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
            var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            if (w < 64 || w > 1024 || w % 64 != 0 || h != w && h != w / 2)
                throw new IOException(
                    "Use a 64×64 or 64×32 Minecraft skin, or an HD multiple up to 1024 pixels wide."
                );
            using var source = new Bitmap(new MemoryStream(png));
            using var converted = new WriteableBitmap(
                source.PixelSize,
                new Vector(96, 96),
                PixelFormats.Bgra8888,
                AlphaFormat.Unpremul
            );
            width = source.PixelSize.Width;
            height = source.PixelSize.Height;
            pixels = new byte[width * height * 4];
            using (var framebuffer = converted.Lock())
            {
                source.CopyPixels(framebuffer, AlphaFormat.Unpremul);
                for (var y = 0; y < height; y++)
                    Marshal.Copy(
                        IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                        pixels,
                        y * width * 4,
                        width * 4
                    );
            }
            await Refresh();
        }
        async Task Player()
        {
            var name = username.Text?.Trim() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{1,16}$"))
                throw new IOException("Enter a Minecraft Java username.");
            info.Text = Ui("Loading player skin…");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                navigationRequest?.Token ?? default
            );
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var identity = await session.Core.Json(
                    "https://api.minecraftservices.com/minecraft/profile/lookup/name/"
                        + Uri.EscapeDataString(name),
                    timeout.Token
                );
                if (!Guid.TryParse(identity.S("id"), out var uuid))
                    throw new IOException("Minecraft Java player not found.");
                avatarSkins ??= new MinecraftSkins(
                    session.Core,
                    session.Store.Root,
                    message => session.Log("WARN", message)
                );
                var skin =
                    await avatarSkins.Get(uuid.ToString("N")).WaitAsync(timeout.Token)
                    ?? throw new IOException(
                        Ui("Could not load this player's skin. Try again or upload a PNG skin.")
                    );
                timeout.Token.ThrowIfCancellationRequested();
                await Load(skin);
                info.Text = name;
            }
            catch
            {
                info.Text = Ui(
                    "Could not load this player's skin. Try again or upload a PNG skin."
                );
                throw;
            }
        }
        export = Btn(
            "Export PNG",
            async () =>
            {
                var bytes = exportBytes;
                if (bytes == null)
                    return;
                var path = await SaveFile("MinecraftPFP", ".png");
                if (path != null)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                    ShowToast(Ui("Avatar exported"));
                }
            },
            true
        );
        export.IsEnabled = false;
        export.Tag = "pfp-export";
        var upload = Btn(
            "Upload skin",
            async () =>
            {
                var file = await OpenFile("*.png");
                if (file == null)
                    return;
                if (new FileInfo(file).Length > 8 * 1024 * 1024)
                    throw new IOException("Skin is too large.");
                await Load(await File.ReadAllBytesAsync(file));
            }
        );
        var controls = Stack(
            username,
            Btn("Load player skin", Player),
            Row(upload, export),
            info,
            Field("Export size", size),
            overlay,
            headOnly,
            shadow,
            slim,
            Field("Padding (%)", padding),
            Field("Background", background),
            customField,
            gradient,
            gradientOptions
        );
        var picture = Card(Stack(T("Avatar preview", 16, true), preview));
        picture.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        var options = Card(controls);
        options.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 16,
        };
        layout.Tag = "pfp-layout";
        layout.Children.Add(picture);
        layout.Children.Add(options);
        Grid.SetColumn(options, 1);
        layout.SizeChanged += (_, _) =>
        {
            var wide = layout.Bounds.Width >= 640;
            Grid.SetColumnSpan(picture, wide ? 1 : 2);
            Grid.SetColumnSpan(options, wide ? 1 : 2);
            Grid.SetColumn(options, wide ? 1 : 0);
            Grid.SetRow(options, wide ? 0 : 1);
        };
        var body = Stack(Btn("Back to toolbox", () => Navigate("toolbox")), layout);
        async Task RefreshSafely()
        {
            try
            {
                await Refresh();
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        }
        username.KeyDown += async (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                try
                {
                    await Player();
                }
                catch (Exception ex)
                {
                    await Error(ex);
                }
            }
        };
        foreach (var combo in new[] { size, background })
            combo.SelectionChanged += async (_, _) => await RefreshSafely();
        foreach (var check in new[] { overlay, headOnly, shadow, slim, gradient })
            check.IsCheckedChanged += async (_, _) => await RefreshSafely();
        foreach (var input in new[] { start, end })
            input.TextChanged += async (_, _) => await RefreshSafely();
        padding.ValueChanged += async (_, _) => await RefreshSafely();
        angle.ValueChanged += async (_, _) => await RefreshSafely();
        preview.DetachedFromVisualTree += (_, _) =>
        {
            renderRevision++;
            preview.Source = null;
            rendered?.Dispose();
            rendered = null;
        };
        return body;
    }
}
