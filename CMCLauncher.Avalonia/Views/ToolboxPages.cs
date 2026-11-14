using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control ToolboxPage()
    {
        var cards = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 12,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        foreach (
            var (title, description, target) in new[]
            {
                ("frpc", "Manage port forwarding connections.", "frpc"),
                ("Terracotta", "Create or join a Minecraft LAN room.", "terracotta"),
                (
                    "MinecraftPFP",
                    "Create a profile picture from a Minecraft skin and export it as PNG.",
                    "pfp"
                ),
            }
        )
        {
            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 6,
            };
            content.Children.Add(T(title, 16, true));
            var summary = T(description);
            Grid.SetRow(summary, 1);
            content.Children.Add(summary);
            var open = Btn("Open", () => Navigate(target), true);
            open.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            Grid.SetRow(open, 2);
            content.Children.Add(open);
            var card = Card(content);
            card.Padding = new global::Avalonia.Thickness(11, 7);
            Grid.SetColumn(card, cards.Children.Count);
            cards.Children.Add(card);
        }
        return cards;
    }

    bool terracottaBusy,
        terracottaInviteOpen;
    string terracottaOperation = "Starting Terracotta…";
    Action? refreshTerracotta;

    Control TerracottaPage()
    {
        var client = session.Terracotta;
        refreshTerracotta = null;
        if (client.Installed == null)
        {
            return Stack(
                Btn("Back to toolbox", () => Navigate("toolbox")),
                Card(
                    Stack(
                        T("Terracotta is not downloaded.", 18, true),
                        T(
                            "The component is downloaded from the official release only when you click Download. The launcher does not include it."
                        ),
                        Btn(
                            "Download Terracotta",
                            async () =>
                            {
                                await session.Run("Download Terracotta", client.Install);
                                await Navigate("terracotta");
                            },
                            true
                        )
                    )
                )
            );
        }
        var heading = T("Multiplayer component ready", 15, true);
        var progress = new ProgressBar
        {
            Height = 4,
            MinHeight = 4,
            Foreground = Accent,
            Background = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.Parse(Light ? "#D9DEE3" : "#454A54")
            ),
            Minimum = 0,
            Maximum = 100,
            Value = 100,
        };
        var scanHint = T(
            "Start Minecraft, enter a singleplayer world, press Esc and choose Open to LAN, then Start LAN World."
        );
        Control? choices = null;
        StackPanel? actions = null;
        var status = T("");
        var code = Input("", "Room code / server address");
        code.IsReadOnly = true;
        var codeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };
        codeRow.Children.Add(code);
        Button? copy = null;
        copy = Btn(
            "Copy",
            async () =>
            {
                if (!client.Connected || string.IsNullOrWhiteSpace(code.Text))
                    return;
                if (Clipboard == null)
                    throw new IOException("Clipboard is unavailable.");
                await Clipboard.SetTextAsync(code.Text);
                copy!.Content = Ui("Copied");
            }
        );
        Grid.SetColumn(copy, 1);
        codeRow.Children.Add(copy);
        var members = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        var membersCard = Card(Stack(T("Players", 18, true), members));
        string[] memberNames = [];
        void Update()
        {
            var state = client.State?["state"]?.GetValue<string>();
            heading.Text = Ui(
                terracottaBusy ? terracottaOperation
                : client.Connected && state != "waiting"
                    ? (
                        state == "host-scanning"
                            ? "Scanning for LAN worlds…"
                            : TerracottaStatus.Description(client.State)
                    )
                : "Multiplayer component ready"
            );
            progress.IsIndeterminate =
                terracottaBusy || client.Connected && TerracottaStatus.IsPending(state);
            progress.IsVisible = state != "exception" || terracottaBusy;
            scanHint.IsVisible = client.Connected && state == "host-scanning";
            status.Text = Ui(client.Error ?? "");
            status.IsVisible = client.Error != null;
            if (choices != null)
                choices.IsVisible = !terracottaBusy && (!client.Connected || state == "waiting");
            if (actions != null)
                actions.IsVisible = client.Connected && state != "waiting" && !terracottaBusy;
            var previousCode = code.Text;
            code.Text =
                client.State?["room"]?.GetValue<string>()
                ?? client.State?["url"]?.GetValue<string>()
                ?? "";
            code.IsVisible = client.Connected && !string.IsNullOrEmpty(code.Text);
            codeRow.IsVisible = code.IsVisible;
            if (previousCode != code.Text)
                copy.Content = Ui("Copy");
            var names = client.Connected ? TerracottaStatus.Members(client.State) : [];
            membersCard.IsVisible = names.Length > 0;
            if (!names.SequenceEqual(memberNames))
            {
                memberNames = names;
                members.Children.Clear();
                members.RowDefinitions.Clear();
                for (var i = 0; i < names.Length; i++)
                {
                    if (i % 3 == 0)
                        members.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var label = T("", 14, true);
                    label.Text = names[i];
                    label.TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap;
                    label.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
                    ToolTip.SetTip(label, names[i]);
                    var content = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                        ColumnSpacing = 10,
                        MinHeight = 36,
                    };
                    content.Children.Add(T("●", 18));
                    Grid.SetColumn(label, 1);
                    content.Children.Add(label);
                    var card = Card(content);
                    card.Padding = new global::Avalonia.Thickness(11, 7);
                    Grid.SetColumn(card, i % 3);
                    Grid.SetRow(card, i / 3);
                    members.Children.Add(card);
                }
            }
        }
        var ready = Stack(heading, progress, scanHint, status, codeRow);
        ready.Spacing = 6;
        actions = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
        };
        actions.Children.Add(
            Btn("Leave room", () => TerracottaOperation("Leaving room…", () => client.Leave()))
        );
        actions.Children.Add(
            Btn(
                "Stop Terracotta",
                () => TerracottaOperation("Stopping Terracotta…", () => client.Stop())
            )
        );
        ready.Children.Add(actions);
        var statusCard = Card(ready);
        statusCard.Padding = new global::Avalonia.Thickness(12, 10);
        var body = Stack(
            Btn("Back to toolbox", () => Navigate("toolbox")),
            T("Multiplayer lobby", 20, true),
            statusCard,
            membersCard
        );
        {
            var lobby = Stack(
                T(
                    "Terracotta is third-party open-source software. For issues with its networking component, contact the upstream project."
                ),
                T(
                    "Connection quality depends on the participants’ networks. Use compatible game and mod versions and follow applicable local laws."
                )
            );
            foreach (
                var (title, subtitle, view) in new[]
                {
                    (
                        "I want to host",
                        "Create a room and share its invitation code with friends.",
                        "host"
                    ),
                    (
                        "I want to join",
                        "Enter the host’s invitation code to join their world.",
                        "guest"
                    ),
                }
            )
            {
                var button = Btn(
                    title,
                    async () =>
                    {
                        if (terracottaBusy || terracottaInviteOpen)
                            return;
                        if (view == "host")
                            await TerracottaOperation(
                                "Starting Terracotta…",
                                async () =>
                                {
                                    await client.Start();
                                    await client.CreateRoom(session.Account?.Name ?? "Player");
                                }
                            );
                        else
                            await JoinTerracottaDialog();
                    }
                );
                button.Padding = new global::Avalonia.Thickness(12, 8);
                button.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = global::Avalonia
                    .Layout
                    .HorizontalAlignment
                    .Stretch;
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 12,
                };
                row.Children.Add(T(view == "host" ? "▣" : "⊕", 20));
                var labels = Stack(T(title, 16, true), T(subtitle, 12));
                labels.Spacing = 2;
                Grid.SetColumn(labels, 1);
                row.Children.Add(labels);
                var arrow = T("→", 18);
                Grid.SetColumn(arrow, 2);
                row.Children.Add(arrow);
                button.Content = row;
                lobby.Children.Add(button);
            }
            if (OperatingSystem.IsMacOS())
                lobby.Children.Add(
                    Btn("Install macOS application", () => client.InstallMacApplication(default))
                );
            choices = Card(lobby);
            body.Children.Add(choices);
        }
        body.Children.Add(
            Card(
                Stack(
                    T("Terracotta · © burningtnt · AGPL-3.0-or-later"),
                    Btn(
                        "Source and license",
                        () =>
                        {
                            Process
                                .Start(
                                    CMCLauncher.Platform.PlatformCommand.SystemEnvironment(
                                        new ProcessStartInfo(TerracottaManager.SourceUrl)
                                        {
                                            UseShellExecute = true,
                                        }
                                    )
                                )
                                ?.Dispose();
                            return Task.CompletedTask;
                        }
                    )
                )
            )
        );
        refreshTerracotta = Update;
        void Changed() => Dispatcher.UIThread.Post(Update);
        body.AttachedToVisualTree += (_, _) =>
        {
            client.Changed += Changed;
            Update();
        };
        body.DetachedFromVisualTree += (_, _) =>
        {
            client.Changed -= Changed;
            if (refreshTerracotta == Update)
                refreshTerracotta = null;
        };
        Update();
        return body;
    }

    async Task TerracottaOperation(string operation, Func<Task> action)
    {
        if (terracottaBusy)
            return;
        terracottaBusy = true;
        terracottaOperation = operation;
        refreshTerracotta?.Invoke();
        try
        {
            await action();
        }
        finally
        {
            terracottaBusy = false;
            if (route == "terracotta")
                refreshTerracotta?.Invoke();
        }
    }

    async Task JoinTerracottaDialog()
    {
        if (terracottaInviteOpen || terracottaBusy)
            return;
        terracottaInviteOpen = true;
        string? invitation = null;
        try
        {
            var input = Input("", "Paste the invitation code");
            var dialog = new Window
            {
                Title = Ui("Join room"),
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                RequestedThemeVariant = RequestedThemeVariant,
                Background = Background,
                Foreground = Foreground,
            };
            PrepareDialog(dialog);
            var confirm = Btn(
                "Join room",
                () =>
                {
                    dialog.Close(true);
                    return Task.CompletedTask;
                },
                true
            );
            confirm.IsEnabled = false;
            confirm.IsDefault = true;
            dialog.Opened += (_, _) => input.Focus();
            input.TextChanged += (_, _) =>
            {
                try
                {
                    _ = TerracottaManager.Parameter(input.Text ?? "");
                    confirm.IsEnabled = true;
                }
                catch (IOException)
                {
                    confirm.IsEnabled = false;
                }
            };
            var panel = Stack(
                T("Join room", 20, true),
                Field("Room code", input),
                Row(
                    confirm,
                    Btn(
                        "Cancel",
                        () =>
                        {
                            dialog.Close(false);
                            return Task.CompletedTask;
                        }
                    )
                )
            );
            panel.Margin = new global::Avalonia.Thickness(22);
            dialog.Content = panel;
            if (await dialog.ShowDialog<bool>(this))
                invitation = input.Text?.Trim();
        }
        finally
        {
            terracottaInviteOpen = false;
        }
        if (invitation == null)
            return;
        _ = TerracottaManager.Parameter(invitation);
        await TerracottaOperation(
            "Connecting to room…",
            async () =>
            {
                await session.Terracotta.Start();
                await session.Terracotta.JoinRoom(invitation, session.Account?.Name ?? "Player");
            }
        );
    }

    Control TerracottaSettingsCard()
    {
        var panel = Stack(
            T("Terracotta", 18, true),
            T(
                session.Terracotta.Installed is { } value
                    ? "Terracotta " + value.Version
                    : "Terracotta is not downloaded."
            )
        );
        var actions = Row();
        panel.Children.Add(actions);
        actions.Children.Add(
            Btn(
                "Download Terracotta",
                async () =>
                {
                    await session.Run("Download Terracotta", session.Terracotta.Install);
                    await Navigate("settings");
                },
                true
            )
        );
        if (session.Terracotta.Installed != null || Directory.Exists(session.Terracotta.Root))
            actions.Children.Add(
                Btn(
                    "Remove Terracotta",
                    async () =>
                    {
                        if (terracottaBusy)
                            throw new IOException(
                                Ui("Wait for the current Terracotta operation to finish.")
                            );
                        if (
                            !await Confirm(
                                "Remove Terracotta?",
                                "This removes the downloaded component. You can download it again from the toolbox."
                            )
                        )
                            return;
                        await session.Terracotta.Stop();
                        await session.Run("Remove Terracotta", session.Terracotta.Remove);
                        await Navigate("settings");
                    }
                )
            );
        var card = Card(panel);
        card.Padding = new global::Avalonia.Thickness(12);
        return card;
    }
}
