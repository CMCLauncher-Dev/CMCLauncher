using Microsoft.Win32;

namespace CMCLauncher;

// Windows Graphics Settings identifies desktop applications by their full executable path.
public static class GameGpuPreference
{
    const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

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

    public static async Task Apply(Store store, string executable)
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
        key.SetValue(path, WithPreference(previous ?? "", "2"), RegistryValueKind.String);
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

    public static async Task Restore(Store store)
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
                var restored = WithPreference(current!, original);
                if (restored.Length == 0 && pair.Value == null)
                    key.DeleteValue(pair.Key, false);
                else
                    key.SetValue(pair.Key, restored, RegistryValueKind.String);
            }
            store.Config.Settings.ManagedGpuPreferences.Remove(pair.Key);
        }
        await store.Save();
    }
}
