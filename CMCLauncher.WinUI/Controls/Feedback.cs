using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal sealed record PopconfirmRequest(
        FrameworkElement Anchor,
        string Title,
        UIElement Body,
        string ConfirmText
    );

    Func<PopconfirmRequest, Task<bool>>? popconfirmOverride;
    Flyout? activePopconfirm;
    DispatcherQueueTimer? toastTimer;
    readonly TranslateTransform toastOffset = new();
    Storyboard? toastAnimation;

    void InitializeFeedback()
    {
        TaskToast.RenderTransform = toastOffset;
        toastTimer = DispatcherQueue.CreateTimer();
        toastTimer.Interval = TimeSpan.FromMilliseconds(3400);
        toastTimer.IsRepeating = false;
        toastTimer.Tick += (_, _) =>
        {
            if (!systemUi.AnimationsEnabled)
            {
                TaskToast.IsOpen = false;
                return;
            }

            AnimateToast(TaskToast, "Opacity", 1, 0, 650, new SineEase
            {
                EasingMode = EasingMode.EaseInOut,
            }, () => TaskToast.IsOpen = false);
        };
        TaskToast.Closed += (_, _) =>
        {
            toastTimer.Stop();
            StopToastAnimation();
        };
    }

    void StopToastAnimation()
    {
        var previous = toastAnimation;
        toastAnimation = null;
        previous?.Stop();
    }

    void AnimateToast(
        DependencyObject target,
        string property,
        double from,
        double to,
        int milliseconds,
        EasingFunctionBase easing,
        Action? finished = null
    )
    {
        StopToastAnimation();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = easing,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        toastAnimation = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (toastAnimation != storyboard)
                return;
            finished?.Invoke();
            StopToastAnimation();
        };
        storyboard.Begin();
    }

    internal void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        toastTimer?.Stop();
        StopToastAnimation();
        RefreshToastPalette();
        TaskToast.MaxWidth = Math.Max(
            0,
            Math.Min(440, ((FrameworkElement)Content).ActualWidth - 48)
        );
        TaskToast.Tag =
            severity == InfoBarSeverity.Success ? "\uE73E"
            : severity == InfoBarSeverity.Error ? "\uE711"
            : "\uE946";
        TaskToast.Severity = severity;
        TaskToast.Message = message;
        TaskToast.IsOpen = true;
        if (systemUi.AnimationsEnabled)
            AnimateToast(toastOffset, "Y", 28, 0, 240, new CubicEase
            {
                EasingMode = EasingMode.EaseOut,
            });
        toastTimer?.Start();
    }

    void RefreshToastPalette()
    {
        var color = PaletteColor(PrimaryColor);
        TaskToast.Background = new SolidColorBrush(color);
        TaskToast.BorderBrush = new SolidColorBrush(color);
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        TaskToast.Foreground = new SolidColorBrush(
            luminance > .179 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White
        );
    }

    internal Task<bool> ConfirmPop(
        FrameworkElement anchor,
        string title,
        UIElement body,
        string? confirmText = null
    )
    {
        var request = new PopconfirmRequest(anchor, title, body, confirmText ?? Ui("Delete"));
        if (popconfirmOverride != null)
            return popconfirmOverride(request);
        activePopconfirm?.Hide();
        var result = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var panel = new StackPanel
        {
            Spacing = 12,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
        };
        var heading = Text(title, 16, true);
        heading.Foreground = ComboTextBrush;
        var cancel = new Button
        {
            Content = Ui("Cancel"),
            Tag = "popconfirm-cancel",
            Background = ComboSurfaceBrush,
            Foreground = ComboTextBrush,
            BorderBrush = ComboBorderBrush,
        };
        var confirm = new Button
        {
            Content = request.ConfirmText,
            Tag = "popconfirm-confirm",
            Background = ComboSelectedBrush,
            Foreground = ComboTextBrush,
        };
        panel.Children.Add(heading);
        panel.Children.Add(body);
        panel.Children.Add(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, confirm },
            }
        );
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(
            new Setter(
                FrameworkElement.RequestedThemeProperty,
                lightTheme ? ElementTheme.Light : ElementTheme.Dark
            )
        );
        style.Setters.Add(new Setter(Control.BackgroundProperty, ComboSurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ComboTextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, ComboBorderBrush));
        // 自定义 Style 未设置 BasedOn 会整体替换默认样式，CornerRadius 会回退为 0（直角）。
        // 默认样式通过主题资源 OverlayCornerRadius 提供 8px 圆角，这里显式补回保持一致。
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(
            new Setter(
                FrameworkElement.MaxWidthProperty,
                Math.Max(120, Math.Min(420, ((FrameworkElement)Content).ActualWidth - 32))
            )
        );
        style.Setters.Add(
            new Setter(
                FrameworkElement.MaxHeightProperty,
                Math.Max(120, ((FrameworkElement)Content).ActualHeight - 48)
            )
        );
        var flyout = new Flyout
        {
            XamlRoot = anchor.XamlRoot,
            Content = panel,
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = style,
        };
        activePopconfirm = flyout;
        cancel.Click += (_, _) => flyout.Hide();
        confirm.Click += (_, _) =>
        {
            result.TrySetResult(true);
            flyout.Hide();
        };
        flyout.Closed += (_, _) =>
        {
            result.TrySetResult(false);
            if (ReferenceEquals(activePopconfirm, flyout))
                activePopconfirm = null;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (anchor is Control { IsLoaded: true, IsEnabled: true } target)
                    target.Focus(FocusState.Programmatic);
            });
        };
        flyout.Opened += (_, _) => cancel.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
        return result.Task;
    }

    internal Task<bool> ConfirmPop(
        FrameworkElement anchor,
        string title,
        string message,
        string? confirmText = null
    )
    {
        var text = Text(message, 13);
        text.Foreground = ComboTextBrush;
        // 垂直 StackPanel 的子元素测量时宽度无界，超长消息（如 Java 路径）
        // 会按单行撑开再被弹出层最大宽度裁切。用相同的上限约束文本，
        // 让它在测量阶段就换行，保证内容完整可见。
        text.MaxWidth = Math.Max(
            120,
            Math.Min(400, ((FrameworkElement)Content).ActualWidth - 64)
        );
        return ConfirmPop(anchor, title, text, confirmText);
    }

    void RecyclePath(string path)
    {
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
            );
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
            );
    }
}