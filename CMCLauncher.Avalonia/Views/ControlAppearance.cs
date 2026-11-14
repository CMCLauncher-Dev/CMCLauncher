using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    readonly SolidColorBrush controlSurfaceBrush = new(Color.Parse("#F1F2F5"));

    void PrepareDialog(Window dialog)
    {
        dialog.RequestedThemeVariant = ActualThemeVariant;
        dialog.Background = surfaceBrush;
        dialog.Foreground = textBrush;
        dialog.FontFamily = FontFamily;
        dialog.FontSize = FontSize;
        dialog.Classes.Set(
            "custom-appearance",
            session.Store.Config.Settings.ColorPreset != "native"
        );
        ConfigureControlAppearance(dialog.Styles);
    }

    void ConfigureControlAppearance(Styles? target = null)
    {
        foreach (
            var selector in new Func<Selector?, Selector>[]
            {
                s => s.Class("custom-appearance").Descendant().OfType<TextBox>(),
                s => s.Class("custom-appearance").Descendant().OfType<ComboBox>(),
                s => s.Class("custom-appearance").Descendant().OfType<NumericUpDown>(),
            }
        )
            (target ?? Styles).Add(
                new Style(selector)
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.BackgroundProperty, controlSurfaceBrush),
                        new Setter(TemplatedControl.BorderBrushProperty, borderBrush),
                        new Setter(TemplatedControl.ForegroundProperty, textBrush),
                        new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                        new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                    },
                }
            );
    }
}
