using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

// 程序入口 — 负责 MUI 语言资源部署、COM 初始化、窗口启动
internal static class Program
{
    // The single-EXE host keeps editable files beside the distributed executable.
    internal static string UserDirectory =>
        Environment.GetEnvironmentVariable("CMCLAUNCHER_PROGRAM_DIRECTORY")
            is { Length: > 0 } directory
            ? Path.GetFullPath(directory)
            : AppContext.BaseDirectory;

    // Portable installs keep their data beside the executable. Protected installations use
    // a per-user directory; never require elevation just to write logs or FRP configurations.
    static readonly Lazy<string> WritableRoot = new(() => ResolveWritableDirectory(UserDirectory));
    internal static string WritableDirectory => WritableRoot.Value;

    internal static string ResolveWritableDirectory(string preferred) =>
        PortableDataDirectory.Resolve(
            preferred,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMCLauncher"
            )
        );

    const uint MuiLanguageName = 0x8;

    // 当前会话已加载的 MUI 语言列表（如 zh-CN、en-us）
    internal static IReadOnlyList<string> LoadedMuiLanguages { get; private set; } = [];

    // COM 初始化/清理
    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    static extern void OleUninitialize();

    // 获取用户首选 UI 语言列表（Windows 多语言设置）
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetUserPreferredUILanguages(
        uint flags,
        ref uint count,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? languages,
        ref uint length
    );

    [STAThread]
    static int Main(string[] args)
    {
        WindowsLauncherPlatform.Initialize();
#if SINGLE_FILE
        ExtractCompanionFiles();
#endif
        if (args.Length >= 2 && args[0] == "--cleanup-mui")
            return 0; // Older launchers may still invoke this command; native resources are now persistent.
        PrepareMuiResources();
        if (args.Length == 2 && args[0] == "--verify-singlefile-runtime")
        {
            var result = OleInitialize(IntPtr.Zero);
            if (result < 0)
                Marshal.ThrowExceptionForHR(result);
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                _ = Application.Current;
                File.WriteAllLines(
                    args[1],
                    new[]
                    {
                        "activation=success",
                        "runtime=" + AppContext.BaseDirectory,
                        "program=" + UserDirectory,
                    }
                );
                return 0;
            }
            finally
            {
                OleUninitialize();
            }
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // 文件拖放依赖 OLE，在 STA 主线程初始化，并在消息循环退出后成对清理。
        var oleResult = OleInitialize(IntPtr.Zero);
        if (oleResult < 0)
            Marshal.ThrowExceptionForHR(oleResult);
        try
        {
            Application.Start(callback =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
                );
                _ = new CMCApp(args);
            });
        }
        finally
        {
            OleUninitialize();
        }
        return 0;
    }

    // 部署 MUI 语言资源 — 将用户首选语言的 .mui 文件从 languages 目录复制到 exe 同级
    static void PrepareMuiResources()
    {
        try
        {
            PrepareMuiResourcesCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RuntimeLog.Write("WARN", "Native language resources are unavailable: " + ex.Message);
        }
    }

    static void PrepareMuiResourcesCore()
    {
        var root = AppContext.BaseDirectory;
        var languageRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languageRoot))
            return;
        var sources = Directory
            .GetDirectories(languageRoot)
            .Where(path => Path.GetFileName(path).Contains('-'))
            .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        var preferred = new List<string>();
        uint count = 0,
            length = 0;
        GetUserPreferredUILanguages(MuiLanguageName, ref count, null, ref length);
        if (length > 0 && length < 32768)
        {
            var buffer = new StringBuilder((int)length);
            if (GetUserPreferredUILanguages(MuiLanguageName, ref count, buffer, ref length))
                preferred.AddRange(
                    buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries)
                );
        }
        preferred.Add(CultureInfo.CurrentUICulture.Name);
        preferred.Add("en-us");
        preferred.Add("zh-CN");

        var loaded = new List<string>();
        foreach (var language in preferred.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!sources.TryGetValue(language, out var source))
                continue;
            var destination = Path.Combine(root, Path.GetFileName(source));
            try
            {
                if (NativeLanguageResources.Stage(source, destination))
                    loaded.Add(Path.GetFileName(source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A protected installation may not stage optional native translations.
                // The published en-US resources remain available without writing to the install directory.
                RuntimeLog.Write(
                    "WARN",
                    "Cannot stage native language " + language + ": " + ex.Message
                );
            }
        }
        LoadedMuiLanguages = loaded;
    }

#if SINGLE_FILE
    // 单文件发布模式下，将嵌入的伴生文件解压到 exe 所在目录
    static void ExtractCompanionFiles()
    {
        var executableDirectory =
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var exeDir = ResolveWritableDirectory(executableDirectory);
        if (!string.Equals(exeDir, executableDirectory, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("CMCLAUNCHER_PROGRAM_DIRECTORY", exeDir);
        var marker = Path.Combine(exeDir, ".companion-extracted");
        if (
            File.Exists(marker)
            && File.ReadAllText(marker).Trim() == CompanionData.CompanionVersion
        )
            return;
        foreach (var (relativePath, data) in CompanionData.Files)
        {
            var destination = Path.Combine(exeDir, relativePath);
            var directory = Path.GetDirectoryName(destination);
            if (directory is not null)
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, data);
        }
        // 所有伴生文件写完才更新版本标记，中断后下次启动仍会重新解包。
        File.WriteAllText(marker, CompanionData.CompanionVersion);
    }
#endif
}

// WinUI 3 Application 子类 — 处理未捕获异常、命令行参数、窗口创建
public sealed partial class CMCApp : Application
{
    readonly string[] args;
    MainWindow? window;

    public CMCApp(string[] arguments)
    {
        args = arguments;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "CMCLauncher.WinUI-error.log"),
                    DateTime.Now + " " + e.Exception + Environment.NewLine
                );
            }
            catch { }
        };
    }

    // 验证模式也启动真实窗口，失败信息写入指定目录后退出应用。
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(this.args);
        window.Activate();
        if (this.args.Length == 2 && (this.args[0] is "--verify-ui" or "--verify-cms-ui"))
        {
            var directory = Path.GetFullPath(this.args[1]);
            Directory.CreateDirectory(directory);
            try
            {
                await Task.Delay(600);
                if (this.args[0] == "--verify-cms-ui")
                    await window.VerifyCmsUi(directory);
                else
                    await window.VerifyUi(directory);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            Exit();
        }
    }
}
