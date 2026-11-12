namespace CMCLauncher;

public static class GameMemory
{
    // Follows HMCLGameRepository.getAutoAllocatedMemory (HMCL, GPL-3.0).
    // https://github.com/HMCL-dev/HMCL/blob/a21bd735e232262bb8842029fc4b5c575810024f/HMCL/src/main/java/org/jackhuang/hmcl/game/HMCLGameRepository.java#L501
    public static int Calculate(long totalMb, long availableMb, bool is64Bit = true)
    {
        var minimum = is64Bit ? 512 : 256;
        var maximum = is64Bit ? 24576 : 768;
        if (totalMb <= minimum || availableMb < 0)
            return is64Bit ? 2048 : 512;
        var available = Math.Clamp(availableMb, 0, totalMb);
        var usable = Math.Max(0, Math.Max(available, totalMb / 16) - 256);
        // Extra RAM contributes progressively less heap, leaving capacity for native allocations.
        var budget =
            Math.Min(usable, 8192) * .8
            + Math.Clamp(usable - 8192, 0, 8192) * .4
            + Math.Max(0, usable - 16384) * .2;
        return (int)Math.Clamp(Math.Floor(budget), minimum, maximum);
    }

    // Fractions share one denominator; never normalize an over-allocation into a misleading scale.
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
