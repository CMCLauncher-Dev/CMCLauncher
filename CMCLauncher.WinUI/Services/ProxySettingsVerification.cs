using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyProxySettings(Action<bool, string> check, string directory)
    {
        var previous = page;
        page = "settings";
        await RenderAsync();
        ((FrameworkElement)Content).UpdateLayout();
        var field = Descendants(PageBody)
            .OfType<StackPanel>()
            .Single(p => p.Tag?.ToString() == "proxy-input-group");
        check(
            field.Children.Count == 2
                && field.Children[1] is TextBlock { Visibility: Visibility.Collapsed },
            "empty proxy validation does not occupy a row between proxy and update settings"
        );
        var address = (TextBox)field.Children[0];
        var source = Descendants(PageBody)
            .OfType<ComboBox>()
            .Single(c => c.Tag?.ToString() == "game-download-source");
        check(
            source.Items.Count == 3 && source.SelectedIndex >= 0,
            "game download source offers mirror, automatic fallback and official priorities"
        );
        check(
            field.ActualHeight <= address.ActualHeight + 5,
            "proxy input group has no reserved empty validation height"
        );
        var modes = Descendants(PageBody)
            .OfType<ComboBox>()
            .Single(c => c.Tag?.ToString() == "proxy-mode");
        check(
            modes.Items.Count == 3 && address.IsEnabled == (modes.SelectedIndex == 2),
            "proxy mode offers system, direct and manual choices with matching address state"
        );
        field.StartBringIntoView();
        await Task.Delay(250);
        await Capture(Path.Combine(directory, "proxy-settings.png"));
        var advanced = Descendants(PageBody)
            .OfType<Expander>()
            .Single(e => e.Tag?.ToString() == "advanced-network-settings");
        advanced.IsExpanded = true;
        await Task.Delay(150);
        check(
            Descendants(advanced).OfType<PasswordBox>().Any()
                && Descendants(advanced).OfType<TextBox>().Any(t => t.AcceptsReturn),
            "advanced proxy settings include password entry and host bypass rules"
        );
        page = previous;
        await RenderAsync();
    }
}
