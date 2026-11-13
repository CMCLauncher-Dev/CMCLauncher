using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

// Win32 经典文件夹选择对话框。unpackaged/自包含 WinUI3 中 FolderPicker 常抛
// E_FAIL(0x80004005)，此实现走 IFileOpenDialog + FOS_PICKFOLDERS，稳定可靠。
// 接口方法顺序即 COM vtable 槽位顺序，由 CLR/InterfaceIsIUnknown 自动绑定。
static class FolderPickerNative
{
    const uint FOS_PICKFOLDERS = 0x00000020;
    const uint FOS_FORCEFILESYSTEM = 0x00000040;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    // IFileOpenDialog : IFileDialog : IModalWindow : IUnknown
    // 完整声明继承链，保证 GetResult/Show 等槽位由运行时计算而不错位。
    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        // IUnknown
        void _0(); void _1(); void _2();
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog slots 4..26
        [PreserveSig] int SetFileTypes(uint cFileTypes, [In] IntPtr rgFilterSpec);
        [PreserveSig] int SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(uint fos);
        [PreserveSig] int GetOptions(out uint pfos);
        [PreserveSig] int SetDefaultFolder(IntPtr psi);
        [PreserveSig] int SetFolder(IntPtr psi);
        [PreserveSig] int GetFolder(IntPtr riid, out IntPtr ppv);
        [PreserveSig] int GetCurrentSelection(IntPtr riid, out IntPtr ppv);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out IntPtr pszName);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] int GetResult(out IntPtr ppv);
        [PreserveSig] int AddPlace(IntPtr psi, uint fdap);
        [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] int Close(uint hr);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(IntPtr pFilter);
        // IFileOpenDialog
        [PreserveSig] int GetResults(IntPtr riid, out IntPtr ppv);
        [PreserveSig] int GetSelectedItems(IntPtr riid, out IntPtr ppv);
    }

    // IShellItem : IUnknown — GetDisplayName 位于槽位 5（_0.._2 IUnknown，_3 BindToHandler，_4 GetParent，_5 GetDisplayName）。
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void _0(); void _1(); void _2();
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string? ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("ole32.dll", PreserveSig = true)]
    static extern int CoCreateInstance(
        [In] ref Guid clsid,
        IntPtr outer,
        uint context,
        [In] ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog dialog
    );

    internal static string? PickFolder(Window window)
    {
        var owner = WindowNative.GetWindowHandle(window);
        var clsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        var iid = typeof(IFileOpenDialog).GUID;
        _ = CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            1 /* CLSCTX_INPROC_SERVER */,
            ref iid,
            out var dialog
        );
        try
        {
            return ShowAndGet(dialog, owner);
        }
        finally
        {
            if (dialog != null)
                Marshal.ReleaseComObject(dialog);
        }
    }

    static string? ShowAndGet(IFileOpenDialog dialog, IntPtr owner)
    {
        if (dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM) != 0)
            return null;
        if (dialog.Show(owner) != 0)
            return null;
        IntPtr itemPtr;
        if (dialog.GetResult(out itemPtr) != 0 || itemPtr == IntPtr.Zero)
            return null;
        try
        {
            var shellItem = (IShellItem)Marshal.GetObjectForIUnknown(itemPtr);
            string? name;
            if (shellItem.GetDisplayName(SIGDN_FILESYSPATH, out name) != 0)
                return null;
            return name;
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }
}