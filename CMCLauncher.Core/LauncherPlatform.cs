namespace CMCLauncher;

// Hosts supply OS integration. The default never stores credentials as plaintext.
public class LauncherPlatform
{
    static LauncherPlatform current = new();
    public static LauncherPlatform Current
    {
        get => Volatile.Read(ref current);
        set =>
            Interlocked.Exchange(
                ref current,
                value ?? throw new ArgumentNullException(nameof(value))
            );
    }

    public virtual byte[] TransformCredential(byte[] data, bool protect) =>
        throw new PlatformNotSupportedException(
            "This host has not configured a secure credential store."
        );

    public virtual (long TotalMb, long AvailableMb)? PhysicalMemory() => null;

    public virtual int GeoId => -1;

    public virtual IEnumerable<string> JavaHomes() => [];

    public virtual Task ApplyGpuPreference(Store store, string executable) => Task.CompletedTask;

    public virtual Task RestoreGpuPreferences(Store store) => Task.CompletedTask;
}
