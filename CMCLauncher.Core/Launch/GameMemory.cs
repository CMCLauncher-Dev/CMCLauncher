namespace CMCLauncher;

// 为 JVM 堆计算预算，读取系统内存失败时使用固定值。
public static class GameMemory
{
    public static int Calculate(long totalMb, long availableMb)
    {
        // Leave room for Windows, other apps and the JVM's non-heap allocations.
        var budget = Math.Min(totalMb / 2, Math.Max(512, availableMb - 1536));
        // 向下对齐到 256 MB，自动分配最多使用 16 GB。
        return (int)(Math.Clamp(budget, 512, 16384) / 256 * 256);
    }

    public static int Resolve(Settings settings)
    {
        if (!settings.AutoMemory)
            return Math.Clamp(settings.MemoryMb, 512, 65536);
        var memory = LauncherPlatform.Current.PhysicalMemory();
        return memory is { } value ? Calculate(value.TotalMb, value.AvailableMb) : 2048;
    }
}
