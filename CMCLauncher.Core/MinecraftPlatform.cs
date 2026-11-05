namespace CMCLauncher;

public static class MinecraftPlatform
{
    public static string Architecture =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException("Unsupported Minecraft architecture."),
        };

    public static string OsName =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx"
        : OperatingSystem.IsLinux() ? "linux"
        : throw new PlatformNotSupportedException("Unsupported Minecraft platform.");

    public static void RequireWindowsX64(string feature)
    {
        if (
            !OperatingSystem.IsWindows()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64
        )
            throw new PlatformNotSupportedException(
                feature + " currently supports Windows x64 only."
            );
    }
}
