using System.Runtime.InteropServices.WindowsRuntime;
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
        var yaw = .52f;
        var pitch = .24f;
        var pose = SkinPose.Standing;
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
        previewSlot.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var poseButtons = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var previewRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children = { previewSlot, poseButtons },
        };
        var status = Text("Import a skin to preview it.");
        var url = new TextBox { Header = Ui("Skin image URL"), PlaceholderText = "https://…" };
        var slim = new CheckBox { Content = Ui("Slim arms") };
        var fileButton = new Button { Content = Ui("Choose PNG file") };
        var urlButton = new Button { Content = Ui("Import from URL") };
        var playerName = new TextBox { Header = Ui("Minecraft Java username") };
        var playerButton = new Button { Content = Ui("Load player skin") };
        static Grid InputWithAction(TextBox input, Button button)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            button.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(button, 1);
            row.Children.Add(input);
            row.Children.Add(button);
            return row;
        }

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
                    Children =
                    {
                        previewRow, status, fileButton,
                        InputWithAction(url, urlButton),
                        InputWithAction(playerName, playerButton),
                        slim,
                    },
                },
            },
        };

        var dialogWidth = Math.Min(420, Math.Max(0, Content.XamlRoot.Size.Width - 48));
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;

        void RenderPreview()
        {
            if (texture == null)
                return;
            var pixels = SkinImport.RenderPreview(texture, skinHeight, slim.IsChecked == true, yaw, pitch, pose);
            // WriteableBitmap expects premultiplied BGRA pixels.
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var alpha = pixels[i + 3];
                for (var channel = 0; channel < 3; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * alpha + 127) / 255);
            }
            var bitmap = preview.Source as WriteableBitmap
                ?? new WriteableBitmap(SkinImport.PreviewSize, SkinImport.PreviewSize);
            using (var output = bitmap.PixelBuffer.AsStream())
                output.Write(pixels, 0, pixels.Length);
            bitmap.Invalidate();
            preview.Source = bitmap;
            checkerboard.Visibility = Visibility.Collapsed;
        }
        slim.Checked += (_, _) => RenderPreview();
        slim.Unchecked += (_, _) => RenderPreview();

        (SkinPose Pose, string Icon, string Label)[] poses =
        [
            (SkinPose.Standing, "🧍", "Standing pose"),
            (SkinPose.Walking, "🚶", "Walking pose"),
            (SkinPose.Running, "🏃", "Running pose"),
            (SkinPose.Waving, "👋", "Waving pose"),
        ];
        foreach (var entry in poses)
        {
            var button = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = entry.Icon, Width = 32, Height = 32, MinWidth = 0,
                Padding = new Thickness(0), IsChecked = pose == entry.Pose,
                Tag = entry.Pose,
            };
            ToolTipService.SetToolTip(button, Ui(entry.Label));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Ui(entry.Label));
            button.Click += (_, _) =>
            {
                pose = entry.Pose;
                foreach (Microsoft.UI.Xaml.Controls.Primitives.ToggleButton other in poseButtons.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
                    other.IsChecked = other.Tag is SkinPose value && value == pose;
                RenderPreview();
            };
            poseButtons.Children.Add(button);
        }

        Windows.Foundation.Point? dragOrigin = null;
        previewSlot.PointerPressed += (_, e) =>
        {
            if (texture == null || !e.GetCurrentPoint(previewSlot).Properties.IsLeftButtonPressed)
                return;
            dragOrigin = e.GetCurrentPoint(previewSlot).Position;
            previewSlot.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        previewSlot.PointerMoved += (_, e) =>
        {
            if (dragOrigin is not { } previous)
                return;
            var next = e.GetCurrentPoint(previewSlot).Position;
            dragOrigin = next;
            yaw = (yaw - (float)(next.X - previous.X) * .014f) % MathF.Tau;
            pitch = Math.Clamp(pitch + (float)(next.Y - previous.Y) * .012f, -1.1f, 1.1f);
            RenderPreview();
            e.Handled = true;
        };
        previewSlot.PointerReleased += (_, e) =>
        {
            dragOrigin = null;
            previewSlot.ReleasePointerCapture(e.Pointer);
        };
        previewSlot.PointerCaptureLost += (_, _) => dragOrigin = null;

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
                    RenderPreview();
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
