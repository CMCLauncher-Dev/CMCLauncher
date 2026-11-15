using System.ComponentModel;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    const uint FrameNcCalcSize = 0x0083;
    const uint FrameNcHitTest = 0x0084;
    const uint FrameNcDestroy = 0x0082;
    const nuint FullClientSubclassId = 0x434D434C;
    FrameSubclassProc? fullClientCallback;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate nint FrameSubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint id,
        nuint data
    );

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowSubclass(
        nint hwnd,
        FrameSubclassProc callback,
        nuint id,
        nuint data
    );

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RemoveWindowSubclass(nint hwnd, FrameSubclassProc callback, nuint id);

    [DllImport("comctl32.dll")]
    static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    struct FrameRect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FramePoint
    {
        public int X,
            Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    static extern bool FrameGetWindowRect(nint hwnd, out FrameRect rect);

    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    static extern bool FrameGetClientRect(nint hwnd, out FrameRect rect);

    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    static extern bool FrameClientToScreen(nint hwnd, ref FramePoint point);

    // Extending the DWM glass frame does not expand the HWND client rectangle. With a
    // transparent backdrop the remaining resize border is visible as a see-through strip.
    // Fill the whole normal window while retaining the existing backdrop and its opacity.
    void InitializeFullClientFrame()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        fullClientCallback = FullClientWindowProc;
        if (!SetWindowSubclass(hwnd, fullClientCallback, FullClientSubclassId, 0))
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Cannot initialize the window client frame."
            );
        Closed += (_, _) =>
        {
            if (fullClientCallback != null)
                RemoveWindowSubclass(hwnd, fullClientCallback, FullClientSubclassId);
        };
        SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | 0x0010
        );
    }

    nint FullClientWindowProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint id,
        nuint data
    )
    {
        // Always keep the managed delegate alive for the complete native callback lifetime.
        if (message == FrameNcDestroy)
        {
            if (fullClientCallback != null)
                RemoveWindowSubclass(hwnd, fullClientCallback, id);
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }
        if (message == FrameNcCalcSize && wParam != 0 && lParam != 0 && !IsZoomed(hwnd))
        {
            // NCCALCSIZE_PARAMS starts with the proposed window RECT. Let WinUI process
            // the message first, then retain that complete rectangle as the client area.
            var proposed = Marshal.PtrToStructure<FrameRect>(lParam);
            DefSubclassProc(hwnd, message, wParam, lParam);
            Marshal.StructureToPtr(proposed, lParam, false);
            return 0;
        }
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        if (
            message == FrameNcHitTest
            && (result == 1 || result == 2)
            && !IsZoomed(hwnd)
            && (GetWindowLong(hwnd, -16) & 0x00040000) != 0
            && FrameGetWindowRect(hwnd, out var rect)
        )
        {
            var dpi = GetDpiForWindow(hwnd);
            var horizontal = GetSystemMetricsForDpi(32, dpi) + GetSystemMetricsForDpi(92, dpi);
            var vertical = GetSystemMetricsForDpi(33, dpi) + GetSystemMetricsForDpi(92, dpi);
            var position = lParam.ToInt64();
            var hit = FrameResizeHit(
                rect,
                unchecked((short)position),
                unchecked((short)(position >> 16)),
                horizontal,
                vertical
            );
            // Preserve native caption buttons and other non-client results from WinUI.
            if (hit != 0)
                return hit;
        }
        return result;
    }

    static int FrameResizeHit(FrameRect rect, int x, int y, int horizontal, int vertical)
    {
        if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom)
            return 0;
        var left = x < rect.Left + horizontal;
        var right = x >= rect.Right - horizontal;
        var top = y < rect.Top + vertical;
        var bottom = y >= rect.Bottom - vertical;
        return top
            ? left
                ? 13
                : right
                    ? 14
                    : 12
            : bottom
                ? left
                    ? 16
                    : right
                        ? 17
                        : 15
                : left
                    ? 10
                    : right
                        ? 11
                        : 0;
    }
}
