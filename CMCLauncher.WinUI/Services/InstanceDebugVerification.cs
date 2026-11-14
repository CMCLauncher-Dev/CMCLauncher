using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyInstanceDebugMode(Action<bool, string> check)
    {
        var instance = new Instance { Name = "Debug fixture" };
        try
        {
            PageBody.Children.Clear();
            InstanceOverview(instance);
            var management = PageBody
                .Children.OfType<Border>()
                .Single(card => Equals(card.Tag, "instance-management"));
            var toggle = Descendants(management)
                .OfType<ToggleSwitch>()
                .Single(control => Equals(control.Tag, "instance-debug-mode"));
            StartInstanceDebugLog(instance);
            check(
                !toggle.IsOn && debugWindow == null,
                "instance debug defaults off and does not open a log window"
            );
            toggle.IsOn = true;
            var saved = JsonSerializer.Serialize(instance, Store.JsonOptions);
            var restored = JsonSerializer.Deserialize<Instance>(saved, Store.JsonOptions)!;
            check(
                restored.DebugMode && !new Instance().DebugMode,
                "debug preference persists per instance without changing other instances"
            );
            StartInstanceDebugLog(instance);
            RuntimeLog.Write("INFO", "instance-debug-live-log-fixture");
            var log = Descendants((DependencyObject)debugWindow!.Content)
                .OfType<TextBox>()
                .Single();
            check(
                log.IsReadOnly && log.Text.Contains("instance-debug-live-log-fixture"),
                "instance debug opens a separate window and receives live logs"
            );
            debugWindow.SetProgress(100, Ui("Game is running"));
            check(
                debugWindow.AppWindow.IsVisible,
                "debug log window remains open after game startup"
            );
        }
        finally
        {
            debugWindow?.CloseSafe();
            debugWindow = null;
            await Task.Delay(100);
            await RenderAsync();
        }
    }
}
