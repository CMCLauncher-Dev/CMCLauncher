using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

static partial class DesktopPicker
{
    internal static async Task Verify(Window window, Action<bool, string> check, string directory)
    {
        check(
            !CanVerifyDialogs(true, "true", null)
                && !CanVerifyDialogs(true, null, "true")
                && !CanVerifyDialogs(false, null, null)
                && CanVerifyDialogs(true, null, null),
            "CI and noninteractive sessions exclude all modal picker checks"
        );
        if (
            CanVerifyDialogs(
                Environment.UserInteractive,
                Environment.GetEnvironmentVariable("CI"),
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS")
            )
        )
            await VerifyDialogs(window, check, directory);
        else
            check(
                true,
                "SKIP modal picker checks in CI or noninteractive sessions; shell interop remains verified"
            );
        // Exercise IShellItem's actual vtable rather than merely checking the declared GUID.
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

    static bool CanVerifyDialogs(bool interactive, string? ci, string? githubActions) =>
        interactive && UnsetOrFalse(ci) && UnsetOrFalse(githubActions);

    static bool UnsetOrFalse(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("false", StringComparison.OrdinalIgnoreCase)
        || value == "0";

    static async Task VerifyDialogs(Window window, Action<bool, string> check, string directory)
    {
        var owner = WindowNative.GetWindowHandle(window);
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
                    (_, cancel) =>
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
        var fixture = Path.Combine(directory, "picker 中文 files");
        Directory.CreateDirectory(fixture);
        var first = Path.Combine(fixture, "first 中文.txt");
        var second = Path.Combine(fixture, "second file.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        foreach (var mode in new[] { "open", "multiple", "save", "folder" })
        {
            var name = mode switch
            {
                "multiple" => "\"" + first + "\" \"" + second + "\"",
                "save" => Path.Combine(fixture, "new 中文.txt"),
                "folder" => fixture,
                _ => first,
            };
            var timer = window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(400);
            timer.IsRepeating = true;
            var attempts = 0;
            try
            {
                var selected = Pick(
                    owner,
                    mode == "save",
                    mode == "multiple",
                    mode == "folder",
                    mode == "folder" ? [] : [new Filter { Name = "Text files", Pattern = "*.txt" }],
                    mode == "folder" ? null : name,
                    (dialog, cancel) =>
                    {
                        if (mode == "folder")
                        {
                            var folderIid = typeof(IShellItem).GUID;
                            Marshal.ThrowExceptionForHR(
                                SHCreateItemFromParsingName(
                                    fixture,
                                    IntPtr.Zero,
                                    ref folderIid,
                                    out var folderItem
                                )
                            );
                            try
                            {
                                var pointer = Marshal.GetComInterfaceForObject(
                                    folderItem,
                                    typeof(IShellItem)
                                );
                                try
                                {
                                    Marshal.ThrowExceptionForHR(
                                        ((IFileDialog)dialog).SetFolder(pointer)
                                    );
                                }
                                finally
                                {
                                    Marshal.Release(pointer);
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(folderItem);
                            }
                        }
                        timer.Tick += (_, _) =>
                        {
                            if (++attempts >= 15)
                            {
                                timer.Stop();
                                cancel();
                                return;
                            }
                            Marshal.ThrowExceptionForHR(
                                ((IOleWindow)dialog).GetWindow(out var handle)
                            );
                            PostMessage(handle, 0x0111, new IntPtr(1), IntPtr.Zero); // IDOK in our test dialog only.
                        };
                        timer.Start();
                    }
                );
                var expected = mode == "multiple" ? new[] { first, second } : new[] { name };
                check(
                    selected
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(
                            expected.Order(StringComparer.OrdinalIgnoreCase),
                            StringComparer.OrdinalIgnoreCase
                        ),
                    "desktop " + mode + " returns selected Unicode paths with spaces"
                );
                if (mode == "open")
                    check(
                        (await Windows.Storage.StorageFile.GetFileFromPathAsync(selected[0])).Path
                            == first,
                        "selected desktop path is compatible with existing StorageFile consumers"
                    );
            }
            finally
            {
                timer.Stop();
            }
        }
    }

    [
        ComImport,
        Guid("00000114-0000-0000-C000-000000000046"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ]
    interface IOleWindow
    {
        [PreserveSig]
        int GetWindow(out IntPtr handle);

        [PreserveSig]
        int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr context,
        ref Guid iid,
        out IShellItem item
    );
}
