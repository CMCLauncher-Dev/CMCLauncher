namespace CMCLauncher;

// 游戏 GPU 偏好设置：在启动参数中维护 GpuPreference 项，
// 并把实际的 GPU 偏好应用/还原操作委托给平台层（如 Windows 的 GPU 调度设置）。
public static class GameGpuPreference
{
    // 在分号分隔的启动参数字符串中替换或追加 GpuPreference 项。
    // preference 为 null 时仅移除已有的 GpuPreference 项；结果保持以 ';' 结尾的格式。
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

    // 对指定游戏可执行文件应用 GPU 偏好（委托平台层实现）。
    public static Task Apply(Store store, string executable) =>
        LauncherPlatform.Current.ApplyGpuPreference(store, executable);

    // 还原启动器之前修改过的所有 GPU 偏好设置（委托平台层实现）。
    public static Task Restore(Store store) =>
        LauncherPlatform.Current.RestoreGpuPreferences(store);
}
