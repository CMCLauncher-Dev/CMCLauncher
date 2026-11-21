using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task EditPlayerSkin(Account account)
    {
        using var lifetime = new CancellationTokenSource();
        byte[]? candidate = null;
        byte[]? texture = null;
        int skinHeight = 0;
        var previewRevision = 0;
        var preview = new Image { Width = 224, Height = 224, Stretch = Stretch.Uniform };
        var checkerboard = new Canvas { Width = 224, Height = 224, Background = Brush("#FAFAFA") };
        var gray = Brush("#D9D9D9");
        const int tileSize = 28;
        for (var y = 0; y < 8; y++)
        for (var x = (y + 1) % 2; x < 8; x += 2)
        {
            var square = new Border { Width = tileSize, Height = tileSize, Background = gray };
            Canvas.SetLeft(square, x * tileSize);
            Canvas.SetTop(square, y * tileSize);
            checkerboard.Children.Add(square);
        }
        var previewSlot = new Grid { Width = 224, Height = 224, Children = { checkerboard, preview } };
        var status = Text("Import a skin to preview it.");
        var url = new TextBox { Header = Ui("Skin image URL"), PlaceholderText = "https://…" };
        var slim = new CheckBox { Content = Ui("Slim arms") };
        var fileButton = new Button { Content = Ui("Choose PNG file") };
        var urlButton = new Button { Content = Ui("Import from URL") };
        var playerName = new TextBox { Header = Ui("Minecraft Java username") };
        var playerButton = new Button { Content = Ui("Load player skin") };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Change skin") + " · " + account.Name,
            PrimaryButtonText = Ui("Apply skin"),
            CloseButtonText = Ui("Cancel"),
            IsPrimaryButtonEnabled = false,
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children = { previewSlot, status, fileButton, url, urlButton, playerName, playerButton, slim },
                },
            },
        };

        async Task RenderPreview()
        {
            if (texture == null)
                return;
            var revision = ++previewRevision;
            var pixels = SkinImport.RenderPreview(texture, skinHeight, slim.IsChecked == true);
            using var image = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, image);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, SkinImport.PreviewSize, SkinImport.PreviewSize, 96, 96, pixels);
            await encoder.FlushAsync();
            image.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(image);
            if (!lifetime.IsCancellationRequested && revision == previewRevision)
            {
                preview.Source = bitmap;
                checkerboard.Visibility = Visibility.Collapsed;
            }
        }
        async void ModelChanged(object sender, RoutedEventArgs args)
        {
            try { await RenderPreview(); }
            catch (Exception ex) { status.Text = Ui(ex.Message); }
        }
        slim.Checked += ModelChanged;
        slim.Unchecked += ModelChanged;

        async Task Import(Func<Task<byte[]?>> read)
        {
            fileButton.IsEnabled = urlButton.IsEnabled = playerButton.IsEnabled = dialog.IsPrimaryButtonEnabled = false;
            status.Text = Ui("Loading skin…");
            try
            {
                var bytes = await read();
                if (bytes != null && !lifetime.IsCancellationRequested)
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, stream);
                    var pixels = (await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage
                    )).DetachPixelData();
                    if (lifetime.IsCancellationRequested)
                        return;
                    texture = pixels;
                    skinHeight = (int)decoder.PixelHeight;
                    slim.IsEnabled = skinHeight == 64;
                    if (!slim.IsEnabled)
                        slim.IsChecked = false;
                    await RenderPreview();
                    candidate = bytes;
                }
                status.Text = Ui(candidate == null ? "Import a skin to preview it." : "Skin ready to apply.");
            }
            catch (Exception ex) when (!lifetime.IsCancellationRequested)
            {
                status.Text = Ui(ex.Message);
            }
            catch (Exception) when (lifetime.IsCancellationRequested) { }
            finally
            {
                fileButton.IsEnabled = urlButton.IsEnabled = playerButton.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = candidate != null;
            }
        }

        fileButton.Click += async (_, _) => await Import(async () =>
        {
            var picker = new DesktopFileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFileAsync() is not { } file)
                return null;
            using var stream = await file.OpenReadAsync();
            using var input = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
            return await SkinImport.Read(input, lifetime.Token);
        });
        urlButton.Click += async (_, _) => await Import(async () => await SkinImport.Download(url.Text, lifetime.Token));
        playerButton.Click += async (_, _) => await Import(async () =>
        {
            avatarSkins ??= new MinecraftSkins(core, store.Root, message => RuntimeLog.Write("WARN", message));
            var bytes = await avatarSkins.FindByName(playerName.Text ?? "", lifetime.Token);
            if (!SkinImport.IsStandardSkin(bytes))
                throw new InvalidDataException("Select a 64×64 or 64×32 PNG skin.");
            return bytes;
        });
        var result = await dialog.ShowAsync();
        lifetime.Cancel();
        if (result != ContentDialogResult.Primary || candidate == null)
            return;

        await Run(async ct =>
        {
            var refreshed = await auth.Refresh(account, MicrosoftAuth.ConfiguredClientId, ct);
            if (!ReferenceEquals(account, refreshed))
                await SaveAccount(refreshed);
            await SkinImport.Apply(core, refreshed, candidate, slim.IsChecked == true, ct);
            avatarSkins ??= new MinecraftSkins(core, store.Root, message => RuntimeLog.Write("WARN", message));
            await avatarSkins.RememberAppliedSkin(account.Uuid, candidate);
        }, "Apply skin");
        Render();
        ShowToast(Ui("Skin applied."));
    }
}
