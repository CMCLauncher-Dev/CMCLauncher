using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyMinecraftPfp(Action<bool, string> check, string directory)
    {
        var previous = page;
        page = "minecraft-pfp";
        await RenderAsync();
        check(
            !Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "pfp-export")
                .IsEnabled,
            "MinecraftPFP cannot export before a skin is loaded"
        );
        var skin = new byte[64 * 64 * 4];
        for (var y = 8; y < 16; y++)
        for (var x = 8; x < 16; x++)
        {
            var pixel = (y * 64 + x) * 4;
            skin[pixel] = 70;
            skin[pixel + 1] = (byte)(x * 12);
            skin[pixel + 2] = (byte)(y * 12);
            skin[pixel + 3] = 255;
        }
        PageBody.Children.Clear();
        // Give the portrait fixture a shirt and two distinct arms as well as a face.
        for (var y = 20; y < 32; y++)
        for (var x = 20; x < 48; x++)
        {
            var p = (y * 64 + x) * 4;
            skin[p] = 170;
            skin[p + 1] = 120;
            skin[p + 2] = 95;
            skin[p + 3] = 255;
        }
        for (var y = 52; y < 64; y++)
        for (var x = 36; x < 40; x++)
        {
            var p = (y * 64 + x) * 4;
            skin[p] = 170;
            skin[p + 1] = 120;
            skin[p + 2] = 95;
            skin[p + 3] = 255;
        }
        await MinecraftPfpPage(skin);
        var image = Descendants(PageBody)
            .OfType<Image>()
            .Single(i => i.Tag?.ToString() == "pfp-preview");
        check(
            image.Source is BitmapImage { PixelWidth: 512, PixelHeight: 512 }
                && Descendants(PageBody)
                    .OfType<Button>()
                    .Single(b => b.Tag?.ToString() == "pfp-export")
                    .IsEnabled,
            "MinecraftPFP encodes a valid 512px PNG and enables export after preview"
        );
        await Capture(Path.Combine(directory, "minecraft-pfp.png"));
        var background = Descendants(PageBody)
            .OfType<BelowComboBox>()
            .Single(c => c.Tag?.ToString() == "pfp-background");
        for (var i = 0; i < 3; i++)
        {
            background.IsDropDownOpen = true;
            await Task.Delay(100);
            background.SelectedIndex = i;
            background.IsDropDownOpen = false;
            await Task.Delay(200);
        }
        check(
            Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "pfp-export")
                .IsEnabled,
            "MinecraftPFP background popup remains responsive after repeated open and selection"
        );
        check(
            Descendants(PageBody).OfType<TextBox>().Any(t => t.Tag?.ToString() == "pfp-username"),
            "MinecraftPFP accepts a Minecraft Java player username"
        );
        var gradient = Descendants(PageBody)
            .OfType<CheckBox>()
            .Single(c => c.Tag?.ToString() == "pfp-gradient");
        gradient.IsChecked = true;
        await Task.Delay(300);
        check(
            Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "pfp-export")
                .IsEnabled,
            "MinecraftPFP gradient background regenerates an exportable PNG"
        );

        using var encoded = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encoded);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            64,
            64,
            96,
            96,
            skin
        );
        await encoder.FlushAsync();
        using var reader = new DataReader(encoded.GetInputStreamAt(0));
        await reader.LoadAsync((uint)encoded.Size);
        var fixture = new byte[(int)encoded.Size];
        reader.ReadBytes(fixture);
        var originalFetch = fetchPfpSkin;
        var response = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            fetchPfpSkin = (_, ct) => response.Task.WaitAsync(ct);
            await RenderAsync();
            var history = recentWork.Count;
            Descendants(PageBody)
                .OfType<TextBox>()
                .Single(t => t.Tag?.ToString() == "pfp-username")
                .Text = "Steve";
            var load = Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "pfp-fetch");
            (
                (IInvokeProvider)new ButtonAutomationPeer(load).GetPattern(PatternInterface.Invoke)
            ).Invoke();
            await Task.Delay(50);
            check(
                activeWork == null && recentWork.Count == history && !load.IsEnabled,
                "skin lookup stays local to the page without starting a managed task"
            );
            response.SetResult(fixture);
            for (var i = 0; i < 200 && !load.IsEnabled; i++)
                await Task.Delay(25);
            check(
                load.IsEnabled
                    && recentWork.Count == history
                    && Descendants(PageBody)
                        .OfType<Button>()
                        .Single(b => b.Tag?.ToString() == "pfp-export")
                        .IsEnabled,
                "skin lookup decodes its PNG and creates no recent-task entry"
            );
        }
        finally
        {
            fetchPfpSkin = originalFetch;
            response.TrySetCanceled();
        }

        var oldHistory = recentWork.ToArray();
        var oldDrawer = TaskDrawer.IsPaneOpen;
        try
        {
            await Run(
                _ =>
                {
                    TaskDrawer.IsPaneOpen = true;
                    Tasks();
                    Report("Task presentation fixture");
                    check(
                        activeWork?.Stage == "Task presentation fixture"
                            && taskStage?.Text == "Task presentation fixture"
                            && taskMetrics?.Visibility == Visibility.Collapsed,
                        "local task stage updates immediately without duplicated waiting or transfer text"
                    );
                    UpdateDownloadProgress(25);
                    check(
                        taskMetrics != null && !taskMetrics.Text.Contains(ByteSize(0)),
                        "local progress does not imply network traffic"
                    );
                    activeWork!.Progress = null;
                    activeWork.Bytes = 16;
                    RefreshTaskMetrics();
                    check(
                        taskMetrics != null && !taskMetrics.Text.Contains(activeWork.Stage),
                        "network metrics do not repeat an unknown-progress stage"
                    );
                    activeWork.Bytes = 0;
                    return Task.CompletedTask;
                },
                "Task presentation fixture"
            );
            check(
                recentWork[0].Stage == recentWork[0].State && recentWork[0].Progress == 100,
                "completed tasks replace processing with a final state and complete progress"
            );
            Tasks();
            check(
                !Descendants(TaskDrawerBody)
                    .OfType<TextBlock>()
                    .Any(t => t.Text.Contains(UiFormat("Received {0}", ByteSize(0)))),
                "task history omits zero-byte transfers"
            );
        }
        finally
        {
            recentWork.Clear();
            recentWork.AddRange(oldHistory);
            TaskDrawer.IsPaneOpen = oldDrawer;
            Tasks();
        }
        page = previous;
        await RenderAsync();
    }
}
