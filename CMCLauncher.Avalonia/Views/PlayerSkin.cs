using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task EditPlayerSkin(Account account)
    {
        using var lifetime = new CancellationTokenSource();
        byte[]? candidate = null;
        WriteableBitmap? bitmap = null;
        byte[]? texture = null;
        int skinHeight = 0;
        var yaw = .52f;
        var pitch = .24f;
        var pose = SkinPose.Standing;
        var preview = new Image { Width = 224, Height = 224, Stretch = Stretch.Uniform };
        var checkerboard = new Canvas { Width = 224, Height = 224, Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)) };
        var gray = new SolidColorBrush(Color.FromRgb(217, 217, 217));
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
        previewSlot.Background = Brushes.Transparent;
        var poseButtons = new StackPanel { Spacing = 5, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        var previewRow = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 8,
            Children = { previewSlot, poseButtons },
        };
        var status = T("Import a skin to preview it.");
        var url = Input("", "https://…");
        var slim = new CheckBox { Content = Ui("Slim arms") };
        var dialog = new Window
        {
            Title = Ui("Change skin") + " · " + account.Name,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var apply = new Button { Content = Ui("Apply skin"), IsEnabled = false };
        apply.Click += (_, _) => dialog.Close(true);
        var cancel = new Button { Content = Ui("Cancel") };
        cancel.Click += (_, _) => dialog.Close(false);
        var fileButton = new Button { Content = Ui("Choose PNG file") };
        var urlButton = new Button { Content = Ui("Import from URL") };
        var playerName = Input("", "Minecraft Java username");
        var playerButton = new Button { Content = Ui("Load player skin") };

        void RenderPreview()
        {
            if (texture == null)
                return;
            var pixels = SkinImport.RenderPreview(texture, skinHeight, slim.IsChecked == true, yaw, pitch, pose);
            var next = new WriteableBitmap(new PixelSize(SkinImport.PreviewSize, SkinImport.PreviewSize), new Vector(96, 96),
                PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            using (var frame = next.Lock())
                for (var y = 0; y < SkinImport.PreviewSize; y++)
                    Marshal.Copy(pixels, y * SkinImport.PreviewSize * 4, IntPtr.Add(frame.Address, y * frame.RowBytes), SkinImport.PreviewSize * 4);
            preview.Source = next;
            checkerboard.IsVisible = false;
            bitmap?.Dispose();
            bitmap = next;
        }
        slim.IsCheckedChanged += (_, _) => RenderPreview();

        (SkinPose Pose, string Icon, string Label)[] poses =
        [
            (SkinPose.Standing, "🧍", "Standing pose"),
            (SkinPose.Walking, "🚶", "Walking pose"),
            (SkinPose.Running, "🏃", "Running pose"),
            (SkinPose.Waving, "👋", "Waving pose"),
        ];
        foreach (var entry in poses)
        {
            var button = new global::Avalonia.Controls.Primitives.ToggleButton
            {
                Content = entry.Icon, Width = 32, Height = 32, MinWidth = 0,
                Padding = new Thickness(0), IsChecked = pose == entry.Pose,
                Tag = entry.Pose,
            };
            ToolTip.SetTip(button, Ui(entry.Label));
            global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(entry.Label));
            button.Click += (_, _) =>
            {
                pose = entry.Pose;
                foreach (global::Avalonia.Controls.Primitives.ToggleButton other in poseButtons.Children.OfType<global::Avalonia.Controls.Primitives.ToggleButton>())
                    other.IsChecked = other.Tag is SkinPose value && value == pose;
                RenderPreview();
            };
            poseButtons.Children.Add(button);
        }

        Point? dragOrigin = null;
        previewSlot.PointerPressed += (_, e) =>
        {
            if (texture == null || !e.GetCurrentPoint(previewSlot).Properties.IsLeftButtonPressed)
                return;
            dragOrigin = e.GetPosition(previewSlot);
            e.Pointer.Capture(previewSlot);
            e.Handled = true;
        };
        previewSlot.PointerMoved += (_, e) =>
        {
            if (dragOrigin is not { } previous)
                return;
            var next = e.GetPosition(previewSlot);
            dragOrigin = next;
            yaw = (yaw - (float)(next.X - previous.X) * .014f) % MathF.Tau;
            pitch = Math.Clamp(pitch + (float)(next.Y - previous.Y) * .012f, -1.1f, 1.1f);
            RenderPreview();
            e.Handled = true;
        };
        previewSlot.PointerReleased += (_, e) =>
        {
            dragOrigin = null;
            e.Pointer.Capture(null);
        };
        previewSlot.PointerCaptureLost += (_, _) => dragOrigin = null;

        async Task Import(Func<Task<byte[]?>> read)
        {
            fileButton.IsEnabled = urlButton.IsEnabled = playerButton.IsEnabled = apply.IsEnabled = false;
            status.Text = Ui("Loading skin…");
            try
            {
                var bytes = await read();
                if (bytes != null && !lifetime.IsCancellationRequested)
                {
                    using var input = new MemoryStream(bytes);
                    using var source = new Bitmap(input);
                    using var converted = new WriteableBitmap(source.PixelSize, new Vector(96, 96),
                        PixelFormats.Bgra8888, AlphaFormat.Unpremul);
                    var pixels = new byte[64 * source.PixelSize.Height * 4];
                    using (var frame = converted.Lock())
                    {
                        source.CopyPixels(frame, AlphaFormat.Unpremul);
                        for (var y = 0; y < source.PixelSize.Height; y++)
                            Marshal.Copy(IntPtr.Add(frame.Address, y * frame.RowBytes), pixels, y * 64 * 4, 64 * 4);
                    }
                    texture = pixels;
                    skinHeight = source.PixelSize.Height;
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
                apply.IsEnabled = candidate != null;
            }
        }

        fileButton.Click += async (_, _) => await Import(async () =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(
                new global::Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = [new("PNG") { Patterns = ["*.png"] }],
                });
            if (files.FirstOrDefault() is not { } file)
                return null;
            await using var input = await file.OpenReadAsync();
            return await SkinImport.Read(input, lifetime.Token);
        });
        urlButton.Click += async (_, _) => await Import(async () => await SkinImport.Download(url.Text ?? "", lifetime.Token));
        playerButton.Click += async (_, _) => await Import(async () =>
        {
            avatarSkins ??= new MinecraftSkins(session.Core, session.Store.Root, message => session.Log("WARN", message));
            var bytes = await avatarSkins.FindByName(playerName.Text ?? "", lifetime.Token);
            if (!SkinImport.IsStandardSkin(bytes))
                throw new InvalidDataException("Select a 64×64 or 64×32 PNG skin.");
            return bytes;
        });
        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                MaxHeight = 580,
                Content = Stack(previewRow, status, fileButton, Field("Skin image URL", url),
                    urlButton, Field("Minecraft Java username", playerName), playerButton, slim, Row(apply, cancel)),
            },
        };
        bool confirmed;
        try
        {
            confirmed = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            lifetime.Cancel();
            preview.Source = null;
            bitmap?.Dispose();
        }
        if (!confirmed || candidate == null)
            return;

        if (await session.Run("Apply skin", async ct =>
        {
            var refreshed = await new MicrosoftAuth(session.Core).Refresh(
                account, MicrosoftAuth.ConfiguredClientId, ct
            );
            if (!ReferenceEquals(account, refreshed))
            {
                session.Store.Config.Accounts.RemoveAll(item => item.Key == account.Key);
                session.Store.Config.Accounts.Add(refreshed);
                session.Account = refreshed;
                await session.Save();
            }
            await SkinImport.Apply(session.Core, refreshed, candidate, slim.IsChecked == true, ct);
            avatarSkins ??= new MinecraftSkins(session.Core, session.Store.Root, message => session.Log("WARN", message));
            await avatarSkins.RememberAppliedSkin(account.Uuid, candidate);
        }))
            await Navigate("home");
    }
}
