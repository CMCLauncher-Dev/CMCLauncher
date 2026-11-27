using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyScrollBarAppearance(Action<bool, string> check)
    {
        var savedPage = page;
        var settings = store.Config.Settings;
        var savedSetting = settings.ShowScrollBars;
        var savedGlass = settings.MicaEffect;
        var savedGlassBorders = settings.ShowGlassCardBorders;
        var savedGlassStyle = settings.GlassStyle;
        var savedGlassOpacity = settings.GlassOpacityPercent;
        var savedGlassBlur = settings.GlassBackgroundBlur;
        try
        {
            settings.MicaEffect = false;
            settings.ShowGlassCardBorders = true;
            settings.GlassStyle = "aero";
            settings.GlassBackgroundBlur = false;
            page = "personalization";
            await RenderAsync();
            var toggle = AppearanceElements(PageBody)
                .OfType<ToggleSwitch>()
                .Single(t => t.Tag?.ToString() == "show-scrollbars");
            var glassToggle = AppearanceElements(PageBody)
                .OfType<ToggleSwitch>()
                .Single(t => t.Tag?.ToString() == "mica-effect");
            check(
                PageBody
                    .Children.OfType<Border>()
                    .Any(card =>
                        card.Child is StackPanel panel
                        && panel.Children.FirstOrDefault() is Grid row
                        && row.Children.Contains(toggle)
                        && row.Children.Contains(glassToggle)
                        && Grid.GetColumn(toggle) != Grid.GetColumn(glassToggle)
                    ),
                "scrollbar and liquid glass switches share the first row in the controls card"
            );
            var borderToggle = AppearanceElements(PageBody)
                .OfType<ToggleSwitch>()
                .Single(t => t.Tag?.ToString() == "show-glass-card-borders");
            var glassOptions = AppearanceElements(PageBody).OfType<Expander>().Single();
            var glassStyle = AppearanceElements((StackPanel)glassOptions.Content)
                .OfType<ComboBox>().Single();
            check(
                glassOptions.Visibility == Visibility.Collapsed,
                "glass options are hidden with glass off"
            );
            glassToggle.IsOn = true;
            check(
                glassOptions.Visibility == Visibility.Visible && !glassOptions.IsExpanded
                    && glassStyle.SelectedIndex == 1,
                "enabling glass reveals the options and retains the chosen style"
            );
            glassOptions.IsExpanded = true;
            var opacity = ((StackPanel)glassOptions.Content).Children.OfType<Slider>().Single();
            opacity.Value = 60;
            var blurToggle = AppearanceElements((StackPanel)glassOptions.Content)
                .OfType<ToggleSwitch>()
                .Single(t => t.Tag?.ToString() == "glass-background-blur");
            check(
                !blurToggle.IsOn && !settings.GlassBackgroundBlur,
                "background blur defaults off, keeping the clear-transparent behavior"
            );
            blurToggle.IsOn = true;
            for (
                var attempt = 0;
                attempt < 50 && !new Store(store.Root).Config.Settings.GlassBackgroundBlur;
                attempt++
            )
                await Task.Delay(20);
            var preview = PageBody.Children.OfType<Border>().First();
            check(
                preview.Background is SolidColorBrush { Color.A: 153 }
                    && new Store(store.Root).Config.Settings.GlassBackgroundBlur,
                "background blur switches independently and does not share the opacity slider value"
            );
            borderToggle.IsOn = false;
            for (
                var attempt = 0;
                attempt < 50 && new Store(store.Root).Config.Settings.ShowGlassCardBorders;
                attempt++
            )
                await Task.Delay(20);
            var cardPreview = PageBody.Children.OfType<Border>().First();
            check(
                cardPreview.BorderBrush is SolidColorBrush { Color.A: 0 }
                    && !new Store(store.Root).Config.Settings.ShowGlassCardBorders,
                "the border toggle immediately hides existing card outlines and persists"
            );
            glassToggle.IsOn = false;
            check(
                glassOptions.Visibility == Visibility.Collapsed && !glassOptions.IsExpanded
                    && cardPreview.BorderBrush is SolidColorBrush { Color.A: > 0 }
                    && !settings.ShowGlassCardBorders,
                "disabling glass restores regular outlines without losing the border preference"
            );
            toggle.IsOn = true;
            for (var i = 0; i < 50 && !new Store(store.Root).Config.Settings.ShowScrollBars; i++)
                await Task.Delay(20);
            PageViewport.UpdateLayout();
            check(
                new Store(store.Root).Config.Settings.ShowScrollBars,
                "scrollbar preference persists immediately"
            );
            var bar = ScrollVisuals(PageViewport)
                .OfType<ScrollBar>()
                .First(b => b.Orientation == Orientation.Vertical && b.Maximum > 0);
            var thumb = ScrollVisuals(bar).OfType<Thumb>().First(t => t.Name == "VerticalThumb");
            check(
                bar.Opacity == 1
                    && bar.IsHitTestVisible
                    && thumb.ActualWidth == 4
                    && thumb.ActualHeight > 0,
                $"enabled scrollbars have a usable four-pixel thumb (opacity={bar.Opacity}, hit={bar.IsHitTestVisible}, width={thumb.ActualWidth}, height={thumb.ActualHeight}, bar={bar.ActualWidth}x{bar.ActualHeight})"
            );
            check(
                thumb.Background is SolidColorBrush brush
                    && brush.Color.R == 136
                    && brush.Color.G == 136
                    && brush.Color.B == 136,
                "scrollbar thumb stays neutral gray"
            );
            toggle.IsOn = false;
            PageViewport.ChangeView(null, Math.Min(120, PageViewport.ScrollableHeight), null, true);
            await Task.Delay(100);
            check(
                bar.Opacity == 0 && !bar.IsHitTestVisible && PageViewport.VerticalOffset > 0,
                "hiding scrollbar chrome preserves page scrolling"
            );
        }
        finally
        {
            settings.ShowScrollBars = savedSetting;
            settings.MicaEffect = savedGlass;
            settings.ShowGlassCardBorders = savedGlassBorders;
            settings.GlassStyle = savedGlassStyle;
            settings.GlassOpacityPercent = savedGlassOpacity;
            settings.GlassBackgroundBlur = savedGlassBlur;
            await store.Save();
            ApplyTheme();
            ApplyPersonalization();
            page = savedPage;
            await RenderAsync();
        }
    }

    static IEnumerable<DependencyObject> ScrollVisuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ScrollVisuals(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}