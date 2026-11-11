using System.Runtime.InteropServices;

namespace CMCLauncher.Platform;

public sealed record GraphicsUsage(string Name, double Usage);

public interface IPlatformEnvironment
{
    string MinecraftOsName { get; }
    string Architecture { get; }
    byte[] TransformCredential(byte[] data, bool protect);
    (long TotalMb, long AvailableMb)? PhysicalMemory();
    int GeoId { get; }
    IEnumerable<string> JavaHomes();
    Task MoveToTrash(string path);
    Task<double?> CpuUsage(CancellationToken ct);
    Task<IReadOnlyList<GraphicsUsage>> Graphics(string cachePath, CancellationToken ct);
}

// Portable host defaults; an online credential store must be explicitly implemented.
public class PlatformEnvironment : IPlatformEnvironment
{
    public virtual string MinecraftOsName =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx"
        : OperatingSystem.IsLinux() ? "linux"
        : throw new PlatformNotSupportedException();
    public virtual string Architecture =>
        RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException(),
        };

    public virtual byte[] TransformCredential(byte[] data, bool protect) =>
        throw new PlatformNotSupportedException(
            "This host has not configured a secure credential store."
        );

    public virtual (long TotalMb, long AvailableMb)? PhysicalMemory() => null;

    public virtual int GeoId => -1;

    public virtual Task<double?> CpuUsage(CancellationToken ct) => Task.FromResult<double?>(null);

    public virtual Task<IReadOnlyList<GraphicsUsage>> Graphics(
        string cachePath,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<GraphicsUsage>>([]);

    public virtual IEnumerable<string> JavaHomes() => [];

    public virtual Task MoveToTrash(string path) =>
        throw new PlatformNotSupportedException("This host does not provide a recycle bin.");
}
