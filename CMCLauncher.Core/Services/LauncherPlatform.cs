namespace CMCLauncher;

// 平台宿主基类：由宿主程序（WinUI/Avalonia 等）提供操作系统集成能力。
// 默认实现不会以明文形式存储凭据。
public class LauncherPlatform : CMCLauncher.Platform.PlatformEnvironment
{
    static LauncherPlatform current = new();

    // 当前平台宿主实例（线程安全读写），由宿主程序在启动时注入。
    public static LauncherPlatform Current
    {
        get => Volatile.Read(ref current);
        set =>
            Interlocked.Exchange(
                ref current,
                value ?? throw new ArgumentNullException(nameof(value))
            );
    }

    // 把 GPU 偏好设置应用到指定可执行文件（默认空实现，由平台宿主覆盖）。
    public virtual Task ApplyGpuPreference(Store store, string executable) => Task.CompletedTask;

    // 恢复所有 GPU 偏好设置（默认空实现，由平台宿主覆盖）。
    public virtual Task RestoreGpuPreferences(Store store) => Task.CompletedTask;
}
