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
    readonly SemaphoreSlim gpuSampleLock = new(1, 1);

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
                    Text("Continue sampling resource usage after leaving this page.", 12),
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
                    "System environment",
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
        var gpuTicks = 0;
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
            if (!await gpuSampleLock.WaitAsync(0))
                return;
            try
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
                gpuSampleLock.Release();
            }
        }
        _ = RefreshGraphics();
        statsTimer.Tick += (_, _) =>
        {
            if (
                (page != "stats" && !store.Config.Settings.ContinuousStatsSampling)
                || statsTimer != ownerTimer
            )
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
                }
                catch (IOException)
                {
                    disk.Text = Ui("Not available");
                }
                catch (UnauthorizedAccessException)
                {
                    disk.Text = Ui("Not available");
                }
                if (gpuTicks++ % 5 == 0)
                    _ = RefreshGraphics();
            }
            finally
            {
                sampling = false;
            }
        };
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

    sealed record StatsGpuAdapter(string Name, double Usage);

    async Task<StatsGpuAdapter[]> ReadStatsGpuAdapters()
    {
        const string script = """
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $ErrorActionPreference='Stop'
            $cache=$null
            try { $cache=Get-Content -LiteralPath $env:CMC_GPU_CACHE -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
            if($null -eq $cache -or $cache.Boot -ne $env:CMC_GPU_BOOT -or $cache.Created -lt [DateTime]::UtcNow.AddHours(-24).ToString('o')) {
                $names=@(Get-CimInstance Win32_VideoController | Where-Object { $_.PNPDeviceID -like 'PCI\*' } | Select-Object -ExpandProperty Name)
                $registry=@(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\DirectX' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath | Select-Object Description,AdapterLuid })
                $cache=@{Names=$names;Registry=$registry;Boot=$env:CMC_GPU_BOOT;Created=[DateTime]::UtcNow.ToString('o')}
                if($names.Count -gt 0) {
                    try {
                        $directory=[IO.Path]::GetDirectoryName($env:CMC_GPU_CACHE)
                        [IO.Directory]::CreateDirectory($directory) | Out-Null
                        $temporary=$env:CMC_GPU_CACHE+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
                        $cache | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $temporary -Encoding UTF8
                        Move-Item -LiteralPath $temporary -Destination $env:CMC_GPU_CACHE -Force
                    } catch { }
                }
            }
            $names=@($cache.Names)
            $registry=@($cache.Registry)
            $usage=@{}
            try {
                $samples=(Get-Counter '\GPU Engine(*)\Utilization Percentage').CounterSamples
                foreach($engine in ($samples | Group-Object { $_.InstanceName -replace '^pid_[0-9]+_','' })) {
                    if($engine.Name -match 'luid_0x([0-9a-f]+)_0x([0-9a-f]+)') {
                        $id=([uint64][Convert]::ToUInt32($Matches[1],16)*4294967296)+[Convert]::ToUInt32($Matches[2],16)
                        foreach($device in $registry) {
                            if($null -ne $device.AdapterLuid -and [uint64]$device.AdapterLuid -eq $id) {
                                $load=[Math]::Min(100,[Math]::Max(0,($engine.Group | Measure-Object CookedValue -Sum).Sum))
                                $key=[string]$device.Description
                                if(!$usage.ContainsKey($key) -or $load -gt $usage[$key]) { $usage[$key]=$load }
                            }
                        }
                    }
                }
            } catch { }
            $result=@($names | Select-Object -Unique | ForEach-Object {
                $value=-1; if($usage.ContainsKey($_)) { $value=$usage[$_] }
                @{Name=$_;Usage=$value}
            })
            ConvertTo-Json -InputObject $result -Compress
            """;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                ),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            },
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["CMC_GPU_CACHE"] = StatsGpuCachePath;
        process.StartInfo.Environment["CMC_GPU_BOOT"] = (
            DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64)
        ).ToString("yyyyMMddHH");
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var json = await output;
            await error;
            if (process.ExitCode != 0)
                throw new IOException("GPU adapter query failed.");
            return System.Text.Json.JsonSerializer.Deserialize<StatsGpuAdapter[]>(json) ?? [];
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }
    }
}
