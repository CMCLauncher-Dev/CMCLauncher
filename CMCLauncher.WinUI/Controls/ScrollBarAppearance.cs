using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CMCLauncher.WinUI;

// Template-created bars register through the implicit style. Weak references keep recycled lists collectible.
public sealed class ScrollBarAppearance : DependencyObject
{
    static readonly List<WeakReference<ScrollBar>> Bars = [];
    static bool show;
    public static readonly DependencyProperty AttachProperty = DependencyProperty.RegisterAttached(
        "Attach",
        typeof(bool),
        typeof(ScrollBarAppearance),
        new PropertyMetadata(false, Attached)
    );

    public static bool GetAttach(DependencyObject target) => (bool)target.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject target, bool value) =>
        target.SetValue(AttachProperty, value);

    public static bool Show
    {
        get => show;
        set
        {
            show = value;
            for (var i = Bars.Count - 1; i >= 0; i--)
            {
                if (Bars[i].TryGetTarget(out var bar))
                    Apply(bar);
                else
                    Bars.RemoveAt(i);
            }
        }
    }

    static void Attached(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollBar bar || args.NewValue is not true)
            return;
        for (var i = Bars.Count - 1; i >= 0; i--)
        {
            if (!Bars[i].TryGetTarget(out var existing))
                Bars.RemoveAt(i);
            else if (ReferenceEquals(existing, bar))
            {
                Apply(bar);
                return;
            }
        }
        Bars.Add(new(bar));
        Apply(bar);
    }

    static void Apply(ScrollBar bar)
    {
        bar.Opacity = show ? 1 : 0;
        bar.IsHitTestVisible = show;
    }
}
