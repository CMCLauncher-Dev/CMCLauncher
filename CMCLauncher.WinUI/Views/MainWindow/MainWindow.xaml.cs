using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow : Window
{
    readonly Store store;
    readonly LauncherCore core;
    readonly ModrinthService modrinth;
    readonly CurseForgeCommunityService curseforge;
    readonly CommunityService community;
    readonly MicrosoftAuth auth;
    string page = "home",
        kind = "resourcepack";
    string instanceFilter = "全部实例",
        instanceQuery = "",
        modFilter = "全部 Mods",
        modQuery = "";
    Instance? selected;
    string? world;
    int renderVersion;

    // 暴露给 CMCPage 子类的内部属性
    internal Store Store => store;
    internal LauncherCore Core => core;
    internal ModrinthService ModrinthService => modrinth;
    internal MicrosoftAuth Auth => auth;
    internal Instance? SelectedInstance => selected;
    internal string CurrentPage
    {
        get => page;
        set => page = value;
    }
    internal string ExpandedNavigation
    {
        get => expandedNavigation;
        set => expandedNavigation = value;
    }
    internal int RenderVersion => renderVersion;
    internal CancellationTokenSource? resourceSearch;
    SearchResult? selectedProject;
    string resourceReturnPage = "download-mod";
    Func<string, CancellationToken, Task<CommunityProject>> fetchProject = null!;
    Func<string, CancellationToken, Task<List<CommunityVersion>>> fetchProjectVersions = null!;
    Func<string, int, int, CancellationToken, Task<CommunityVersionPage>>? fetchProjectVersionPage;
    string communityGameCategory = "全部";
    Func<
        string,
        string,
        CancellationToken,
        int,
        int,
        CommunitySearchFilters?,
        Task<CommunitySearchPage>
    > searchResourcePage = null!;
    Func<string, string, string?, Task<List<ContentItem>>> listContent = (
        directory,
        type,
        targetWorld
    ) => Task.Run(() => ContentManager.ListFiles(directory, type, targetWorld));
    Task contentLoad = Task.CompletedTask;
    Task importedMetadataLoad = Task.CompletedTask;
    internal readonly Queue<string> logs = new();
    internal TextBox? logView;
    bool dialogOpen;
    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags
    );

    const uint SWP_FRAMECHANGED = 0x0020;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOZORDER = 0x0004;

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute
    );

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    // 透明 Backdrop 不提供系统材质，DWM 不会自动绘制圆角，需显式指定四角圆角。
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUND = 2;
    const int DWMWCP_DONOTROUND = 1;
    const uint LWA_COLORKEY = 0x1;
    const uint LWA_ALPHA = 0x2;

    // 验证模式运行在 CI 无交互会话，仅跳过透明样式处理以避免不稳定。
    internal bool Verifying;
    internal StackPanel PageBody = new() { Spacing = 11 };

    // UI 验证使用单独的数据目录，避免改动日常使用的实例和账号配置。
    public MainWindow(string[] args)
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            // TransparentBackdrop 不含系统材质，DWM 不会自动圆角窗口，显式开启圆角。
            var corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(
                hWnd,
                DWMWA_WINDOW_CORNER_PREFERENCE,
                ref corner,
                sizeof(int)
            );
            // 延伸客户区到整个窗口，使 XAML 内容铺满四边，避免半透明层相对底层内缩。
            var extend = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1,
            };
            DwmExtendFrameIntoClientArea(hWnd, ref extend);
            // 强制刷新窗口框架，让 DwmExtendFrame 立即生效并移除内容内缩。
            SetWindowPos(
                hWnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED
            );
        }
        catch (Exception ex)
        {
            RuntimeLog.Write("ERROR", "Failed to set layered window style: " + ex);
        }
        Verifying = args.Length == 2 && (args[0] is "--verify-ui" or "--verify-cms-ui" or "--verify-datapack-ui");
        store = new Store(
            args.Length == 2 && (args[0] is "--verify-ui" or "--verify-cms-ui" or "--verify-datapack-ui")
                ? Path.Combine(Path.GetFullPath(args[1]), "data")
                : null
        );
        InitializeComponent();
        // 透明 Backdrop 无系统材质，DWM 只圆角最底层；上层半透明背景是直角矩形，
        // 对根内容施加匹配半径的 Composition RectangleClip 裁剪，与 DWM 圆角对齐。
        var rootVisual = ElementCompositionPreview.GetElementVisual(ModpackDropRoot);
        var compositor = rootVisual.Compositor;
        var rectClip = compositor.CreateRectangleClip();
        rectClip.TopLeftRadius = rectClip.TopRightRadius =
            rectClip.BottomLeftRadius = rectClip.BottomRightRadius = new Vector2(8, 8);
        void SyncClip()
        {
            if (IsZoomed(WindowNative.GetWindowHandle(this)))
            {
                rootVisual.Clip = null;
                return;
            }
            rootVisual.Clip = rectClip;
            rectClip.Left = 0;
            rectClip.Top = 0;
            rectClip.Right = (float)ModpackDropRoot.ActualWidth;
            rectClip.Bottom = (float)ModpackDropRoot.ActualHeight;
        }
        ModpackDropRoot.Loaded += (_, _) => SyncClip();
        ModpackDropRoot.SizeChanged += (_, _) => SyncClip();
        rootVisual.Clip = rectClip;
        InitializeCommunityFilterDrawer();
        RuntimeLog.DebugEnabled = store.Config.Settings.DebugLogging;
        runtimeLogTimer.Interval = TimeSpan.FromMilliseconds(150);
        runtimeLogTimer.Tick += (_, _) => FlushRuntimeLogs();
        runtimeLogTimer.Start();
        RuntimeLog.Written += RuntimeLogWritten;
        ModpackDropRoot.AddHandler(
            UIElement.DragEnterEvent,
            new DragEventHandler(Modpack_DragOver),
            true
        );
        ModpackDropRoot.AddHandler(
            UIElement.DragOverEvent,
            new DragEventHandler(Modpack_DragOver),
            true
        );
        ModpackDropRoot.AddHandler(UIElement.DropEvent, new DragEventHandler(Modpack_Drop), true);
        TaskDrawer.RegisterPropertyChangedCallback(
            SplitView.IsPaneOpenProperty,
            (_, _) => SyncTaskDrawerOverlay()
        );
        TaskDrawer.SizeChanged += (_, _) => ApplyPersonalization();
        TaskDrawerTitle.Text = Ui("Tasks in progress");
        TaskManagerLabel.Text = Ui("Tasks");
        PageTitle.Text = Ui(PageTitle.Text);
        PageSubtitle.Text = Ui(PageSubtitle.Text);
        ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("Back"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);
        InitializeFullClientFrame();
        CustomTitleBar.SizeChanged += (_, _) => UpdateTitleBarInset();
        AppWindow.Changed += (_, _) => UpdateTitleBarInset();
        // Frame 加载前先缓存正文，等它准备好后再执行首次导航。
        var startupUpdateChecked = false;
        PageFrame.Loaded += async (_, _) =>
        {
            pageFrameReady = true;
            if (!startupUpdateChecked && args.Length == 0)
            {
                startupUpdateChecked = true;
                if (store.Config.Settings.MinecraftDirectorySetupPending)
                    await SetupMinecraftDirectory();
                if (store.Config.Settings.CheckLauncherUpdates)
                    _ = CheckLauncherUpdate(true);
            }
            if (pendingPageBody is { } initial)
            {
                pendingPageBody = null;
                NavigatePage(initial);
            }
        };
        BuildNavigation();
        Title = "CMCLauncher";
        TitleIcon.Source = new BitmapImage(
            new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.png"))
        );
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.ico"));
        core = new LauncherCore(store);
        InitializeTerracotta();
        importedMetadataLoad = LoadImportedMetadata();
        InitializeFrpc(
            args.Length == 2 && (args[0] is "--verify-ui" or "--verify-cms-ui" or "--verify-datapack-ui")
                ? Path.Combine(Path.GetFullPath(args[1]), "program", "frpc", "configs")
                : null
        );
        modrinth = new ModrinthService(core, store);
        curseforge = new(core, store, modrinth);
        community = new(modrinth, curseforge);
        auth = new MicrosoftAuth(core);
        fetchPfpSkin = FindPfpSkin;
        fetchVersions = ct => core.VersionCatalog(ct);
        fetchDownloadLoaderBuilds = core.LoaderBuilds;
        fetchProject = community.ProjectDetails;
        fetchProjectVersions = community.ProjectVersions;
        fetchProjectVersionPage = curseforge.ProjectVersionPage;
        searchResourcePage = community.SearchPage;
        fetchCommunityFilters = modrinth.FilterCatalog;
        fetchCurseForgeFilters = curseforge.FilterCatalog;
        core.Status += Report;
        modrinth.Status += Report;
        curseforge.Status += Report;
        curseforge.DownloadBytes += bytes =>
        {
            if (activeWork is { } work)
                Interlocked.Add(ref work.Bytes, bytes);
        };
        core.DownloadBytes += bytes =>
        {
            if (activeWork is { } work)
                Interlocked.Add(ref work.Bytes, bytes);
        };
        taskViewTimer = DispatcherQueue.CreateTimer();
        taskViewTimer.Interval = TimeSpan.FromMilliseconds(200);
        taskViewTimer.Tick += (_, _) => RefreshTaskMetrics();
        taskViewTimer.Start();
        // 捕获回调所属任务，排队期间任务若已结束或更换，就丢弃这次进度。
        core.DownloadProgress += value =>
        {
            var activeTask = task;
            var work = activeWork;
            var phase = work == null ? 0 : Volatile.Read(ref work.Phase);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (
                    activeTask != null
                    && task == activeTask
                    && !launching
                    && work == activeWork
                    && work != null
                    && phase == Volatile.Read(ref work.Phase)
                )
                    UpdateDownloadProgress(value);
            });
        };
        // Process output is already on a worker thread; disk logging must stay off the UI queue.
        core.Log += AddLog;
        core.DebugLog += RuntimeLog.Debug;
        ApplyTheme();
        Render();
        RestoreWindowSize();

        if (
            AppWindow.Presenter is OverlappedPresenter presenter
            && store.Config.Settings.StartMaximized
        )
        {
            presenter.Maximize();
        }

        InitializeWindowSizeSaving();
        systemUi.ColorValuesChanged += SystemThemeChanged;
        RuntimeLog.Write("INFO", "应用启动 · 程序目录=" + AppContext.BaseDirectory);
        Closed += (_, _) =>
        {
            debugWindow?.CloseSafe();
            debugWindow = null;
            updateLifetime.Cancel();
            statsTimer?.Stop();
            RuntimeLog.Written -= RuntimeLogWritten;
            runtimeLogTimer.Stop();
            core.DebugLog -= RuntimeLog.Debug;
            RuntimeLog.Write("INFO", "应用关闭");
            taskViewTimer.Stop();
            toastTimer?.Stop();
            activePopconfirm?.Hide();
            resourceSearch?.Cancel();
            systemUi.ColorValuesChanged -= SystemThemeChanged;
            task?.Cancel();
            StopWallpaperVideo();
            core.Dispose();
            curseforge.Dispose();
            cmsClient?.Dispose();
        };
    }

    // 日志始终记录，但只有仍属于当前任务的消息才能更新任务阶段。
    internal void Report(string message)
    {
        var reportingTask = task;
        var reportingWork = activeWork;
        var phase = reportingWork == null ? 0 : Interlocked.Increment(ref reportingWork.Phase);
        void Apply()
        {
            AddLog(message);
            if (
                reportingTask == null
                || reportingTask != task
                || activeWork == null
                || activeWork != reportingWork
                || phase != Volatile.Read(ref activeWork.Phase)
            )
                return;
            if (activeWork.Stage != message)
                activeWork.Progress = null;
            activeWork.Stage = message;
            RefreshTaskMetrics();
        }
        if (DispatcherQueue.HasThreadAccess)
            Apply();
        else
            DispatcherQueue.TryEnqueue(Apply);
    }

    internal void AddLog(string line) => RuntimeLog.Write("INFO", line);

    readonly object runtimeLogGate = new();
    readonly Queue<string> pendingRuntimeLogs = new();
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer runtimeLogTimer = Microsoft
        .UI.Dispatching.DispatcherQueue.GetForCurrentThread()
        .CreateTimer();

    void RuntimeLogWritten(string line)
    {
        lock (runtimeLogGate)
        {
            // The file keeps every line; only the live view drops old lines during a flood.
            pendingRuntimeLogs.Enqueue(line.Length > 16_000 ? line[^16_000..] : line);
            while (pendingRuntimeLogs.Count > 500)
                pendingRuntimeLogs.Dequeue();
        }
    }

    void FlushRuntimeLogs()
    {
        string[] batch;
        lock (runtimeLogGate)
        {
            if (pendingRuntimeLogs.Count == 0)
                return;
            batch = pendingRuntimeLogs.ToArray();
            pendingRuntimeLogs.Clear();
        }
        foreach (var line in batch)
        {
            logs.Enqueue(line);
            while (logs.Count > 1000)
                logs.Dequeue();
        }
        if (page == "logs" && logView != null)
        {
            var text = logView.Text + Environment.NewLine + string.Join(Environment.NewLine, batch);
            logView.Text = text.Length > 250_000 ? text[^250_000..] : text;
        }
    }

    // 切换界面语言后需重建导航栏和所有文本
    internal async Task SwitchLanguage(string lang)
    {
        localization.Reload();
        store.Config.Settings.LauncherLanguage = lang;
        await store.Save();
        LocalizeStaticChrome();
        NavigationMenu.Children.Clear();
        pageNavigationAnimation?.Stop();
        pageNavigationAnimation = null;
        displayedPageRoute = null;
        PageSurface.Opacity = 1;
        navigationButtons.Clear();
        navigationButtonPalettes.Clear();
        navigationIndicators.Clear();
        navigationSubmenus.Clear();
        animatedSubmenus.Clear();
        animatedNavigationIndicators.Clear();
        selectedRootIndicator = selectedPageIndicator = null;
        BuildNavigation();
        Render();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? windowSizeSaveTimer;

    private void RestoreWindowSize()
    {
        var settings = store.Config.Settings;

        int width = Math.Clamp(settings.WindowWidth, 800, 3000);
        int height = Math.Clamp(settings.WindowHeight, 500, 2000);

        var savedPosition = settings.WindowX.HasValue && settings.WindowY.HasValue;
        var point = new Windows.Graphics.PointInt32(settings.WindowX ?? 0, settings.WindowY ?? 0);
        var display = savedPosition
            ? DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        var x = savedPosition
            ? Math.Clamp(point.X, area.X, area.X + area.Width - width)
            : area.X + (area.Width - width) / 2;
        var y = savedPosition
            ? Math.Clamp(point.Y, area.Y, area.Y + area.Height - height)
            : area.Y + (area.Height - height) / 2;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private void InitializeWindowSizeSaving()
    {
        windowSizeSaveTimer = DispatcherQueue.CreateTimer();
        windowSizeSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
        windowSizeSaveTimer.IsRepeating = false;

        windowSizeSaveTimer.Tick += async (_, _) =>
        {
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                RuntimeLog.Write("ERROR", "保存窗口尺寸失败 · " + ex);
            }
        };

        // Flush pending geometry before shutdown, including an immediate close after dragging.
        var closingSaved = false;
        var closingSaving = false;
        AppWindow.Closing += async (_, args) =>
        {
            if (closingSaved)
                return;
            args.Cancel = true;
            if (closingSaving)
                return;
            closingSaving = true;
            windowSizeSaveTimer.Stop();
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                RuntimeLog.Write("ERROR", "Could not save window placement: " + ex);
            }
            finally
            {
                closingSaved = true;
                Close();
            }
        };

        AppWindow.Changed += (sender, args) =>
        {
            if (!args.DidSizeChange && !args.DidPositionChange)
                return;

            if (
                sender.Presenter is not OverlappedPresenter presenter
                || presenter.State != OverlappedPresenterState.Restored
            )
                return;

            var size = sender.Size;

            if (size.Width < 800 || size.Height < 500)
                return;

            store.Config.Settings.WindowWidth = size.Width;
            store.Config.Settings.WindowHeight = size.Height;
            store.Config.Settings.WindowX = sender.Position.X;
            store.Config.Settings.WindowY = sender.Position.Y;

            windowSizeSaveTimer.Stop();
            windowSizeSaveTimer.Start();
        };
    }
}