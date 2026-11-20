namespace CMCLauncher;

// Minecraft 平台信息：从平台宿主获取当前架构与操作系统名称，
// 并提供对仅支持 Windows x64 的功能做平台校验。
public static class MinecraftPlatform
{
    // 当前 CPU 架构（如 x86_64、arm64）。
    public static string Architecture => LauncherPlatform.Current.Architecture;

    // Minecraft 视角的操作系统名称（如 windows、osx、linux）。
    public static string OsName => LauncherPlatform.Current.MinecraftOsName;

    // 校验当前平台是否为 Windows x64，否则抛出异常（用于仅支持该平台的特性）。
    public static void RequireWindowsX64(string feature)
    {
        if (OsName != "windows" || Architecture != "x86_64")
            throw new PlatformNotSupportedException(
                feature + " currently supports Windows x64 only."
            );
    }
}
