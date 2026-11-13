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
        var username = Input("", "Minecraft Java username");
        var info = T("");
        var preview = new Image
        {
            Width = 300,
            Height = 300,
            Stretch = Stretch.Uniform,
        };
        var size = Choice(new[] { 128, 256, 512, 1024 }, 512);
        var padding = Number(12, 0, 40);
        var mode = Choice(new[] { "Portrait", "Head" });
        var overlay = new CheckBox { Content = Ui("Include outer skin layer"), IsChecked = true };
        var shadow = new CheckBox { Content = Ui("Shadow"), IsChecked = true };
        var slim = new CheckBox { Content = Ui("Slim arms") };
        var background = Choice(new[] { "Transparent", "Solid", "Gradient" });
        var start = Input("#00BFA5");
        var end = Input("#2478E8");
        var angle = Number(90, 0, 360, 5);
        async Task Refresh()
        {
            if (pixels == null)
                return;
            var revision = ++renderRevision;
            byte[]? first = null,
                second = null;
            if (background.SelectedIndex != 0)
            {
                if (!Color.TryParse(start.Text, out var color))
                    return;
                first = [color.R, color.G, color.B];
            }
            if (background.SelectedIndex == 2)
            {
                if (!Color.TryParse(end.Text, out var color))
                    return;
                second = [color.R, color.G, color.B];
            }
            var dimension = (int)(size.SelectedItem ?? 512);
            var portrait = mode.SelectedIndex == 0;
            var layer = overlay.IsChecked == true;
            var inset = (int)(padding.Value ?? 12);
            var shadows = shadow.IsChecked == true;
            var isSlim = slim.IsChecked == true;
            var degrees = (double)(angle.Value ?? 90);
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
        var body = Stack(
            Btn("Back to toolbox", () => Navigate("toolbox")),
            Row(
                username,
                Btn("Load player skin", Player),
                Btn(
                    "Upload skin",
                    async () =>
                    {
                        var file = await OpenFile("*.png");
                        if (file != null)
                        {
                            if (new FileInfo(file).Length > 8 * 1024 * 1024)
                                throw new IOException("Skin is too large.");
                            await Load(await File.ReadAllBytesAsync(file));
                        }
                    }
                )
            ),
            info,
            Card(preview),
            Row(size, mode, background),
            Field("Padding / %", padding),
            overlay,
            shadow,
            slim,
            Field("Background color", start),
            Field("Gradient end color", end),
            Field("Gradient angle", angle),
            Btn(
                "Export PNG",
                async () =>
                {
                    if (rendered == null)
                        throw new IOException("Load a skin first.");
                    var path = await SaveFile("MinecraftPFP", ".png");
                    if (path != null)
                        rendered.Save(path);
                },
                true
            )
        );
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
        foreach (var combo in new[] { size, mode, background })
            combo.SelectionChanged += async (_, _) => await RefreshSafely();
        foreach (var check in new[] { overlay, shadow, slim })
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
