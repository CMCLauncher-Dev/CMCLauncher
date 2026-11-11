namespace CMCLauncher;

public static class MinecraftPlatform
{
    public static string Architecture => LauncherPlatform.Current.Architecture;
    public static string OsName => LauncherPlatform.Current.MinecraftOsName;

    public static void RequireWindowsX64(string feature)
    {
        if (OsName != "windows" || Architecture != "x86_64")
            throw new PlatformNotSupportedException(
                feature + " currently supports Windows x64 only."
            );
    }
}
