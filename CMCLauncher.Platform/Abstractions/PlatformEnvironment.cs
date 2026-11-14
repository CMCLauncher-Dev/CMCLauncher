using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CMCLauncher.Platform;

public sealed record GraphicsUsage(string Name, double Usage);

public sealed record NetworkUsage(double? DownloadBytesPerSecond, double? UploadBytesPerSecond);

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
    NetworkUsage? NetworkUsage();
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

    // 跨平台网络采样：累计接口收发字节差值除以间隔得到速率。
    long lastNetworkBytesReceived,
        lastNetworkBytesSent;
    long lastNetworkTicks;

    public virtual NetworkUsage? NetworkUsage()
    {
        long received = 0,
            sent = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (
                    adapter.NetworkInterfaceType
                        is not (
                            NetworkInterfaceType.Ethernet
                            or NetworkInterfaceType.Wireless80211
                            or NetworkInterfaceType.GigabitEthernet
                            or NetworkInterfaceType.Tunnel
                        )
                        || adapter.OperationalStatus != OperationalStatus.Up
                )
                    continue;
                var statistics = adapter.GetIPv4Statistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
        }
        catch (PlatformNotSupportedException) { }
        catch (NetworkInformationException) { }

        var ticks = Environment.TickCount64;
        if (lastNetworkTicks == 0)
        {
            lastNetworkBytesReceived = received;
            lastNetworkBytesSent = sent;
            lastNetworkTicks = ticks;
            return null;
        }
        var elapsed = (double)(ticks - lastNetworkTicks) / 1000.0;
        double? download =
            received >= lastNetworkBytesReceived
                ? (received - lastNetworkBytesReceived) / elapsed
                : null;
        double? upload =
            sent >= lastNetworkBytesSent ? (sent - lastNetworkBytesSent) / elapsed : null;
        lastNetworkBytesReceived = received;
        lastNetworkBytesSent = sent;
        lastNetworkTicks = ticks;
        return new NetworkUsage(elapsed <= 0 ? null : download, elapsed <= 0 ? null : upload);
    }

    public virtual IEnumerable<string> JavaHomes() => [];

    public virtual Task MoveToTrash(string path) =>
        throw new PlatformNotSupportedException("This host does not provide a recycle bin.");
}