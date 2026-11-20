namespace CMCLauncher;

// 游戏内存分配：根据系统物理内存自动计算 Java 堆大小，
// 并提供内存占用分布（已用/已分配/空闲）的计算，用于设置页与状态展示。
public static class GameMemory
{
    // 自动分配算法，参考 HMCLGameRepository.getAutoAllocatedMemory (HMCL, GPL-3.0)。
    // https://github.com/HMCL-dev/HMCL/blob/a21bd735e232262bb8842029fc4b5c575810024f/HMCL/src/main/java/org/jackhuang/hmcl/game/HMCLGameRepository.java#L501
    // 返回建议的堆大小（MB），并限制在 32/64 位各自的上下限内。
    public static int Calculate(long totalMb, long availableMb, bool is64Bit = true)
    {
        var minimum = is64Bit ? 512 : 256;
        var maximum = is64Bit ? 24576 : 768;
        if (totalMb <= minimum || availableMb < 0)
            return is64Bit ? 2048 : 512;
        var available = Math.Clamp(availableMb, 0, totalMb);
        var usable = Math.Max(0, Math.Max(available, totalMb / 16) - 256);
        // 超出 8GB 的内存按递减比例计入堆预算，为原生（堆外）分配预留空间。
        var budget =
            Math.Min(usable, 8192) * .8
            + Math.Clamp(usable - 8192, 0, 8192) * .4
            + Math.Max(0, usable - 16384) * .2;
        return (int)Math.Clamp(Math.Floor(budget), minimum, maximum);
    }

    // 计算内存分布比例（已用/已分配/空闲），三者共用 totalMb 作为分母；
    // 即使分配量超出可用内存也不做归一化，避免产生误导性的比例。
    public static (double Used, double Allocated, double Free) Distribution(
        long totalMb,
        long availableMb,
        int allocationMb
    )
    {
        if (totalMb <= 0)
            return (0, 0, 1);
        var available = Math.Clamp(availableMb, 0, totalMb);
        var allocated = Math.Min(Math.Max(0L, allocationMb), available);
        return (
            (totalMb - available) / (double)totalMb,
            allocated / (double)totalMb,
            (available - allocated) / (double)totalMb
        );
    }

    // 根据设置解析最终堆大小（MB）：
    // 手动模式直接取用户设置值（限制在 512~65536）；
    // 自动模式查询系统物理内存后按 Calculate 计算，查询失败时回退为 2048。
    public static int Resolve(Settings settings)
    {
        if (!settings.AutoMemory)
            return Math.Clamp(settings.MemoryMb, 512, 65536);
        var memory = LauncherPlatform.Current.PhysicalMemory();
        return memory is { } value
            ? Calculate(value.TotalMb, value.AvailableMb, Environment.Is64BitOperatingSystem)
            : 2048;
    }
}
