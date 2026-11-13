using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

// Desktop shell dialogs work without the WinRT picker broker in unpackaged installations.
// InterfaceIsIUnknown already supplies IUnknown: declaring its slots again corrupts the vtable.
static partial class DesktopPicker
{
    const uint FOS_PICKFOLDERS = 0x00000020;
    const uint FOS_FORCEFILESYSTEM = 0x00000040;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    // IFileOpenDialog : IFileDialog : IModalWindow : IUnknown
    // Flatten the native inheritance chain, excluding the three implicit IUnknown slots.
    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig]
        int Show(IntPtr parent);

        // IFileDialog slots 4..26
        [PreserveSig]
        int SetFileTypes(uint cFileTypes, [In] IntPtr rgFilterSpec);

        [PreserveSig]
        int SetFileTypeIndex(uint iFileType);

        [PreserveSig]
        int GetFileTypeIndex(out uint piFileType);

        [PreserveSig]
        int Advise(IntPtr pfde, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOptions(uint fos);

        [PreserveSig]
        int GetOptions(out uint pfos);

        [PreserveSig]
        int SetDefaultFolder(IntPtr psi);

        [PreserveSig]
        int SetFolder(IntPtr psi);

        [PreserveSig]
        int GetFolder(out IntPtr ppv);

        [PreserveSig]
        int GetCurrentSelection(out IntPtr ppv);

        [PreserveSig]
        int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out IntPtr pszName);

        [PreserveSig]
        int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        [PreserveSig]
        int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        [PreserveSig]
        int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        [PreserveSig]
        int GetResult(out IntPtr ppv);

        [PreserveSig]
        int AddPlace(IntPtr psi, uint fdap);

        [PreserveSig]
        int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        [PreserveSig]
        int Close(uint hr);

        [PreserveSig]
        int SetClientGuid(ref Guid guid);

        [PreserveSig]
        int ClearClientData();

        [PreserveSig]
        int SetFilter(IntPtr pFilter);

        // IFileOpenDialog
        [PreserveSig]
        int GetResults(out IShellItemArray items);

        [PreserveSig]
        int GetSelectedItems(out IShellItemArray items);
    }

    // IFileOpenDialog : IFileDialog : IModalWindow : IUnknown
    // Flatten the native inheritance chain, excluding the three implicit IUnknown slots.
    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        // IModalWindow
        [PreserveSig]
        int Show(IntPtr parent);

        // IFileDialog slots 4..26
        [PreserveSig]
        int SetFileTypes(uint cFileTypes, [In] IntPtr rgFilterSpec);

        [PreserveSig]
        int SetFileTypeIndex(uint iFileType);

        [PreserveSig]
        int GetFileTypeIndex(out uint piFileType);

        [PreserveSig]
        int Advise(IntPtr pfde, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOptions(uint fos);

        [PreserveSig]
        int GetOptions(out uint pfos);

        [PreserveSig]
        int SetDefaultFolder(IntPtr psi);

        [PreserveSig]
        int SetFolder(IntPtr psi);

        [PreserveSig]
        int GetFolder(out IntPtr ppv);

        [PreserveSig]
        int GetCurrentSelection(out IntPtr ppv);

        [PreserveSig]
        int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out IntPtr pszName);

        [PreserveSig]
        int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        [PreserveSig]
        int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        [PreserveSig]
        int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        [PreserveSig]
        int GetResult(out IntPtr ppv);

        [PreserveSig]
        int AddPlace(IntPtr psi, uint fdap);

        [PreserveSig]
        int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        [PreserveSig]
        int Close(uint hr);

        [PreserveSig]
        int SetClientGuid(ref Guid guid);

        [PreserveSig]
        int ClearClientData();

        [PreserveSig]
        int SetFilter(IntPtr pFilter);
    }

    // IShellItem inherits IUnknown; only its own methods belong in this declaration.
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string? ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [
        ComImport,
        Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ]
    interface IShellItemArray
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetPropertyStore(uint flags, ref Guid iid, out IntPtr result);
        void GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr result);
        void GetAttributes(uint flags, uint mask, out uint attributes);
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem item);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Filter
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Pattern;
    }

    internal static void Initialize(DesktopFilePicker picker, IntPtr owner) => picker.Owner = owner;

    internal static string? PickFolder(Window window) =>
        Pick(WindowNative.GetWindowHandle(window), false, false, true, [], null).FirstOrDefault();

    internal static string[] Pick(
        IntPtr owner,
        bool save,
        bool multiple,
        bool folder,
        Filter[] filters,
        string? name,
        Action<Action>? beforeShow = null
    )
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || owner == IntPtr.Zero)
            throw new InvalidOperationException(
                "File dialogs require the launcher's STA UI thread and owner window."
            );
        var clsid = new Guid(
            save ? "C0B4E2F3-BA21-4773-8DBA-335EC946EB8B" : "DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"
        );
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)!)!;
        IntPtr filterMemory = IntPtr.Zero;
        int initializedFilters = 0;
        try
        {
            var dialog = (IFileDialog)instance;
            Marshal.ThrowExceptionForHR(dialog.GetOptions(out var options));
            options |=
                FOS_FORCEFILESYSTEM
                | 0x800 /* PATHMUSTEXIST */
                | 0x02000000 /* DONTADDTORECENT */
            ;
            options |=
                save ? 2u /* OVERWRITEPROMPT */
                : folder ? FOS_PICKFOLDERS
                : 0x1000u /* FILEMUSTEXIST */
            ;
            if (multiple)
                options |= 0x200;
            Marshal.ThrowExceptionForHR(dialog.SetOptions(options));
            if (filters.Length > 0)
            {
                var size = Marshal.SizeOf<Filter>();
                filterMemory = Marshal.AllocCoTaskMem(size * filters.Length);
                foreach (var filter in filters)
                {
                    Marshal.StructureToPtr(filter, filterMemory + size * initializedFilters, false);
                    initializedFilters++;
                }
                Marshal.ThrowExceptionForHR(
                    dialog.SetFileTypes((uint)filters.Length, filterMemory)
                );
                if (save)
                {
                    var extension = filters[0].Pattern.Split(';')[0].TrimStart('*', '.');
                    if (extension.Length > 0)
                        Marshal.ThrowExceptionForHR(dialog.SetDefaultExtension(extension));
                }
            }
            if (!string.IsNullOrEmpty(name))
                Marshal.ThrowExceptionForHR(dialog.SetFileName(name));
            beforeShow?.Invoke(() =>
                Marshal.ThrowExceptionForHR(dialog.Close(unchecked((uint)0x800704C7)))
            );
            var result = dialog.Show(owner);
            if (result == unchecked((int)0x800704C7))
                return []; // User cancelled, not a picker failure.
            Marshal.ThrowExceptionForHR(result);
            if (multiple)
            {
                Marshal.ThrowExceptionForHR(((IFileOpenDialog)instance).GetResults(out var items));
                try
                {
                    items.GetCount(out var count);
                    var paths = new List<string>();
                    for (uint i = 0; i < count; i++)
                    {
                        items.GetItemAt(i, out var item);
                        try
                        {
                            paths.Add(GetPath(item));
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                    return paths.ToArray();
                }
                finally
                {
                    Marshal.ReleaseComObject(items);
                }
            }
            Marshal.ThrowExceptionForHR(dialog.GetResult(out var pointer));
            try
            {
                var item = (IShellItem)Marshal.GetObjectForIUnknown(pointer);
                try
                {
                    return [GetPath(item)];
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        finally
        {
            for (var i = 0; i < initializedFilters; i++)
                Marshal.DestroyStructure<Filter>(filterMemory + i * Marshal.SizeOf<Filter>());
            if (filterMemory != IntPtr.Zero)
                Marshal.FreeCoTaskMem(filterMemory);
            Marshal.ReleaseComObject(instance);
        }
    }

    static string GetPath(IShellItem item)
    {
        Marshal.ThrowExceptionForHR(item.GetDisplayName(SIGDN_FILESYSPATH, out var path));
        return path ?? throw new IOException("The selected item has no file system path.");
    }
}

// Keep the existing StorageFile-based consumers, but use desktop dialogs without the WinRT picker broker.
abstract class DesktopFilePicker
{
    internal IntPtr Owner { get; set; }
}

sealed class DesktopFileOpenPicker : DesktopFilePicker
{
    public List<string> FileTypeFilter { get; } = [];

    public async Task<Windows.Storage.StorageFile?> PickSingleFileAsync() =>
        (await Pick(false)).FirstOrDefault();

    public Task<IReadOnlyList<Windows.Storage.StorageFile>> PickMultipleFilesAsync() => Pick(true);

    async Task<IReadOnlyList<Windows.Storage.StorageFile>> Pick(bool multiple)
    {
        var filters =
            FileTypeFilter.Count == 0
                ? Array.Empty<DesktopPicker.Filter>()
                : new[]
                {
                    new DesktopPicker.Filter
                    {
                        Name = string.Join(", ", FileTypeFilter),
                        Pattern = string.Join(
                            ";",
                            FileTypeFilter.Select(e => e == "*" ? "*.*" : "*" + e)
                        ),
                    },
                };
        var paths = DesktopPicker.Pick(Owner, false, multiple, false, filters, null);
        var files = new List<Windows.Storage.StorageFile>();
        foreach (var path in paths)
            files.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        return files;
    }
}

sealed class DesktopFileSavePicker : DesktopFilePicker
{
    public string? SuggestedFileName { get; set; }
    public Dictionary<string, IList<string>> FileTypeChoices { get; } = [];

    public async Task<Windows.Storage.StorageFile?> PickSaveFileAsync()
    {
        var filters = FileTypeChoices
            .Select(pair => new DesktopPicker.Filter
            {
                Name = pair.Key,
                Pattern = string.Join(";", pair.Value.Select(e => "*" + e)),
            })
            .ToArray();
        var path = DesktopPicker
            .Pick(Owner, true, false, false, filters, SuggestedFileName)
            .FirstOrDefault();
        if (path == null)
            return null;
        // Match FileSavePicker: obtain a StorageFile, creating an empty file only after confirmation.
        using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)) { }
        return await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
    }
}

static class FolderPickerNative
{
    internal static string? PickFolder(Window window) => DesktopPicker.PickFolder(window);
}
