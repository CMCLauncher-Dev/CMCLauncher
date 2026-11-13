using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyPfpParity(List<string> checks)
    {
        await Navigate("pfp");
        var controls = pageContent.GetVisualDescendants().OfType<Control>().ToArray();
        var background = controls.OfType<ComboBox>().Single(c => Equals(c.Tag, "pfp-background"));
        var gradient = controls.OfType<CheckBox>().Single(c => Equals(c.Tag, "pfp-gradient"));
        var options = controls.Single(c => Equals(c.Tag, "pfp-gradient-options"));
        var export = controls.OfType<Button>().Single(c => Equals(c.Tag, "pfp-export"));
        if (
            background.ItemCount != 4
            || background.SelectedIndex != 3
            || export.IsEnabled
            || options.IsVisible
        )
            throw new IOException("Avatar controls do not match WinUI defaults.");
        gradient.IsChecked = true;
        if (!options.IsVisible)
            throw new IOException("Avatar gradient controls did not expand.");
        background.SelectedIndex = 0;
        if (options.IsVisible || export.IsEnabled)
            throw new IOException(
                "Transparent avatars expose gradient controls or allow export without a skin."
            );
        var sliders = controls.OfType<Slider>().ToArray();
        if (
            !sliders.Any(s => s.Maximum == 40 && s.Value == 12)
            || !sliders.Any(s => s.Maximum == 360 && s.Value == 90)
        )
            throw new IOException("Avatar padding and angle sliders differ from WinUI.");
        var layout = controls.OfType<Grid>().Single(c => Equals(c.Tag, "pfp-layout"));
        if (layout.Children.OfType<Border>().Count() != 2)
            throw new IOException("Avatar preview and controls must use separate cards.");
        checks.Add(
            "PASS WinUI avatar background modes, gradient visibility, sliders and export gating"
        );
    }
}
