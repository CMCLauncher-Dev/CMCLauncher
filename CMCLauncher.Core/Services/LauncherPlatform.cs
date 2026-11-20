namespace CMCLauncher;

// Hosts supply OS integration. The default never stores credentials as plaintext.
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
