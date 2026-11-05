namespace CMCLauncher;

public static class GameGpuPreference
{
    public static string WithPreference(string value, string? preference)
    {
        var parts = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p =>
                !p.TrimStart().StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        if (preference != null)
            parts.Add("GpuPreference=" + preference);
        return parts.Count == 0 ? "" : string.Join(';', parts) + ";";
    }

    public static Task Apply(Store store, string executable) =>
        LauncherPlatform.Current.ApplyGpuPreference(store, executable);

    public static Task Restore(Store store) =>
        LauncherPlatform.Current.RestoreGpuPreferences(store);
}
