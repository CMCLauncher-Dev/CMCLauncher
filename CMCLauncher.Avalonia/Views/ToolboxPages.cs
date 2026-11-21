using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control ToolboxPage()
    {
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var (title, description, target, glyph) in new[]
        {
            ("frpc", "Manage port forwarding connections.", "frpc", "M4,5 H20 V15 H4 Z M8,20 H16 M12,15 V20"),
            ("Terracotta", "Create or join a Minecraft LAN room.", "terracotta", "M12,3 L21,8 V16 L12,21 L3,16 V8 Z M3,8 L12,13 L21,8 M12,13 V21"),
            ("MinecraftPFP", "Create a profile picture from a Minecraft skin and export it as PNG.", "pfp", "M3,4 H21 V20 H3 Z M4,17 L9,12 L13,16 L16,13 L20,17 M7,8 H9"),
            ("Data pack generator", "Create recipes, loot tables, advancements and functions.", "datapack-generator", "M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M9,12 H16 M9,16 H16"),
            ("Command generator", "Build Minecraft Java Edition commands with a live preview.", "command-generator", "M3,5 H21 V19 H3 Z M7,9 L10,12 L7,15 M12,15 H17"),
        })
        {
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
            };
            var icon = NavigationIcon(glyph);
            icon.Width = icon.Height = 20;
            icon.Stroke = Accent;
            content.Children.Add(icon);

            var text = Stack(T(title, 16, true), T(description, 13));
            text.Spacing = 4;
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            var arrow = NavigationIcon("M9,5 L16,12 L9,19");
            arrow.Width = arrow.Height = 12;
            Grid.SetColumn(arrow, 2);
            content.Children.Add(arrow);

            var open = Btn(title, () => Navigate(target));
            open.Tag = "toolbox-open:" + target;
            open.Content = content;
            open.Padding = new Thickness(16, 14);
            open.MinHeight = 96;
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            open.VerticalAlignment = VerticalAlignment.Stretch;
            open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            open.Background = Brushes.Transparent;
            open.BorderThickness = new Thickness(0);
            global::Avalonia.Automation.AutomationProperties.SetName(open, Ui(title));
            var card = Card(open);
            card.Padding = new Thickness(0);
            cards.Children.Add(card);
        }

        var columns = 0;
        void ArrangeCards(double width)
        {
            var count = width < 480 ? 1 : width < 820 ? 2 : 3;
            if (columns == count)
                return;
            columns = count;
            cards.ColumnDefinitions.Clear();
            cards.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++)
                cards.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            for (var i = 0; i < cards.Children.Count; i++)
            {
                if (i % columns == 0)
                    cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(cards.Children[i], i % columns);
                Grid.SetRow(cards.Children[i], i / columns);
            }
        }
        ArrangeCards(double.PositiveInfinity);
        cards.SizeChanged += (_, e) => ArrangeCards(e.NewSize.Width);

        if (!LauncherPlatform.Current.SupportsMemoryOptimization)
            return cards;

        var utilities = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var heading = T("Small tools", 16, true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        utilities.Children.Add(heading);

        var optimize = Btn("Memory optimization", OptimizeMemoryNow);
        optimize.Tag = "toolbox-optimize-memory";
        optimize.VerticalAlignment = VerticalAlignment.Center;
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var optimizeIcon = NavigationIcon("M19,10 A7,7 0 1 0 18,16 M19,5 V10 H14");
        optimizeIcon.Width = optimizeIcon.Height = 14;
        label.Children.Add(optimizeIcon);
        label.Children.Add(T("Memory optimization"));
        optimize.Content = label;
        global::Avalonia.Automation.AutomationProperties.SetName(optimize, Ui("Memory optimization"));
        Grid.SetColumn(optimize, 1);
        utilities.Children.Add(optimize);

        var utilitiesCard = Card(utilities);
        utilitiesCard.Padding = new Thickness(16, 8, 12, 8);
        utilitiesCard.CornerRadius = new CornerRadius(8);
        return Stack(utilitiesCard, cards);
    }

    async Task OptimizeMemoryNow()
    {
        MemoryOptimizationResult result = default;
        var completed = await session.Run("Optimizing memory", async ct =>
        {
            ShowToast(Ui("Optimizing memory"));
            result = await Task.Run(() => LauncherPlatform.Current.OptimizeMemory(ct), ct);
            ct.ThrowIfCancellationRequested();
        }, showSuccessToast: false);
        if (!completed)
            return;

        session.Log("INFO", $"Memory optimization completed: {result.CompletedOperations} · {result.AvailableBeforeMb} → {result.AvailableAfterMb} MiB");
        var language = session.Store.Config.Settings.LauncherLanguage;
        var message = result.AvailableGainMb is long gain && result.AvailableAfterMb is long available
            ? session.Locales.Format(language, "Available memory increased by {0:N0} MiB. Available now: {1:N0} MiB.", gain, available)
            : session.Locales.Format(language, "Memory optimization finished. System operations completed: {0}.", result.SystemOperationsCompleted);
        ShowToast(Ui("Memory optimization complete") + " · " + message);
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
