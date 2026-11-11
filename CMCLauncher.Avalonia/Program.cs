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
            LinuxLauncherPlatform.Initialize();
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
