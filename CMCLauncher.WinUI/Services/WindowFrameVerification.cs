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
            foreach (var glass in new[] { false, true, false })
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
                        && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0,
                    "pure transparent compositor pipeline is retained with glass option " + glass + " (" + SystemBackdrop?.GetType().Name + ", style=" + GetWindowLong(hwnd, GWL_EXSTYLE).ToString("X") + ")"
                );
                if (glass)
                    check(
                        WindowBackground.Color.A == 0x33,
                        "glass content retains commit-defined 20 percent opacity"
                    );
            }
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
                await Task.Delay(120);
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
