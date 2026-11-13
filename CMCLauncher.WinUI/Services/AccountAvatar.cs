using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 同一会话中相同 UUID 共用皮肤请求，账号列表和主页不会各下载一遍。
    MinecraftSkins? avatarSkins;

    UIElement AccountAvatar(Account? account, int size = 40)
    {
        var holder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Brush(lightTheme ? "#EEEAF7" : "#343044"),
            Child = new FontIcon { Glyph = "\uE77B", FontSize = size / 2 },
        };
        if (account?.Type == "microsoft" && Guid.TryParse(account.Uuid, out var id))
            _ = LoadAvatar(holder, id.ToString("N"));
        return holder;
    }

    // 优先使用一天内的缓存，网络失败时仍可回退到旧皮肤。
    Task<byte[]?> FetchSkin(string uuid)
    {
        avatarSkins ??= new MinecraftSkins(
            core,
            store.Root,
            message => RuntimeLog.Write("WARN", message)
        );
        return avatarSkins.Get(uuid);
    }

    async Task LoadAvatar(Border holder, string uuid)
    {
        try
        {
            var bytes = await FetchSkin(uuid);
            if (bytes == null)
                return;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth != 64 || decoder.PixelHeight is not (32 or 64))
                return;
            var pixels = (
                await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage
                )
            ).DetachPixelData();
            // 截取皮肤上的 8×8 脸部和帽子层，放大时按整像素取样，保留像素边缘。
            var image = new byte[64 * 64 * 4];
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var face = ((8 + y / 8) * 64 + 8 + x / 8) * 4;
                var hat = ((8 + y / 8) * 64 + 40 + x / 8) * 4;
                var dest = (y * 64 + x) * 4;
                var alpha = pixels[hat + 3] / 255.0;
                for (var c = 0; c < 3; c++)
                    image[dest + c] = (byte)(
                        pixels[hat + c] * alpha + pixels[face + c] * (1 - alpha)
                    );
                image[dest + 3] = 255;
            }
            var bitmap = new WriteableBitmap(64, 64);
            using (var output = bitmap.PixelBuffer.AsStream())
                await output.WriteAsync(image);
            bitmap.Invalidate();
            holder.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }
        catch
        { /* An unavailable skin never prevents account selection or launching. */
        }
    }
}
