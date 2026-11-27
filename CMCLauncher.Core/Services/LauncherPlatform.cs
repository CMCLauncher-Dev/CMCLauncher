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

    public virtual Task<bool> OpenDirectory(string path) => Task.FromResult(false);

    public virtual Task<Action?> PreventIdleSleep(CancellationToken ct) => Task.FromResult<Action?>(null);

    public virtual Task<TransferHotspot> StartTransferHotspot(CancellationToken ct) =>
        throw new PlatformNotSupportedException("Automatic hotspot creation is unavailable on this system.");

    public virtual bool SupportsMemoryOptimization => false;

    public virtual MemoryOptimizationResult OptimizeMemory(CancellationToken ct) => default;

    public virtual MemoryOptimizationResult? OptimizeMemoryBeforeLaunch(CancellationToken ct) => OptimizeMemory(ct);

    // 把 GPU 偏好设置应用到指定可执行文件（默认空实现，由平台宿主覆盖）。
    public virtual Task ApplyGpuPreference(Store store, string executable) => Task.CompletedTask;

    // 恢复所有 GPU 偏好设置（默认空实现，由平台宿主覆盖）。
    public virtual Task RestoreGpuPreferences(Store store) => Task.CompletedTask;
}

public readonly record struct MemoryOptimizationResult(
    int SystemOperationsCompleted,
    long? AvailableBeforeMb,
    long? AvailableAfterMb,
    string? CompletedOperations = null
)
{
    public long? AvailableGainMb => AvailableBeforeMb is long before && AvailableAfterMb is long after
        ? Math.Max(0, after - before)
        : null;
}

public sealed class MemoryOptimizationDeclinedException : OperationCanceledException
{
    public MemoryOptimizationDeclinedException() : base("Administrator permission was declined.") { }
}
