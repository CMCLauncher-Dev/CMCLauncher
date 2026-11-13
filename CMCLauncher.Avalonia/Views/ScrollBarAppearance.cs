using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    void ConfigureScrollBars()
    {
        Styles.Add(
            new Style(s => s.OfType<ScrollViewer>())
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>())
            {
                Setters =
                {
                    new Setter(OpacityProperty, 0d),
                    new Setter(IsHitTestVisibleProperty, false),
                    new Setter(ScrollBar.AllowAutoHideProperty, false),
                    new Setter(MinWidthProperty, 0d),
                    new Setter(MinHeightProperty, 0d),
                },
            }
        );
        Styles.Add(
            new Style(s => s.Class("show-scrollbars").Descendant().OfType<ScrollBar>())
            {
                Setters =
                {
                    new Setter(OpacityProperty, 1d),
                    new Setter(IsHitTestVisibleProperty, true),
                },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>().Class(":vertical"))
            {
                Setters = { new Setter(WidthProperty, 8d) },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>().Class(":horizontal"))
            {
                Setters = { new Setter(HeightProperty, 8d) },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>().Template().OfType<Thumb>())
            {
                Setters =
                {
                    new Setter(
                        Thumb.TemplateProperty,
                        new FuncControlTemplate<Thumb>(
                            (_, _) =>
                                new Border
                                {
                                    Background = new SolidColorBrush(Color.Parse("#888888")),
                                    CornerRadius = new CornerRadius(2),
                                }
                        )
                    ),
                },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>().Class(":vertical").Template().OfType<Thumb>())
            {
                Setters = { new Setter(WidthProperty, 4d) },
            }
        );
        Styles.Add(
            new Style(s => s.OfType<ScrollBar>().Class(":horizontal").Template().OfType<Thumb>())
            {
                Setters = { new Setter(HeightProperty, 4d) },
            }
        );
    }
}
