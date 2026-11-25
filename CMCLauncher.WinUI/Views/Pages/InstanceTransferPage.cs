using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void InstanceTransferPage()
    {
        Header("Instance transfer", "Utilities");
        Border PanelCard(Panel child) => CreateCard(child, new Thickness(16), 8);
        var body = new StackPanel { Spacing = 14 };
        var content = new StackPanel { Spacing = 12 };
        var step = Text("", 19, true);
        var hint = Text("");
        var status = Text("");
        var progress = new ProgressBar { IsIndeterminate = true };
        progress.Visibility = Visibility.Collapsed;
        var footer = new StackPanel { Spacing = 8 };
        CancellationTokenSource? active = null;
        CancellationTokenSource? scanning = null;
        System.Action? previous = null;

        Button Button(string label, Func<Task> run, bool primary = false) =>
            Action(label, (_, _) => run(), primary);
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
            label.Children.Add(Text(title, 16, true));
            label.Children.Add(Text(description));
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
        cancel.Visibility = Visibility.Collapsed;
        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(step);
        heading.Children.Add(hint);
        footer.Children.Add(progress);
        footer.Children.Add(status);
        footer.Children.Add(cancel);
        footer.Children.Add(back);
        body.Children.Add(PanelCard(heading));
        body.Children.Add(PanelCard(content));
        body.Children.Add(footer);

        void Stage(string title, string description, System.Action? goBack)
        {
            scanning?.Cancel();
            step.Text = Ui(title);
            hint.Text = Ui(description);
            status.Text = "";
            previous = goBack;
            back.Visibility = goBack == null ? Visibility.Collapsed : Visibility.Visible;
            content.Children.Clear();
        }

        async Task Run(Func<CancellationToken, Task> work)
        {
            if (active != null)
                return;
            using var operation = new CancellationTokenSource();
            active = operation;
            back.IsEnabled = false;
            progress.Visibility = cancel.Visibility = Visibility.Visible;
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
                status.Text = Ui("Transfer failed.") + " " + ex.Message;
            }
            finally
            {
                active = null;
                back.IsEnabled = true;
                progress.Visibility = cancel.Visibility = Visibility.Collapsed;
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
            foreach (var instance in store.Config.Instances)
            {
                var include = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(include, instance.Name);
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
                content.Children.Add(PanelCard(row));
                selections.Add((instance, include, choices));
            }
            if (selections.Count == 0)
                content.Children.Add(Text("No instances available to transfer."));

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
                            "Enter this pairing code on the receiving device.",
                            () => SendSelection(local)
                        );
                        var pending = Text("Preparing instance transfer…");
                        content.Children.Add(pending);
                        await Run(async ct =>
                        {
                            await Task.Run(
                                () =>
                                    InstanceTransfer.Send(
                                        store,
                                        selected,
                                        local,
                                        (address, code) =>
                                        {
                                            DispatcherQueue.TryEnqueue(() =>
                                            {
                                                if (ct.IsCancellationRequested)
                                                    return;
                                                content.Children.Clear();
                                                content.Children.Add(
                                                    Text("Sender address (IPv4:port)")
                                                );
                                                content.Children.Add(
                                                    new TextBox
                                                    {
                                                        Text = address,
                                                        IsReadOnly = true,
                                                    }
                                                );
                                                content.Children.Add(
                                                    Text("Pairing code", 16, true)
                                                );
                                                content.Children.Add(
                                                    new TextBox { Text = code, IsReadOnly = true }
                                                );
                                                status.Text = Ui(
                                                    "Waiting for the receiving device…"
                                                );
                                            });
                                        },
                                        ct
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
            var left = PanelCard(local);
            var right = PanelCard(hotspot);
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            content.Children.Add(columns);

            local.Children.Add(Text("Local network", 16, true));
            var scanState = Text("");
            var indicator = new ProgressBar { IsIndeterminate = true };
            var results = new StackPanel { Spacing = 6 };
            var retry = Button("Search again", Scan);
            local.Children.Add(scanState);
            local.Children.Add(indicator);
            local.Children.Add(results);
            local.Children.Add(retry);

            hotspot.Children.Add(Text("Hotspot connection", 16, true));
            hotspot.Children.Add(
                Text("Connect both devices to the same hotspot in system Wi-Fi settings.")
            );
            hotspot.Children.Add(
                Text("Use the address and pairing code shown on the sending device.")
            );
            hotspot.Children.Add(
                Button(
                    "Enter connection details",
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
                indicator.Visibility = Visibility.Visible;
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
                    scanState.Text = Ui("Device scan failed.") + " " + ex.Message;
                }
                finally
                {
                    if (scanning == operation)
                        scanning = null;
                    indicator.Visibility = Visibility.Collapsed;
                }
            }
        }

        void ReceivePairing(bool local, string? selectedAddress = null)
        {
            Stage(
                "Pair the devices",
                local
                    ? "Enter the pairing code shown on the sending device."
                    : "Enter the address and pairing code shown on the sending device.",
                () => Connection(false)
            );
            var address = new TextBox { PlaceholderText = Ui("Sender address (IPv4:port)") };
            var code = new TextBox { PlaceholderText = Ui("Pairing code") };
            address.Text = selectedAddress ?? "";
            var receive = Button(
                "Receive instance",
                async () =>
                {
                    if (active != null)
                        return;
                    if (
                        string.IsNullOrWhiteSpace(address.Text)
                        || string.IsNullOrWhiteSpace(code.Text)
                    )
                    {
                        status.Text = Ui("Enter the sender address and pairing code.");
                        return;
                    }
                    Stage(
                        "Transferring instance",
                        "Keep both devices connected until the transfer finishes.",
                        () => ReceivePairing(local, address.Text)
                    );
                    content.Children.Add(
                        Text(
                            "The receiver may need internet access to download Minecraft and the loader."
                        )
                    );
                    await Run(async ct =>
                    {
                        status.Text = Ui("Receiving instance data…");
                        var imported = await InstanceTransfer.Receive(
                            store,
                            core,
                            address.Text,
                            code.Text,
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
            content.Children.Add(code);
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
        body.Unloaded += (_, _) =>
        {
            scanning?.Cancel();
            active?.Cancel();
        };
        PageBody.Children.Add(body);
    }
}
