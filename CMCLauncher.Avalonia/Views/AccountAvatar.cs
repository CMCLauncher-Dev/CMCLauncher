using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    MinecraftSkins? avatarSkins;

    Control AccountAvatar(Account? account, int size = 32)
    {
        var fallback = NavigationIcon(
            "M12,3 A4,4 0 1 0 12,11 A4,4 0 1 0 12,3 M4,22 L4,18 C4,12 20,12 20,18 L20,22"
        );
        var holder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Child = fallback,
        };
        if (account?.Type != "microsoft" || !Guid.TryParse(account.Uuid, out _))
            return holder;
        var lifetime = 0;
        WriteableBitmap? rendered = null;
        holder.AttachedToVisualTree += async (_, _) =>
        {
            var revision = ++lifetime;
            try
            {
                avatarSkins ??= new MinecraftSkins(
                    session.Core,
                    session.Store.Root,
                    message => session.Log("WARN", message)
                );
                var png = await avatarSkins.Get(account.Uuid, Verifying);
                if (png == null || revision != lifetime || !holder.IsAttachedToVisualTree())
                    return;
                using var input = new MemoryStream(png);
                using var skin = new Bitmap(input);
                var width = skin.PixelSize.Width;
                var height = skin.PixelSize.Height;
                if (
                    width < 64
                    || width > 1024
                    || width % 64 != 0
                    || (height != width && height != width / 2)
                )
                    return;
                using var converted = new WriteableBitmap(
                    skin.PixelSize,
                    new Vector(96, 96),
                    PixelFormats.Bgra8888,
                    AlphaFormat.Unpremul
                );
                var pixels = new byte[width * height * 4];
                using (var frame = converted.Lock())
                {
                    skin.CopyPixels(frame, AlphaFormat.Unpremul);
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(
                            IntPtr.Add(frame.Address, y * frame.RowBytes),
                            pixels,
                            y * width * 4,
                            width * 4
                        );
                }
                var avatar = await Task.Run(() =>
                    MinecraftPfp.Render(
                        pixels,
                        width,
                        height,
                        128,
                        true,
                        0,
                        null,
                        false,
                        false,
                        false,
                        null,
                        0
                    )
                );
                if (revision != lifetime || !holder.IsAttachedToVisualTree())
                    return;
                rendered = new WriteableBitmap(
                    new PixelSize(128, 128),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888,
                    AlphaFormat.Unpremul
                );
                using (var frame = rendered.Lock())
                    for (var y = 0; y < 128; y++)
                        Marshal.Copy(
                            avatar,
                            y * 128 * 4,
                            IntPtr.Add(frame.Address, y * frame.RowBytes),
                            128 * 4
                        );
                var image = new Image { Source = rendered, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
                holder.Child = image;
            }
            catch (Exception ex)
            {
                session.Log("DEBUG", ex.Message);
            }
        };
        holder.DetachedFromVisualTree += (_, _) =>
        {
            lifetime++;
            holder.Child = fallback;
            rendered?.Dispose();
            rendered = null;
        };
        return holder;
    }
}
