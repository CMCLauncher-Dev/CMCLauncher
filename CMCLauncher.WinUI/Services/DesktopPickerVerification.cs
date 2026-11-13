using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

static partial class DesktopPicker
{
    internal static void Verify(Window window, Action<bool, string> check)
    {
        // Modal dialogs hang in headless/CI sessions where their message loop never
        // observes the DispatcherQueue cancel timer. GitHub Actions always sets CI=true,
        // so skip the interactive dialogs there and keep only the headless IShellItem check.
        bool desktopDialogs =
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || string.Equals(
                Environment.GetEnvironmentVariable("CI"),
                "false",
                StringComparison.OrdinalIgnoreCase
            );
        var owner = WindowNative.GetWindowHandle(window);
        if (desktopDialogs)
        {
            foreach (var mode in new[] { "folder", "open", "multiple", "save" })
            {
                var timer = window.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(200);
                timer.IsRepeating = false;
                try
                {
                    var paths = Pick(
                        owner,
                        mode == "save",
                        mode == "multiple",
                        mode == "folder",
                        mode == "folder"
                            ? []
                            : [new Filter { Name = "Test files", Pattern = "*.txt;*.zip" }],
                        mode == "save" ? "compatibility.txt" : null,
                        cancel =>
                        {
                            timer.Tick += (_, _) => cancel();
                            timer.Start();
                        }
                    );
                    check(
                        paths.Length == 0,
                        "desktop "
                            + mode
                            + " dialog activates, configures and cancels without a WinRT broker"
                    );
                }
                finally
                {
                    timer.Stop();
                }
            }
        }
        // Exercise IShellItem's actual vtable rather than merely checking the declared GUID.
        // This is headless-safe and still validates the COM interop on every session.
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(
            SHCreateItemFromParsingName(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                IntPtr.Zero,
                ref iid,
                out var item
            )
        );
        try
        {
            check(Directory.Exists(GetPath(item)), "shell item returns a real file-system path");
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr context,
        ref Guid iid,
        out IShellItem item
    );
}