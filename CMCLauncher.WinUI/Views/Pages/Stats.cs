using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OSVersionInfoEx
    {
        public uint OSVersionInfoSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePackVersion;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    [DllImport("ntdll.dll")]
    static extern int RtlGetVersion(ref OSVersionInfoEx versionInfo);

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    static ulong FileTimeTick(FileTime t) => ((ulong)t.High << 32) | t.Low;

    ulong sysLastIdle,
        sysLastKernel,
        sysLastUser;
    bool sysFirst;
    DispatcherTimer? statsTimer;
    readonly Dictionary<string, Queue<double>> statsHistory = new();
    StatsGpuAdapter[] cachedStatsGpuAdapters = [];
    string StatsGpuCachePath => Path.Combine(store.Root, "cache", "gpu-adapters.json");
    bool gpuSamplingBusy;
    bool gpuResamplePending;

    void StatsPage()
    {
        Header(Ui("System status"), Ui("Live CPU, memory and graphics usage"));
        var continuous = new ToggleSwitch
        {
            Header = Ui("Continuous sampling"),
            IsOn = store.Config.Settings.ContinuousStatsSampling,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        continuous.Toggled += async (_, _) =>
        {
            try
            {
                store.Config.Settings.ContinuousStatsSampling = continuous.IsOn;
                await store.Save();
                if (continuous.IsOn)
                    statsTimer?.Start();
                else
                    statsTimer?.Stop();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        PageBody.Children.Add(
            new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    continuous,
                    Text(
                        "Sampling pause/resume switch; on keeps sampling after leaving this page.",
                        12
                    ),
                },
            }
        );
        var memValue = Text("", 14);
        var processValue = Text("", 14);
        var cpuTrend = StatsTrend("cpu");
        var memoryTrend = StatsTrend("memory");
        var cards = new List<Border>();
        var uptime = Text("", 14);
        var runtime = Text("", 14);
        var budget = Text("", 14);
        var disk = Text("", 14);
        var diskBar = new ProgressBar { Minimum = 0, Maximum = 100 };
        var diskPct = Text(Ui("Not available"), 14);
        var diskTrend = StatsTrend("disk");
        var networkDown = Text(Ui("Not available"), 14);
        var networkUp = Text(Ui("Not available"), 14);
        var networkTrend = StatsRateTrend("network");
        var library = Text("", 14);
        StackPanel Section(string title, params UIElement[] content)
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Text(title, 18, true));
            foreach (var child in content)
                body.Children.Add(child);
            return body;
        }
        cards.Add(Card(Section("CPU usage", cpuTrend.View)));
        cards.Add(Card(Section("Memory usage", memValue, memoryTrend.View)));
        cards.Add(
            Card(
                Section(
                    "Disk activity",
                    diskTrend.View,
                    StatRow(Ui("Game storage"), diskPct)
                )
            )
        );
        cards.Add(
            Card(
                Section(
                    "Network",
                    StatRow(Ui("Download"), networkDown),
                    StatRow(Ui("Upload"), networkUp),
                    networkTrend.View
                )
            )
        );
        cards.Add(
            Card(
                Section(
                    "System environment",
                    Text(WindowsName(), 14),
                    Text(RuntimeInformation.OSDescription, 14),
                    Text(
                        $"{RuntimeInformation.OSArchitecture} · {Environment.ProcessorCount} "
                            + Ui("Logical processors"),
                        14
                    ),
                    Text(
                        RuntimeInformation.FrameworkDescription
                            + " · "
                            + RuntimeInformation.ProcessArchitecture,
                        14
                    ),
                    StatRow(Ui("System uptime"), uptime)
                )
            )
        );
        cards.Add(
            Card(
                Section(
                    "Launcher runtime",
                    StatRow(Ui("Launcher process"), processValue),
                    StatRow(Ui("Running time"), runtime),
                    StatRow(Ui("Game memory budget"), budget),
                    StatRow(Ui("Library summary"), library)
                )
            )
        );
        cards.Add(
            Card(
                Section(
                    "Game storage",
                    Text(store.MinecraftRoot, 12),
                    disk,
                    diskBar,
                    Action("打开文件夹", (_, _) => OpenFolder(store.MinecraftRoot))
                )
            )
        );
        var gpuModels = Text(Ui("Reading graphics adapters…"), 14);
        var gpuPlaceholder = Card(Section("Graphics", gpuModels));
        cards.Insert(2, gpuPlaceholder);
        var dashboard = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        dashboard.ColumnDefinitions.Add(new ColumnDefinition());
        dashboard.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < cards.Count; i++)
            dashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var card in cards)
            dashboard.Children.Add(card);
        void LayoutCards(double width)
        {
            var columns = width >= 700 ? 2 : 1;
            var rows = (cards.Count + columns - 1) / columns;
            while (dashboard.RowDefinitions.Count > rows)
                dashboard.RowDefinitions.RemoveAt(dashboard.RowDefinitions.Count - 1);
            while (dashboard.RowDefinitions.Count < rows)
                dashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < cards.Count; i++)
            {
                Grid.SetRow(cards[i], i / columns);
                Grid.SetColumn(cards[i], i % columns);
                Grid.SetColumnSpan(cards[i], columns == 1 ? 2 : 1);
            }
        }
        dashboard.SizeChanged += (_, args) => LayoutCards(args.NewSize.Width);
        LayoutCards(PageBody.ActualWidth);
        PageBody.Children.Add(dashboard);
        if (statsTimer != null)
            statsTimer.Stop();
        statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        sysFirst = true;
        var ownerTimer = statsTimer;
        var sampling = false;
        var gpuCharts = new Dictionary<string, (TextBlock Value, Action<double> Add)>();
        void ShowGraphics(StatsGpuAdapter[] adapters, bool append)
        {
            foreach (var adapter in adapters)
            {
                if (!gpuCharts.TryGetValue(adapter.Name, out var chart))
                {
                    var value = Text(Ui("Not available"), 18, true);
                    var trend = StatsTrend("gpu:" + adapter.Name, value);
                    var card = Card(Section(adapter.Name, trend.View));
                    var index = 2 + gpuCharts.Count;
                    cards.Insert(index, card);
                    dashboard.Children.Add(card);
                    chart = (value, trend.Add);
                    gpuCharts.Add(adapter.Name, chart);
                }
                chart.Value.Text =
                    adapter.Usage < 0 ? Ui("Not available") : adapter.Usage.ToString("0.0") + "%";
                if (append && adapter.Usage >= 0)
                    chart.Add(adapter.Usage);
            }
            if (gpuCharts.Count > 0)
            {
                cards.Remove(gpuPlaceholder);
                dashboard.Children.Remove(gpuPlaceholder);
            }
            LayoutCards(dashboard.ActualWidth);
        }
        if (cachedStatsGpuAdapters.Length == 0)
        {
            try
            {
                if (
                    File.Exists(StatsGpuCachePath)
                    && new FileInfo(StatsGpuCachePath).Length < 1024 * 1024
                )
                {
                    var cache = System.Text.Json.Nodes.JsonNode.Parse(
                        File.ReadAllText(StatsGpuCachePath)
                    );
                    cachedStatsGpuAdapters = cache?["Names"]
                        is System.Text.Json.Nodes.JsonArray names
                        ? names.Select(n => new StatsGpuAdapter(n!.ToString(), -1)).ToArray()
                        : [];
                }
            }
            catch (Exception ex)
            {
                AddLog("GPU cache: " + ex.Message);
            }
        }
        ShowGraphics(cachedStatsGpuAdapters, false);
        async Task RefreshGraphics()
        {
            if (gpuSamplingBusy)
            {
                gpuResamplePending = true;
                return;
            }
            gpuSamplingBusy = true;
            try
            {
                do
                {
                    gpuResamplePending = false;
                    if (store.Config.Settings.ContinuousStatsSampling)
                    {
                        var adapters = await ReadStatsGpuAdapters();
                        if (
                            (page != "stats" && !store.Config.Settings.ContinuousStatsSampling)
                            || statsTimer != ownerTimer
                        )
                            return;
                        cachedStatsGpuAdapters = adapters;
                        ShowGraphics(adapters, true);
                    }
                    else if (gpuCharts.Count == 0)
                    {
                        gpuModels.Text = Ui("Continuous sampling off");
                    }
                } while (gpuResamplePending);
            }
            catch (OperationCanceledException)
            {
                RuntimeLog.Debug("GPU sampling cancelled or timed out.");
            }
            catch (Exception ex)
            {
                if (
                    (page == "stats" || store.Config.Settings.ContinuousStatsSampling)
                    && statsTimer == ownerTimer
                )
                    gpuModels.Text = Ui("Not available");
                AddLog("GPU sampling: " + ex.Message);
            }
            finally
            {
                gpuSamplingBusy = false;
            }
        }
        _ = RefreshGraphics();
        statsTimer.Tick += (_, _) =>
        {
            if (!store.Config.Settings.ContinuousStatsSampling || statsTimer != ownerTimer)
            {
                ownerTimer.Stop();
                return;
            }
            if (sampling)
                return;
            sampling = true;
            try
            {
                GetSystemTimes(out var idleNow, out var kernelNow, out var userNow);
                var idle = FileTimeTick(idleNow);
                var kernel = FileTimeTick(kernelNow);
                var user = FileTimeTick(userNow);
                double cpu = -1;
                if (!sysFirst)
                {
                    var di = idle - sysLastIdle;
                    var dk = kernel - sysLastKernel;
                    var du = user - sysLastUser;
                    var total = dk + du;
                    var busy = total > di ? total - di : 0UL;
                    cpu = total > 0 ? 100.0 * busy / total : 0;
                }
                sysLastIdle = idle;
                sysLastKernel = kernel;
                sysLastUser = user;
                sysFirst = false;

                var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                GlobalMemoryStatusEx(ref ms);
                var used = ms.TotalPhys - ms.AvailPhys;
                var memPct = ms.TotalPhys > 0 ? 100.0 * used / ms.TotalPhys : 0;

                using var process = Process.GetCurrentProcess();
                var workingMB = process.WorkingSet64 / 1048576.0;
                var privateMB = process.PrivateMemorySize64 / 1048576.0;

                memValue.Text =
                    (used / 1073741824.0).ToString("0.00")
                    + " GB / "
                    + (ms.TotalPhys / 1073741824.0).ToString("0.00")
                    + " GB   ("
                    + memPct.ToString("0.0")
                    + "%)";
                processValue.Text =
                    workingMB.ToString("0")
                    + " MB working set · "
                    + privateMB.ToString("0")
                    + " MB private";
                if (cpu >= 0)
                    cpuTrend.Add(cpu);
                memoryTrend.Add(memPct);
                uptime.Text = TimeSpan
                    .FromMilliseconds(Environment.TickCount64)
                    .ToString(@"d\.hh\:mm\:ss");
                runtime.Text = (DateTime.Now - process.StartTime).ToString(@"d\.hh\:mm\:ss");
                budget.Text =
                    GameMemory.Resolve(store.Config.Settings).ToString("N0")
                    + " MB · "
                    + Ui(store.Config.Settings.AutoMemory ? "Automatic [context 467]" : "Manual");
                library.Text = UiFormat(
                    "{0} instances · {1} Java runtimes · {2} accounts",
                    store.Config.Instances.Count,
                    store.Config.Settings.JavaInstallations.Count,
                    store.Config.Accounts.Count
                );
                try
                {
                    var drive = new DriveInfo(
                        Path.GetPathRoot(Path.GetFullPath(store.MinecraftRoot))!
                    );
                    disk.Text = UiFormat(
                        "{0:N1} GB free / {1:N1} GB total",
                        drive.AvailableFreeSpace / 1073741824.0,
                        drive.TotalSize / 1073741824.0
                    );
                    diskBar.Value =
                        drive.TotalSize > 0
                            ? 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize
                            : 0;
                    diskPct.Text = diskBar.Value.ToString("0.0") + "% occupied";
                    diskTrend.Add(diskBar.Value);
                }
                catch (IOException)
                {
                    disk.Text = Ui("Not available");
                    diskPct.Text = Ui("Not available");
                }
                catch (UnauthorizedAccessException)
                {
                    disk.Text = Ui("Not available");
                    diskPct.Text = Ui("Not available");
                }
                var network = LauncherPlatform.Current.NetworkUsage();
                if (network != null && network.DownloadBytesPerSecond != null)
                    networkTrend.Add(network.DownloadBytesPerSecond.Value);
                if (network != null)
                {
                    networkDown.Text =
                        network.DownloadBytesPerSecond == null
                            ? Ui("Not available")
                            : StatsFormatRate(network.DownloadBytesPerSecond.Value);
                    networkUp.Text =
                        network.UploadBytesPerSecond == null
                            ? Ui("Not available")
                            : StatsFormatRate(network.UploadBytesPerSecond.Value);
                }
                _ = RefreshGraphics();
            }
            finally
            {
                sampling = false;
            }
        };
        if (store.Config.Settings.ContinuousStatsSampling)
            statsTimer.Start();
    }

    // Bounded history; redraw only the small chart, including when its card changes width.
    (FrameworkElement View, Action<double> Add) StatsTrend(string key, TextBlock? percentage = null)
    {
        if (!statsHistory.TryGetValue(key, out var samples))
            statsHistory[key] = samples = new Queue<double>();
        var canvas = new Canvas { Height = 90, HorizontalAlignment = HorizontalAlignment.Stretch };
        percentage ??= Text("—", 16, true);
        percentage.HorizontalAlignment = HorizontalAlignment.Right;
        percentage.VerticalAlignment = VerticalAlignment.Top;
        percentage.Margin = new Thickness(0, 4, 8, 0);
        var frame = new Grid { Children = { canvas, percentage } };
        var color = Brush(PrimaryColor);
        void Draw()
        {
            canvas.Children.Clear();
            var width = canvas.ActualWidth;
            if (width <= 0)
                return;
            foreach (var y in new[] { 0d, 45d, 90d })
                canvas.Children.Add(
                    new Microsoft.UI.Xaml.Shapes.Line
                    {
                        X1 = 0,
                        X2 = width,
                        Y1 = y,
                        Y2 = y,
                        Stroke = color,
                        Opacity = 0.15,
                        StrokeThickness = 1,
                    }
                );
            var line = new Microsoft.UI.Xaml.Shapes.Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
            };
            var fill = new Microsoft.UI.Xaml.Shapes.Polygon { Fill = color, Opacity = 0.12 };
            var values = samples.ToArray();
            for (var i = 0; i < values.Length; i++)
            {
                var point = new Windows.Foundation.Point(
                    width * (60 - values.Length + i) / 59,
                    90 * (1 - values[i] / 100)
                );
                line.Points.Add(point);
                fill.Points.Add(point);
            }
            if (values.Length > 0)
            {
                fill.Points.Add(new Windows.Foundation.Point(width, 90));
                fill.Points.Add(
                    new Windows.Foundation.Point(width * (60 - values.Length) / 59, 90)
                );
            }
            canvas.Children.Add(fill);
            canvas.Children.Add(line);
        }
        canvas.SizeChanged += (_, _) => Draw();
        ToolTipService.SetToolTip(frame, Ui("Recent 60 samples · 0–100%"));
        return (
            frame,
            value =>
            {
                percentage.Text = Math.Clamp(value, 0, 100).ToString("0.0") + "%";
                samples.Enqueue(Math.Clamp(value, 0, 100));
                while (samples.Count > 60)
                    samples.Dequeue();
                Draw();
            }
        );
    }

    StackPanel StatRow(string label, TextBlock value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(Text(label, 12, true));
        row.Children.Add(value);
        return row;
    }

    // 速率采用自适应缩放：以 60 个样本中的最大值为满刻度绘制，右上方实时补充数值。
    (FrameworkElement View, Action<double> Add) StatsRateTrend(string key)
    {
        if (!statsHistory.TryGetValue(key, out var samples))
            statsHistory[key] = samples = new Queue<double>();
        var canvas = new Canvas { Height = 90, HorizontalAlignment = HorizontalAlignment.Stretch };
        var current = Text("—", 14, true);
        current.HorizontalAlignment = HorizontalAlignment.Right;
        current.VerticalAlignment = VerticalAlignment.Top;
        current.Margin = new Thickness(0, 4, 8, 0);
        var frame = new Grid { Children = { canvas, current } };
        var color = Brush(PrimaryColor);
        void Draw()
        {
            canvas.Children.Clear();
            var width = canvas.ActualWidth;
            if (width <= 0)
                return;
            var peak = samples.Count > 0 ? samples.Max() : 0;
            foreach (var y in new[] { 0d, 45d, 90d })
                canvas.Children.Add(
                    new Microsoft.UI.Xaml.Shapes.Line
                    {
                        X1 = 0,
                        X2 = width,
                        Y1 = y,
                        Y2 = y,
                        Stroke = color,
                        Opacity = 0.15,
                        StrokeThickness = 1,
                    }
                );
            var line = new Microsoft.UI.Xaml.Shapes.Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
            };
            var fill = new Microsoft.UI.Xaml.Shapes.Polygon { Fill = color, Opacity = 0.12 };
            var values = samples.ToArray();
            for (var i = 0; i < values.Length; i++)
            {
                var ratio = peak > 0 ? values[i] / peak : 0;
                var point = new Windows.Foundation.Point(
                    width * (60 - values.Length + i) / 59,
                    90 * (1 - ratio)
                );
                line.Points.Add(point);
                fill.Points.Add(point);
            }
            if (values.Length > 0)
            {
                fill.Points.Add(new Windows.Foundation.Point(width, 90));
                fill.Points.Add(new Windows.Foundation.Point(width * (60 - values.Length) / 59, 90));
            }
            canvas.Children.Add(fill);
            canvas.Children.Add(line);
        }
        canvas.SizeChanged += (_, _) => Draw();
        ToolTipService.SetToolTip(frame, "近期 60 次采样 · 自适应速率");
        return (
            frame,
            value =>
            {
                samples.Enqueue(value);
                if (samples.Count > 60)
                    samples.Dequeue();
                current.Text = StatsFormatRate(value);
                Draw();
            }
        );
    }

    // 通过 RtlGetVersion 读取真实 OS 版本，构建「Windows 11 Pro」等友好名称。
    string WindowsName()
    {
        var info = new OSVersionInfoEx
        {
            OSVersionInfoSize = (uint)Marshal.SizeOf<OSVersionInfoEx>(),
        };
        if (RtlGetVersion(ref info) != 0)
            return RuntimeInformation.OSDescription;
        var version =
            info.MajorVersion == 10 && info.BuildNumber >= 22000 ? "Windows 11" : "Windows 10";
        var type = info.ProductType == 1 ? "Client" : "Server";
        return type == "Client"
            ? $"{version} · Build {info.BuildNumber}"
            : $"{version} {type} · Build {info.BuildNumber}";
    }

    static string StatsFormatRate(double bytesPerSecond) =>
        bytesPerSecond > 1024.0 * 1024 * 1024
            ? (bytesPerSecond / 1024 / 1024 / 1024).ToString("0.00") + " GB/s"
            : bytesPerSecond > 1024 * 1024
                ? (bytesPerSecond / 1024 / 1024).ToString("0.0") + " MB/s"
                : (bytesPerSecond / 1024).ToString("0.0") + " KB/s";

    sealed record StatsGpuAdapter(string Name, double Usage);

    async Task<StatsGpuAdapter[]> ReadStatsGpuAdapters() =>
        (await LauncherPlatform.Current.Graphics(StatsGpuCachePath, CancellationToken.None))
            .Select(g => new StatsGpuAdapter(g.Name, g.Usage))
            .ToArray();
}