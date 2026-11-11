using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
        var disks = Stack();
        var continuous = new CheckBox
        {
            Content = Ui("Continuous sampling"),
            IsChecked = session.Store.Config.Settings.ContinuousStatsSampling,
        };
        var chart = new global::Avalonia.Controls.Shapes.Polyline
        {
            Height = 100,
            Stroke = Accent,
            StrokeThickness = 2,
        };
        void Update()
        {
            cpu.Text = telemetry.Cpu is double load ? $"{load:N1}%" : Ui("Unavailable");
            memory.Text = telemetry.Memory;
            chart.Points = new Points(
                telemetry.History.Select((value, index) => new Point(index * 7, 100 - value))
            );
            graphics.Children.Clear();
            foreach (var gpu in telemetry.Graphics)
                graphics.Children.Add(T(gpu.Name + (gpu.Usage >= 0 ? $" · {gpu.Usage:N1}%" : "")));
            if (telemetry.Graphics.Count == 0)
                graphics.Children.Add(T("GPU telemetry is unavailable on this host."));
        }
        continuous.IsCheckedChanged += async (_, _) =>
        {
            session.Store.Config.Settings.ContinuousStatsSampling = continuous.IsChecked == true;
            await session.Save();
        };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.TotalSize > 0))
            disks.Children.Add(
                Card(
                    Stack(
                        T(drive.Name),
                        T(
                            $"{drive.AvailableFreeSpace / 1073741824.0:N1} / {drive.TotalSize / 1073741824.0:N1} GiB"
                        )
                    )
                )
            );
        var body = Stack(
            continuous,
            T(System.Runtime.InteropServices.RuntimeInformation.OSDescription),
            Card(Stack(T("CPU usage", 18, true), cpu, chart)),
            Card(Stack(T("Memory", 18, true), memory)),
            Card(Stack(T("Graphics", 18, true), graphics)),
            disks
        );
        body.AttachedToVisualTree += (_, _) =>
        {
            telemetry.Updated += Update;
            telemetry.Start(
                action => Dispatcher.UIThread.Post(action),
                Path.Combine(session.Store.Root, "cache", "gpu-adapters.json"),
                Verifying
            );
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
                                Open("https://github.com/com-in/CMCLauncher");
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
                                Open("https://github.com/com-in/CMCLauncher/blob/main/LICENSE");
                                return Task.CompletedTask;
                            }
                        )
                    )
                )
            ),
            Card(
                Stack(
                    T("Launcher updates", 18, true),
                    Btn("Check for updates", () => CheckDesktopUpdates(true))
                )
            )
        );
}
