using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void InitializeCaptionButtons()
    {
        SetTitleBar(TitleDragRegion);
        CaptionMinimize.Click += (_, _) => (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
        CaptionMaximize.Click += (_, _) =>
        {
            if (AppWindow.Presenter is not OverlappedPresenter window)
                return;
            if (window.State == OverlappedPresenterState.Maximized)
                window.Restore();
            else
                window.Maximize();
        };
        CaptionClose.Click += (_, _) => Close();
        AppWindow.Changed += (_, _) => RefreshCaptionButtons();
        RefreshCaptionButtons();
    }

    void SetCaptionMode(bool glass)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.SetBorderAndTitleBar(true, glass);
        var visibility = glass ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        CaptionMinimize.Visibility = visibility;
        CaptionMaximize.Visibility = visibility;
        CaptionClose.Visibility = visibility;
    }

    void RefreshCaptionButtons()
    {
        static void Label(Button button, string label)
        {
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
        }

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var maximized = presenter?.State == OverlappedPresenterState.Maximized;
        CaptionMinimize.IsEnabled = presenter?.IsMinimizable == true;
        CaptionMaximize.IsEnabled = maximized || presenter?.IsMaximizable == true;
        CaptionMaximizeIcon.Glyph = maximized ? "\uE923" : "\uE922";
        Label(CaptionMinimize, Ui("Minimize"));
        Label(CaptionMaximize, Ui(maximized ? "Restore window" : "Maximize"));
        Label(CaptionClose, Ui("Close"));
    }
}
