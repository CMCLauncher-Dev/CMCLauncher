using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control InstanceTransferPage()
    {
        var body = new StackPanel { Spacing = 14 };
        var content = new StackPanel { Spacing = 12 };
        var step = T("", 19, true);
        var hint = T("");
        var status = T("");
        var progress = new ProgressBar { IsIndeterminate = true };
        progress.IsVisible = false;
        var footer = new StackPanel { Spacing = 8 };
        CancellationTokenSource? active = null;
        CancellationTokenSource? scanning = null;
        System.Action? previous = null;

        Button Button(string label, Func<Task> run, bool primary = false) =>
            Btn(label, run, primary);
        Button Choice(string title, string description, System.Action next)
        {
            var button = Button(
                title,
                () =>
                {
                    next();
                    return Task.CompletedTask;
                }
            );
            var label = new StackPanel { Spacing = 4 };
            label.Children.Add(T(title, 16, true));
            label.Children.Add(T(description));
            button.Content = label;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            return button;
        }

        var back = Button(
            "Previous step",
            () =>
            {
                previous?.Invoke();
                return Task.CompletedTask;
            }
        );
        var cancel = Button(
            "Cancel transfer",
            () =>
            {
                active?.Cancel();
                return Task.CompletedTask;
            }
        );
        cancel.IsVisible = false;
        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(step);
        heading.Children.Add(hint);
        footer.Children.Add(progress);
        footer.Children.Add(status);
        footer.Children.Add(cancel);
        footer.Children.Add(back);
        body.Children.Add(Card(heading));
        body.Children.Add(Card(content));
        body.Children.Add(footer);

        void Stage(string title, string description, System.Action? goBack)
        {
            scanning?.Cancel();
            step.Text = Ui(title);
            hint.Text = Ui(description);
            status.Text = "";
            previous = goBack;
            back.IsVisible = goBack != null;
            content.Children.Clear();
        }

        async Task Run(Func<CancellationToken, Task> work)
        {
            if (active != null)
                return;
            using var operation = new CancellationTokenSource();
            active = operation;
            back.IsEnabled = false;
            progress.IsVisible = cancel.IsVisible = true;
            try
            {
                await work(operation.Token);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                status.Text = Ui("Transfer cancelled.");
            }
            catch (Exception ex)
            {
                status.Text = Ui("Transfer failed.") + " " + Ui(ex.Message);
            }
            finally
            {
                active = null;
                back.IsEnabled = true;
                progress.IsVisible = cancel.IsVisible = false;
            }
        }

        void Roles()
        {
            Stage("Which device is this?", "Open Instance transfer on both devices.", null);
            content.Children.Add(
                Choice(
                    "This is the receiving device",
                    "Copy an instance from another device.",
                    () => Connection(false)
                )
            );
            content.Children.Add(
                Choice(
                    "This is the sending device",
                    "Copy an instance to another device.",
                    () => Connection(true)
                )
            );
        }

        void Connection(bool sending)
        {
            if (!sending)
            {
                FindDevice();
                return;
            }
            Stage(
                "Connection method",
                "Connect both devices to the same LAN or join the same hotspot in system Wi-Fi settings.",
                Roles
            );
            content.Children.Add(
                Choice(
                    "Local network",
                    "Find the sending device on the local network.",
                    () => Next(true)
                )
            );
            content.Children.Add(
                Choice(
                    "Wi-Fi hotspot",
                    "Connect using the address shown on the sending device.",
                    () => Next(false)
                )
            );

            void Next(bool local)
            {
                SendSelection(local);
            }
        }

        void SendSelection(bool local)
        {
            Stage(
                "Choose instances to transfer",
                "Select one or more instances. Expand each card to choose its content.",
                () => Connection(true)
            );
            var selections =
                new List<(
                    Instance Instance,
                    CheckBox Include,
                    (string[] Paths, CheckBox Check)[] Content
                )>();
            foreach (var instance in session.Store.Config.Instances)
            {
                var error = InstanceTransfer.SelectionError(instance);
                var include = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = error == null,
                };
                global::Avalonia.Automation.AutomationProperties.SetName(include, instance.Name);
                var options = new StackPanel { Spacing = 6 };
                var choices = InstanceTransfer
                    .Content.Select(group =>
                        (
                            group.Paths,
                            Check: new CheckBox { Content = Ui(group.Label), IsChecked = true }
                        )
                    )
                    .ToArray();
                foreach (var choice in choices)
                    options.Children.Add(choice.Check);
                var expander = new Expander
                {
                    Header = instance.ToString(),
                    Content = options,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                );
                row.Children.Add(include);
                Grid.SetColumn(expander, 1);
                row.Children.Add(expander);
                if (error != null)
                {
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var warning = T(error);
                    Grid.SetRow(warning, 1);
                    Grid.SetColumn(warning, 1);
                    row.Children.Add(warning);
                }
                content.Children.Add(Card(row));
                selections.Add((instance, include, choices));
            }
            if (selections.Count == 0)
                content.Children.Add(T("No instances available to transfer."));

            content.Children.Add(
                Button(
                    "Start sending",
                    async () =>
                    {
                        var selected = selections
                            .Where(x => x.Include.IsChecked == true)
                            .Select(x => new InstanceTransfer.Selection(
                                x.Instance,
                                x.Content.Where(y => y.Check.IsChecked == true)
                                    .SelectMany(y => y.Paths)
                                    .ToArray()
                            ))
                            .ToArray();
                        if (selected.Length == 0)
                        {
                            status.Text = Ui("No instance selected.");
                            return;
                        }
                        Stage(
                            "Pair the devices",
                            "Select this device on the receiving device to send a connection request.",
                            () => SendSelection(local)
                        );
                        var pending = T("Preparing instance transfer…");
                        content.Children.Add(pending);
                        await Run(async ct =>
                        {
                            await Task.Run(
                                () =>
                                    InstanceTransfer.Send(
                                        session.Store,
                                        selected,
                                        local,
                                        (address, _) =>
                                        {
                                            Dispatcher.UIThread.Post(() =>
                                            {
                                                if (ct.IsCancellationRequested)
                                                    return;
                                                content.Children.Clear();
                                                content.Children.Add(
                                                    T("Sender address (IPv4:port)")
                                                );
                                                content.Children.Add(
                                                    new TextBox
                                                    {
                                                        Text = address,
                                                        IsReadOnly = true,
                                                    }
                                                );
                                                status.Text = Ui(
                                                    "Waiting for the receiving device…"
                                                );
                                            });
                                        },
                                        ct,
                                        ConfirmConnection
                                    ),
                                ct
                            );
                            Complete(
                                "Instance data sent.",
                                string.Join(
                                    Environment.NewLine,
                                    selected.Select(x => x.Instance.Name)
                                )
                            );
                        });
                        content.Children.Remove(pending);
                    },
                    primary: true
                )
            );
        }

        void FindDevice()
        {
            Stage("Connection method", "Open Instance transfer on both devices.", Roles);
            var local = new StackPanel { Spacing = 10 };
            var hotspot = new StackPanel { Spacing = 10 };
            var columns = new Grid { ColumnSpacing = 12 };
            columns.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            columns.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            var left = Card(local);
            var right = Card(hotspot);
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            content.Children.Add(columns);

            local.Children.Add(T("Local network", 16, true));
            var scanState = T("");
            var indicator = new ProgressBar { IsIndeterminate = true };
            var results = new StackPanel { Spacing = 6 };
            var retry = Button("Search again", Scan);
            local.Children.Add(scanState);
            local.Children.Add(indicator);
            local.Children.Add(results);
            local.Children.Add(retry);

            hotspot.Children.Add(T("Hotspot connection", 16, true));
            hotspot.Children.Add(
                T("Connect both devices to the same hotspot in system Wi-Fi settings.")
            );
            hotspot.Children.Add(T("Use the address shown on the sending device."));
            hotspot.Children.Add(
                Button(
                    "Enter sender address",
                    () =>
                    {
                        ReceivePairing(false);
                        return Task.CompletedTask;
                    }
                )
            );
            _ = Scan();

            async Task Scan()
            {
                using var operation = new CancellationTokenSource();
                if (scanning is { IsCancellationRequested: false })
                    return;
                scanning = operation;
                indicator.IsVisible = true;
                results.Children.Clear();
                scanState.Text = Ui("Scanning for devices on the local network…");
                try
                {
                    var addresses = await InstanceTransfer.Discover(operation.Token);
                    if (operation.IsCancellationRequested)
                        return;
                    scanState.Text = Ui(
                        addresses.Length == 0
                            ? "No sending devices found."
                            : "Select a sending device."
                    );
                    foreach (var address in addresses)
                    {
                        results.Children.Add(
                            Button(
                                address,
                                () =>
                                {
                                    ReceivePairing(true, address);
                                    return Task.CompletedTask;
                                }
                            )
                        );
                    }
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    scanState.Text = Ui("Device scan failed.") + " " + Ui(ex.Message);
                }
                finally
                {
                    if (scanning == operation)
                        scanning = null;
                    indicator.IsVisible = false;
                }
            }
        }

        async Task<bool> ConfirmConnection(string peer, string verification, CancellationToken ct)
        {
            var decision = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            StackPanel? request = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested)
                    return;
                request = new StackPanel { Spacing = 8 };
                request.Children.Add(T("Connection request", 16, true));
                request.Children.Add(new TextBlock { Text = peer });
                request.Children.Add(
                    T("Confirm only if this verification code matches the receiving device.")
                );
                request.Children.Add(new TextBlock { Text = verification, FontSize = 20 });
                request.Children.Add(
                    Button(
                        "Confirm connection",
                        () =>
                        {
                            decision.TrySetResult(true);
                            return Task.CompletedTask;
                        },
                        primary: true
                    )
                );
                request.Children.Add(
                    Button(
                        "Decline connection",
                        () =>
                        {
                            decision.TrySetResult(false);
                            return Task.CompletedTask;
                        }
                    )
                );
                content.Children.Add(request);
            });

            try
            {
                return await decision.Task.WaitAsync(ct);
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (request != null)
                        content.Children.Remove(request);
                });
            }
        }

        void ReceivePairing(bool local, string? selectedAddress = null)
        {
            Stage(
                "Pair the devices",
                local
                    ? "Request a connection and confirm it on the sending device."
                    : "Enter the sender address, then request a connection.",
                () => Connection(false)
            );
            var address = new TextBox { Watermark = Ui("Sender address (IPv4:port)") };
            address.Text = selectedAddress ?? "";
            var receive = Button(
                "Request connection",
                async () =>
                {
                    if (active != null)
                        return;
                    if (string.IsNullOrWhiteSpace(address.Text))
                    {
                        status.Text = Ui("Enter the sender address.");
                        return;
                    }
                    Stage(
                        "Transferring instance",
                        "Keep both devices connected until the transfer finishes.",
                        () => ReceivePairing(local, address.Text)
                    );
                    content.Children.Add(
                        T(
                            "The receiver may need internet access to download Minecraft and the loader."
                        )
                    );
                    await Run(async ct =>
                    {
                        status.Text = Ui("Connecting to the sending device…");
                        var imported = await InstanceTransfer.RequestReceive(
                            session.Store,
                            session.Core,
                            address.Text,
                            verification =>
                            {
                                status.Text = Ui("Waiting for confirmation on the sending device…");
                                content.Children.Add(
                                    T("Compare this verification code with the sending device.")
                                );
                                content.Children.Add(
                                    new TextBlock { Text = verification, FontSize = 20 }
                                );
                            },
                            () => status.Text = Ui("Installing Minecraft and importing content…"),
                            ct
                        );
                        Complete(
                            "Instance received.",
                            string.Join(Environment.NewLine, imported.Select(x => x.Name))
                        );
                    });
                },
                primary: true
            );

            content.Children.Add(address);
            content.Children.Add(receive);
        }

        void Complete(string title, string instance)
        {
            Stage(title, "", null);
            content.Children.Add(new TextBlock { Text = instance, FontSize = 17 });
            content.Children.Add(
                Button(
                    "Done",
                    () =>
                    {
                        Roles();
                        return Task.CompletedTask;
                    },
                    primary: true
                )
            );
        }

        Roles();
        body.DetachedFromVisualTree += (_, _) =>
        {
            scanning?.Cancel();
            active?.Cancel();
        };
        return body;
    }
}
