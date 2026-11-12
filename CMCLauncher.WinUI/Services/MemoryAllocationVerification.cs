using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyMemoryAllocation(Action<bool, string> check, string directory)
    {
        var estimate = calculateAutomaticMemory;
        var settings = store.Config.Settings;
        var oldAuto = settings.AutoMemory;
        var oldManual = settings.MemoryMb;
        var amount = 4096;
        var calls = 0;
        try
        {
            calculateAutomaticMemory = () =>
            {
                calls++;
                return amount;
            };
            settings.AutoMemory = true;
            settings.MemoryMb = 3072;
            page = "settings";
            await RenderAsync();
            await Task.Delay(100);
            var automatic = Descendants(PageBody)
                .OfType<ToggleSwitch>()
                .Single(c => c.Tag?.ToString() == "settings-memory-auto");
            var slider = Descendants(PageBody)
                .OfType<Slider>()
                .Single(c => c.Tag?.ToString() == "settings-memory-slider");
            var progress = Descendants(PageBody)
                .OfType<Border>()
                .Single(c => c.Tag?.ToString() == "settings-memory-progress");
            var value = Descendants(PageBody)
                .OfType<TextBlock>()
                .Single(c => c.Tag?.ToString() == "settings-memory-value");
            PageViewport.ChangeView(
                null,
                Math.Max(
                    0,
                    value
                        .TransformToVisual(PageBody)
                        .TransformPoint(new Windows.Foundation.Point())
                        .Y - 160
                ),
                null,
                true
            );
            amount = 6144;
            await Task.Delay(1200);
            check(
                calls >= 2
                    && progress.Visibility == Visibility.Visible
                    && slider.Visibility == Visibility.Collapsed
                    && value.Text == UiFormat("Game allocation: {0:N1} GB", 6d),
                "automatic memory updates its progress and number every second"
            );
            check(
                settings.MemoryMb == 3072,
                "live memory estimates preserve the saved manual allocation"
            );
            await Capture(Path.Combine(directory, "settings-memory-auto.png"));
            automatic.IsOn = false;
            await Task.Delay(100);
            var paused = calls;
            await Task.Delay(1100);
            check(
                calls == paused
                    && slider.Visibility == Visibility.Visible
                    && slider.IsEnabled
                    && slider.Value == 3072,
                "manual mode restores its slider and stops automatic sampling"
            );
            slider.Value = 5120;
            await Task.Delay(100);
            check(
                settings.MemoryMb == 5120
                    && value.Text == UiFormat("Game allocation: {0:N1} GB", 5d),
                "manual slider updates the allocation and persisted settings"
            );
            await Capture(Path.Combine(directory, "settings-memory-manual.png"));
            automatic.IsOn = true;
            page = "home";
            await RenderAsync();
            await Task.Delay(450);
            var stopped = calls;
            await Task.Delay(1100);
            check(calls == stopped, "leaving settings stops the memory update timer");
        }
        finally
        {
            calculateAutomaticMemory = estimate;
            settings.AutoMemory = oldAuto;
            settings.MemoryMb = oldManual;
            await store.Save();
        }
        page = "settings";
        await RenderAsync();
    }
}
