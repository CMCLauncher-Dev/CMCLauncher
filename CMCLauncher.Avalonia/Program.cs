using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using CMCLauncher.Avalonia.Views;

namespace CMCLauncher.Avalonia;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CMCLauncher.Native",
                    "logs"
                );
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, "startup-error.log");
                File.AppendAllText(
                    file,
                    DateTimeOffset.Now.ToString("O")
                        + Environment.NewLine
                        + ex
                        + Environment.NewLine
                );
                Console.Error.WriteLine("Startup diagnostics: " + file);
            }
            catch (Exception loggingError)
            {
                Console.Error.WriteLine(
                    "Cannot write startup diagnostics: " + loggingError.Message
                );
            }
            Environment.ExitCode = 1;
        }
    }

    static void Run(string[] args)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "On Windows, run CMCLauncher.WinUI. Avalonia is the Linux/macOS frontend."
            );
        if (DesktopUpdateInstaller.Handle(args))
            return;
        if (CMCLauncher.Platform.GameExitWatchdog.Handle(args))
            return;
        Arguments = args;
        if (OperatingSystem.IsMacOS())
            MacOSLauncherPlatform.Initialize();
        else if (OperatingSystem.IsLinux())
        {
            // Check before the first HTTPS call: the runtime otherwise aborts without a managed exception.
            foreach (var library in new[] { "libcrypto.so.3", "libssl.so.3" })
            {
                if (!NativeLibrary.TryLoad(library, out var handle))
                    throw new PlatformNotSupportedException(
                        "Cannot load "
                            + library
                            + ". Install OpenSSL 3 libraries (Fedora: openssl3-libs; Debian/Ubuntu: libssl3 or libssl3t64)."
                    );
                NativeLibrary.Free(handle);
            }
            LinuxLauncherPlatform.Initialize();
        }
        // CI can activate the published self-contained host without opening an AppKit window.
        if (args.Length == 2 && args[0] == "--verify-platform-runtime")
        {
            if (!string.Equals(args[1], DesktopUpdateInstaller.Rid, StringComparison.Ordinal))
                throw new PlatformNotSupportedException(
                    "The published runtime does not match the expected RID."
                );
            Console.WriteLine("Desktop runtime activated: " + DesktopUpdateInstaller.Rid);
            return;
        }
        BuildApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
