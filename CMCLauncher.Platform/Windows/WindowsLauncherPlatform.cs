using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CMCLauncher;

// Windows hosts register this adapter before reading settings or credentials.
[SupportedOSPlatform("windows")]
public sealed partial class WindowsLauncherPlatform : LauncherPlatform
{
    public static void Initialize() => Current = new WindowsLauncherPlatform();

    [StructLayout(LayoutKind.Sequential)]
    struct Blob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptProtectData(
        ref Blob input,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(
        ref Blob input,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    // 输入由本程序分配，输出由 DPAPI 分配，分别用 FreeHGlobal 和 LocalFree 释放。
    public override byte[] TransformCredential(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = protect
                ? CryptProtectData(
                    ref input,
                    "CMCLauncher",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                )
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                );
            if (!success)
                throw new Exception("Windows 凭据加密失败，请重新登录");
            var bytes = new byte[output.Length];
            Marshal.Copy(output.Data, bytes, 0, output.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length,
            Load;
        public ulong TotalPhysical,
            AvailablePhysical,
            TotalPageFile,
            AvailablePageFile,
            TotalVirtual,
            AvailableVirtual,
            AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public override (long TotalMb, long AvailableMb)? PhysicalMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status)
            ? ((long)(status.TotalPhysical / 1048576), (long)(status.AvailablePhysical / 1048576))
            : null;
    }

    [DllImport("kernel32.dll")]
    static extern int GetUserGeoID(int geoClass);

    public override int GeoId => GetUserGeoID(16);

    public override IEnumerable<string> JavaHomes()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            foreach (
                var name in new[]
                {
                    @"SOFTWARE\JavaSoft\Java Runtime Environment",
                    @"SOFTWARE\JavaSoft\Java Development Kit",
                    @"SOFTWARE\JavaSoft\JDK",
                    @"SOFTWARE\JavaSoft\JRE",
                }
            )
            {
                using var key = root.OpenSubKey(name);
                if (key == null)
                    continue;
                foreach (var version in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(version);
                    if (sub?.GetValue("JavaHome") is string path)
                        yield return path;
                }
            }
        }
    }

    const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public override async Task ApplyGpuPreference(Store store, string executable)
    {
        if (!OperatingSystem.IsWindows() || !store.Config.Settings.PreferHighPerformanceGpu)
            return;
        var path = Path.GetFullPath(executable);
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        var previous = key.GetValue(path) as string;
        if (!store.Config.Settings.ManagedGpuPreferences.ContainsKey(path))
        {
            store.Config.Settings.ManagedGpuPreferences[path] = previous;
            // Record recovery data before changing the user's Windows preference.
            await store.Save();
        }
        key.SetValue(
            path,
            GameGpuPreference.WithPreference(previous ?? "", "2"),
            RegistryValueKind.String
        );
        key.Flush();
        var applied = key.GetValue(path) as string;
        if (
            applied == null
            || !applied
                .Split(';')
                .Any(p => p.Equals("GpuPreference=2", StringComparison.OrdinalIgnoreCase))
        )
            throw new IOException("Windows did not retain the high-performance GPU preference.");
    }

    public override async Task RestoreGpuPreferences(Store store)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        foreach (var pair in store.Config.Settings.ManagedGpuPreferences.ToArray())
        {
            var current = key.GetValue(pair.Key) as string;
            var preference = current
                ?.Split(';')
                .FirstOrDefault(p =>
                    p.TrimStart().StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)
                )
                ?.Trim();
            if (preference == "GpuPreference=2")
            {
                var original = pair
                    .Value?.Split(';')
                    .FirstOrDefault(p =>
                        p.TrimStart()
                            .StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)
                    )
                    ?.Split('=', 2)[1];
                var restored = GameGpuPreference.WithPreference(current!, original);
                if (restored.Length == 0 && pair.Value == null)
                    key.DeleteValue(pair.Key, false);
                else
                    key.SetValue(pair.Key, restored, RegistryValueKind.String);
            }
            store.Config.Settings.ManagedGpuPreferences.Remove(pair.Key);
        }
        await store.Save();
    }

    public override Task MoveToTrash(string path) =>
        Task.Run(() =>
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
        });
}
