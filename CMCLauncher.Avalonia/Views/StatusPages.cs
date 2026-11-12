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
            AcknowledgementCards("Contributors", AboutAcknowledgements.Contributors, true),
            AcknowledgementCards(
                "Open-source projects and services",
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
