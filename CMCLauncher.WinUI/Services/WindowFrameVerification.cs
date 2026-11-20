using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Hosting;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern nint FrameSendMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    async Task CaptureFrameGeometry(string directory)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        FrameGetWindowRect(hwnd, out var window);
        FrameGetClientRect(hwnd, out var client);
        var origin = new FramePoint();
        FrameClientToScreen(hwnd, ref origin);
        var visual = ElementCompositionPreview.GetElementVisual(ModpackDropRoot);
        var scale = ModpackDropRoot.XamlRoot?.RasterizationScale ?? 1;
        var data = new
        {
            WindowWidth = window.Right - window.Left,
            WindowHeight = window.Bottom - window.Top,
            ClientWidth = client.Right - client.Left,
            ClientHeight = client.Bottom - client.Top,
            ClientLeftInset = origin.X - window.Left,
            ClientTopInset = origin.Y - window.Top,
            RootWidth = ModpackDropRoot.ActualWidth,
            RootHeight = ModpackDropRoot.ActualHeight,
            Scale = scale,
            VisualWidth = visual.Size.X,
            VisualHeight = visual.Size.Y,
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, "window-frame.json"),
            JsonSerializer.Serialize(data)
        );
    }

    async Task VerifyWindowClientFrame(Action<bool, string> check, string directory)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        FrameGetWindowRect(hwnd, out var window);
        FrameGetClientRect(hwnd, out var client);
        var origin = new FramePoint();
        FrameClientToScreen(hwnd, ref origin);
        check(
            client.Right == window.Right - window.Left
                && client.Bottom == window.Bottom - window.Top
                && origin.X == window.Left
                && origin.Y == window.Top,
            "normal window client area covers the complete native window without transparent resize strips"
        );
        var centerX = window.Left + (window.Right - window.Left) / 2;
        var centerY = window.Top + (window.Bottom - window.Top) / 2;
        foreach (
            var (x, y, expected) in new[]
            {
                (window.Left + 1, centerY, 10),
                (window.Right - 2, centerY, 11),
                (centerX, window.Top + 1, 12),
                (centerX, window.Bottom - 2, 15),
            }
        )
        {
            var point = (nint)((long)(ushort)x | ((long)(ushort)y << 16));
            check(
                FrameSendMessage(hwnd, FrameNcHitTest, 0, point) == expected,
                "native window resize hit test " + expected
            );
        }
        foreach (
            var (x, y, expected) in new[]
            {
                (window.Left, window.Top, 13),
                (window.Right - 1, window.Top, 14),
                (window.Left, window.Bottom - 1, 16),
                (window.Right - 1, window.Bottom - 1, 17),
            }
        )
            check(
                FrameResizeHit(window, x, y, 11, 11) == expected,
                "corner resize classification " + expected
            );
        var settings = store.Config.Settings;
        var savedGlass = settings.MicaEffect;
        try
        {
            object? stableBackdrop = null;
            foreach (var glass in new[] { false, true, false, true, false })
            {
                settings.MicaEffect = glass;
                var verifying = Verifying;
                try
                {
                    Verifying = false;
                    ApplyBackdrop(glass);
                    ApplyTheme();
                }
                finally
                {
                    Verifying = verifying;
                }
                check(
                    SystemBackdrop is TransparentBackdrop
                        && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0
                        && (stableBackdrop == null || ReferenceEquals(stableBackdrop, SystemBackdrop))
                        && glassClientFrame == glass,
                    "switching glass preserves the window's compositor: " + glass
                );
                stableBackdrop = SystemBackdrop;
                if (glass)
                    check(
                        WindowBackground.Color.A == 0x33
                            && CaptionClose.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                        "glass retains its translucent surface and custom caption buttons"
                    );
                CustomTitleBar.UpdateLayout();
                var closeEdge = CaptionClose.TransformToVisual(CustomTitleBar).TransformPoint(
                    new Windows.Foundation.Point(CaptionClose.ActualWidth, 0)
                );
                check(
                    CaptionClose.ActualWidth > 0
                        && CaptionClose.ActualHeight == 32
                        && CaptionMinimize.ActualHeight == CaptionClose.ActualHeight
                        && CaptionMaximize.ActualHeight == CaptionClose.ActualHeight
                        && Math.Abs(closeEdge.Y) < 0.5
                        && Math.Abs(closeEdge.X - CustomTitleBar.ActualWidth) < 0.5,
                    "32-DIP caption buttons align with the top and right edges with glass option " + glass
                );
                check(
                    FrameSendMessage(hwnd, FrameNcHitTest, 0, (nint)(
                        (long)(ushort)(window.Right - 2) | ((long)(ushort)(window.Top + 16) << 16)
                    )) == 1,
                    "the close button receives pointer input at the window's right edge"
                );
            }
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                var normalWidth = settings.WindowWidth;
                var normalHeight = settings.WindowHeight;
                presenter.Maximize();
                await Task.Delay(120);
                await store.Save();
                check(new Store(store.Root).Config.Settings.StartMaximized, "maximizing persists startup mode to disk");
                check(settings.WindowWidth == normalWidth && settings.WindowHeight == normalHeight, "maximized geometry does not replace the last normal dimensions");
                presenter.Minimize(); await Task.Delay(80);
                CaptureWindowPlacement(); await store.Save();
                check(new Store(store.Root).Config.Settings.StartMaximized, "minimizing preserves the last maximized mode");
                presenter.Maximize(); await Task.Delay(80);
                var reopened = new MainWindow(new[] { "--verify-datapack-ui", directory });
                var reopenedClosed = new TaskCompletionSource();
                reopened.Closed += (_, _) => reopenedClosed.TrySetResult();
                reopened.Activate(); await Task.Delay(100);
                check(reopened.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized },
                    "a fresh window reloads and restores the saved maximized state");
                check(reopened.store.Config.Settings.WindowWidth == normalWidth && reopened.store.Config.Settings.WindowHeight == normalHeight,
                    "maximizing does not overwrite normal restore dimensions");
                reopened.Close(); await reopenedClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                FrameGetClientRect(hwnd, out var maximized);
                var work = Microsoft
                    .UI.Windowing.DisplayArea.GetFromWindowId(
                        AppWindow.Id,
                        Microsoft.UI.Windowing.DisplayAreaFallback.Nearest
                    )
                    .WorkArea;
                check(
                    maximized.Right <= work.Width && maximized.Bottom <= work.Height,
                    "maximized client area respects monitor work area"
                );
                check(
                    ElementCompositionPreview.GetElementVisual(ModpackDropRoot).Clip == null,
                    "maximized window has no rounded corner cutouts"
                );
                presenter.Restore();
                AppWindow.MoveAndResize(
                    new Windows.Graphics.RectInt32(
                        window.Left,
                        window.Top,
                        window.Right - window.Left,
                        window.Bottom - window.Top
                    )
                );
                await Task.Delay(120);
                await store.Save();
                check(!new Store(store.Root).Config.Settings.StartMaximized, "restoring to normal persists windowed startup mode");
                settings.StartMaximized = true;
                CaptureWindowPlacement(); await store.Save();
                check(new Store(store.Root).Config.Settings.StartMaximized, "an explicit startup preference survives capture without a window state transition");
                settings.StartMaximized = false; await store.Save();
                FrameGetWindowRect(hwnd, out var restored);
                FrameGetClientRect(hwnd, out var restoredClient);
                check(
                    restoredClient.Right == restored.Right - restored.Left
                        && restoredClient.Bottom == restored.Bottom - restored.Top,
                    "restoring the window keeps full client coverage"
                );
            }
        }
        finally
        {
            settings.MicaEffect = savedGlass;
            var verifying = Verifying;
            try
            {
                Verifying = false;
                ApplyBackdrop(savedGlass);
                ApplyTheme();
            }
            finally
            {
                Verifying = verifying;
            }
        }
        await CaptureFrameGeometry(directory);
    }
}
