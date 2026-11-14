using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using CMCLauncher.Avalonia.ViewModels;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    readonly SystemStatusState telemetry = new();

    Control StatsPage()
    {
        var cpu = T("");
        var memory = T("");
        var graphics = Stack();
        var process = T("");
        var runtime = T("");
        var budget = T("");
        var library = T("");
        var uptime = T("");
        var disk = T("");
        var diskBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 5,
        };
        var continuous = new ToggleSwitch
        {
            Content = Ui("Continuous sampling"),
            IsChecked = session.Store.Config.Settings.ContinuousStatsSampling,
        };
        (Control View, global::Avalonia.Controls.Shapes.Polyline Line) Trend()
        {
            var line = new global::Avalonia.Controls.Shapes.Polyline
            {
                Stroke = Accent,
                StrokeThickness = 2,
            };
            var canvas = new Canvas
            {
                Width = 600,
                Height = 100,
                ClipToBounds = true,
            };
            foreach (var level in new[] { 0d, 25, 50, 75, 100 })
                canvas.Children.Add(
                    new global::Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = new Point(0, level),
                        EndPoint = new Point(600, level),
                        Stroke = borderBrush,
                        StrokeThickness = .5,
                    }
                );
            canvas.Children.Add(line);
            return (
                new Viewbox
                {
                    Height = 100,
                    Stretch = global::Avalonia.Media.Stretch.Fill,
                    Child = canvas,
                },
                line
            );
        }
        void Draw(global::Avalonia.Controls.Shapes.Polyline line, IEnumerable<double> samples)
        {
            var points = samples.TakeLast(60).ToArray();
            line.Points = new Points(
                points.Select(
                    (value, index) => new Point(index * 600d / 59, 100 - Math.Clamp(value, 0, 100))
                )
            );
        }
        Control StatRow(string title, TextBlock value)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12,
            };
            row.Children.Add(T(title, 12));
            value.MaxLines = 1;
            value.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }
        var cpuTrend = Trend();
        var memoryTrend = Trend();
        var diskTrend = Trend();
        var diskPct = T(Ui("Unavailable"));
        var networkDown = T(Ui("Unavailable"));
        var networkUp = T(Ui("Unavailable"));
        var graphicsHistory = new Dictionary<string, Queue<double>>(StringComparer.Ordinal);
        void Update()
        {
            cpu.Text = telemetry.Cpu is double load ? $"{load:N1}%" : Ui("Unavailable");
            memory.Text = telemetry.Memory.Length > 0 ? telemetry.Memory : Ui("Unavailable");
            Draw(cpuTrend.Line, telemetry.History);
            Draw(memoryTrend.Line, telemetry.MemoryHistory);
            graphics.Children.Clear();
            foreach (var gpu in telemetry.Graphics)
            {
                var panel = Stack(
                    T(gpu.Name, 15, true),
                    T(gpu.Usage >= 0 ? $"{gpu.Usage:N1}%" : Ui("Unavailable"))
                );
                if (gpu.Usage >= 0)
                {
                    if (!graphicsHistory.TryGetValue(gpu.Name, out var history))
                        graphicsHistory[gpu.Name] = history = new();
                    history.Enqueue(gpu.Usage);
                    while (history.Count > 60)
                        history.Dequeue();
                    var trend = Trend();
                    Draw(trend.Line, history);
                    panel.Children.Add(trend.View);
                }
                graphics.Children.Add(panel);
            }
            if (telemetry.Graphics.Count == 0)
                graphics.Children.Add(T("GPU telemetry is unavailable on this host."));
            Draw(diskTrend.Line, telemetry.DiskHistory);
            if (telemetry.Network is { } network)
            {
                networkDown.Text =
                    network.DownloadBytesPerSecond is { } down
                        ? FormatRate(down)
                        : Ui("Unavailable");
                networkUp.Text =
                    network.UploadBytesPerSecond is { } up
                        ? FormatRate(up)
                        : Ui("Unavailable");
            }
            else
            {
                networkDown.Text = Ui("Unavailable");
                networkUp.Text = Ui("Unavailable");
            }
            using var current = Process.GetCurrentProcess();
            process.Text = $"{current.WorkingSet64 / 1048576d:N1} MiB · PID {current.Id}";
            runtime.Text = (DateTime.Now - current.StartTime).ToString(@"d\.hh\:mm\:ss");
            uptime.Text = TimeSpan
                .FromMilliseconds(Environment.TickCount64)
                .ToString(@"d\.hh\:mm\:ss");
            budget.Text = $"{GameMemory.Resolve(session.Store.Config.Settings):N0} MB";
            library.Text = session.Store.Config.Instances.Count + " " + Ui("versions");
            try
            {
                var root = Path.GetFullPath(session.Store.MinecraftRoot);
                var drive = DriveInfo
                    .GetDrives()
                    .Where(d =>
                        d.IsReady
                        && root.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal)
                    )
                    .OrderByDescending(d => d.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                if (drive is { TotalSize: > 0 })
                {
                    disk.Text =
                        $"{drive.AvailableFreeSpace / 1073741824d:N1} / {drive.TotalSize / 1073741824d:N1} GiB";
                    diskBar.Value =
                        100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                    diskPct.Text = $"{diskBar.Value:N1}%";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                disk.Text = Ui("Unavailable");
                diskPct.Text = Ui("Unavailable");
            }
        }
        var cards = new[]
        {
            Card(Stack(T("CPU usage", 18, true), cpu, cpuTrend.View)),
            Card(Stack(T("Memory usage", 18, true), memory, memoryTrend.View)),
            Card(Stack(T("Graphics", 18, true), graphics)),
            Card(
                Stack(
                    T("Disk activity", 18, true),
                    diskTrend.View,
                    StatRow(Ui("Game storage"), diskPct)
                )
            ),
            Card(
                Stack(
                    T("Network", 18, true),
                    StatRow(Ui("Download"), networkDown),
                    StatRow(Ui("Upload"), networkUp)
                )
            ),
            Card(
                Stack(
                    T("System environment", 18, true),
                    T(System.Runtime.InteropServices.RuntimeInformation.OSDescription),
                    T(
                        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                            + " · "
                            + Environment.ProcessorCount
                            + " "
                            + Ui("Logical processors")
                    ),
                    T(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription),
                    StatRow("System uptime", uptime)
                )
            ),
            Card(
                Stack(
                    T("Launcher runtime", 18, true),
                    StatRow("Launcher process", process),
                    StatRow("Running time", runtime),
                    StatRow("Game memory budget", budget),
                    StatRow("Library summary", library)
                )
            ),
            Card(
                Stack(
                    T("Game storage", 18, true),
                    T(session.Store.MinecraftRoot, 12),
                    disk,
                    diskBar,
                    Btn(
                        "Open folder",
                        () =>
                        {
                            Directory.CreateDirectory(session.Store.MinecraftRoot);
                            Open(session.Store.MinecraftRoot);
                            return Task.CompletedTask;
                        }
                    )
                )
            ),
        };
        var dashboard = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
            RowSpacing = 12,
            Tag = "stats-dashboard",
        };
        foreach (var card in cards)
        {
            dashboard.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            dashboard.Children.Add(card);
        }
        void Layout()
        {
            var columns = dashboard.Bounds.Width >= 700 ? 2 : 1;
            var rows = (cards.Length + columns - 1) / columns;
            while (dashboard.RowDefinitions.Count > rows)
                dashboard.RowDefinitions.RemoveAt(dashboard.RowDefinitions.Count - 1);
            while (dashboard.RowDefinitions.Count < rows)
                dashboard.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < cards.Length; i++)
            {
                Grid.SetColumn(cards[i], i % columns);
                Grid.SetRow(cards[i], i / columns);
                Grid.SetColumnSpan(cards[i], columns == 1 ? 2 : 1);
            }
        }
        dashboard.SizeChanged += (_, _) => Layout();
        Layout();
        void Start() =>
            telemetry.Start(
                action => Dispatcher.UIThread.Post(action),
                Path.Combine(session.Store.Root, "cache", "gpu-adapters.json"),
                Verifying
            );
        continuous.IsCheckedChanged += async (_, _) =>
        {
            session.Store.Config.Settings.ContinuousStatsSampling = continuous.IsChecked == true;
            if (continuous.IsChecked == true)
                Start();
            else
                telemetry.Stop();
            await session.Save();
        };
        var body = Stack(continuous, dashboard);
        body.AttachedToVisualTree += (_, _) =>
        {
            telemetry.Updated += Update;
            if (continuous.IsChecked == true)
                Start();
            Update();
        };
        body.DetachedFromVisualTree += (_, _) =>
        {
            telemetry.Updated -= Update;
            if (!session.Store.Config.Settings.ContinuousStatsSampling)
                telemetry.Stop();
        };
        return body;
    }

    static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond > 1024d * 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024 / 1024:N2} GB/s"
            : bytesPerSecond > 1024 * 1024
                ? $"{bytesPerSecond / 1024 / 1024:N1} MB/s"
                : $"{bytesPerSecond / 1024:N1} KB/s";

    Control AboutPage() =>
        Stack(
            Card(
                Stack(
                    T("CMCLauncher", 26, true),
                    T(LauncherUpdates.CurrentVersion),
                    T(
                        "Avalonia · .NET · "
                            + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier
                    ),
                    Row(
                        Btn(
                            "GitHub",
                            () =>
                            {
                                Open("https://github.com/CMCLauncher-Dev/CMCLauncher");
                                return Task.CompletedTask;
                            }
                        ),
                        Btn(
                            "Website",
                            () =>
                            {
                                Open("https://cmcl.ctfun.top/");
                                return Task.CompletedTask;
                            }
                        ),
                        Btn(
                            "License",
                            () =>
                            {
                                Open(
                                    "https://github.com/CMCLauncher-Dev/CMCLauncher/blob/main/LICENSE"
                                );
                                return Task.CompletedTask;
                            }
                        )
                    )
                )
            ),
            AcknowledgementCards(Ui("Contributors"), AboutAcknowledgements.Contributors, true),
            AcknowledgementCards(
                Ui("Open-source projects and services"),
                AboutAcknowledgements.Projects,
                false
            ),
            Card(
                Stack(
                    T("Launcher updates", 18, true),
                    Btn("Check for updates", () => CheckDesktopUpdates(true))
                )
            )
        );

    Control AcknowledgementCards(string title, IReadOnlyList<Acknowledgement> entries, bool compact)
    {
        var cards = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(compact ? "*,*,*,*" : "*,*,*"),
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        var columns = compact ? 4 : 3;
        for (var i = 0; i < entries.Count; i++)
        {
            if (i % columns == 0)
                cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var entry = entries[i];
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 8,
                MinHeight = compact ? 24 : 30,
            };
            content.Children.Add(
                new Image
                {
                    Source = new global::Avalonia.Media.Imaging.Bitmap(
                        Path.Combine(AppContext.BaseDirectory, "assets", "about", entry.Image)
                    ),
                    Width = compact ? 24 : 28,
                    Height = compact ? 24 : 28,
                }
            );
            var label = T("", 14, true);
            label.Text = entry.Name;
            label.TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap;
            label.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var link = Btn(
                "",
                () =>
                {
                    Open(entry.Url);
                    return Task.CompletedTask;
                }
            );
            link.Content = content;
            link.Padding = new global::Avalonia.Thickness(8, 6);
            link.MinHeight = compact ? 34 : 40;
            link.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            link.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            ToolTip.SetTip(link, entry.Name);
            Grid.SetColumn(link, i % columns);
            Grid.SetRow(link, i / columns);
            cards.Children.Add(link);
            link.DetachedFromVisualTree += (_, _) =>
            {
                if (((Image)content.Children[0]).Source is IDisposable bitmap)
                    bitmap.Dispose();
            };
        }
        return Card(Stack(T(title, 18, true), cards));
    }
}