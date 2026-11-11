namespace CMCLauncher;

// Hosts supply OS integration. The default never stores credentials as plaintext.
public class LauncherPlatform : CMCLauncher.Platform.PlatformEnvironment
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

    public virtual Task ApplyGpuPreference(Store store, string executable) => Task.CompletedTask;

    public virtual Task RestoreGpuPreferences(Store store) => Task.CompletedTask;
}
