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
        var savedSetting = store.Config.Settings.ShowScrollBars;
        try
        {
            page = "personalization";
            await RenderAsync();
            var toggle = AppearanceElements(PageBody)
                .OfType<ToggleSwitch>()
                .Single(t => t.Tag?.ToString() == "show-scrollbars");
            check(
                PageBody
                    .Children.OfType<Border>()
                    .Any(card =>
                        card.Child is StackPanel panel && panel.Children.FirstOrDefault() == toggle
                    ),
                "scrollbar toggle is the first control in the controls card"
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
            store.Config.Settings.ShowScrollBars = savedSetting;
            await store.Save();
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
