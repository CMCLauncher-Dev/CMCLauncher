using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    TerracottaManager terracotta = null!;
    TextBlock? terracottaStatus;
    TextBlock? terracottaHeading,
        terracottaScanHint;
    ProgressBar? terracottaProgress;
    Border? terracottaChoices;
    TextBox? terracottaCode;
    Grid? terracottaCodeRow;
    Grid? terracottaMembers;
    Border? terracottaMembersCard;
    string[] terracottaMemberNames = [];
    readonly List<Border> terracottaMemberCards = [];

    void InitializeTerracotta()
    {
        terracotta = new TerracottaManager(store, core);
        startTerracottaHost = async (player, ct) =>
        {
            await terracotta.Start(ct);
            await terracotta.CreateRoom(player, ct);
        };
        terracotta.Output += message => RuntimeLog.Write("TERRACOTTA", message);
        terracotta.Changed += () => DispatcherQueue.TryEnqueue(UpdateTerracotta);
        Closed += (_, _) => terracotta.Dispose();
    }

    void ToolboxPage()
    {
        Header("Toolbox", "Utilities");
        var cards = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < 3; i++)
            cards.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
        foreach (
            var (title, description, route) in new[]
            {
                ("frpc", "Manage port forwarding connections.", "frpc"),
                ("Terracotta", "Create or join a Minecraft LAN room.", "terracotta"),
                (
                    "MinecraftPFP",
                    "Create a profile picture from a Minecraft skin and export it as PNG.",
                    "minecraft-pfp"
                ),
            }
        )
        {
            var content = new Grid { RowSpacing = 6 };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(Text(title, 16, true));
            var summary = Text(description);
            Grid.SetRow(summary, 1);
            content.Children.Add(summary);
            var open = Action(
                "Open",
                (_, _) =>
                {
                    page = route;
                    expandedNavigation = "more";
                    Render();
                },
                true
            );
            Grid.SetRow(open, 2);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(open);
            var card = CompactCard(content);
            Grid.SetColumn(card, cards.Children.Count);
            cards.Children.Add(card);
        }
        PageBody.Children.Add(cards);
    }

    bool terracottaBusy,
        terracottaInviteOpen;
    string terracottaOperation = "Starting Terracotta…";
    StackPanel? terracottaActions;
    Func<string, CancellationToken, Task> startTerracottaHost = null!;
    Func<ContentDialog, Task<ContentDialogResult>> showTerracottaInviteDialog = async dialog =>
        await dialog.ShowAsync();
    bool terracottaDownloaded;

    void TerracottaPage()
    {
        Header("Multiplayer lobby", "Terracotta");
        terracottaDownloaded = terracotta.Installed != null;
        terracottaStatus = null;
        terracottaChoices = null;
        terracottaCode = null;
        terracottaMembers = null;
        if (!terracottaDownloaded)
        {
            var download = new StackPanel { Spacing = 10 };
            download.Children.Add(Text("Terracotta is not downloaded.", 18, true));
            download.Children.Add(
                Text(
                    "The component is downloaded from the official release only when you click Download. The launcher does not include it."
                )
            );
            download.Children.Add(
                Action(
                    "Download Terracotta",
                    async (_, _) =>
                    {
                        await Run(terracotta.Install, "Download Terracotta");
                        Render();
                    },
                    true
                )
            );
            PageBody.Children.Add(Card(download));
            return;
        }

        var ready = new StackPanel { Spacing = 6 };
        terracottaHeading = Text("Multiplayer component ready", 15, true);
        ready.Children.Add(terracottaHeading);
        terracottaProgress = new ProgressBar
        {
            Height = 4,
            MinHeight = 4,
            Background = Brush(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorderAlt),
            Foreground = Brush(lightTheme ? ThemeTokens.LightAccent : ThemeTokens.LightAccentText),
            Minimum = 0,
            Maximum = 100,
            Value = 100,
        };
        ready.Children.Add(terracottaProgress);
        terracottaScanHint = Text(
            "Start Minecraft, enter a singleplayer world, press Esc and choose Open to LAN, then Start LAN World."
        );
        ready.Children.Add(terracottaScanHint);
        terracottaStatus = Text("");
        ready.Children.Add(terracottaStatus);
        terracottaCode = new TextBox
        {
            IsReadOnly = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = Ui("Room code / server address"),
        };
        terracottaMemberNames = [];
        terracottaMemberCards.Clear();
        terracottaMembers = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < 3; i++)
            terracottaMembers.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
        terracottaCodeRow = new Grid { ColumnSpacing = 8 };
        terracottaCodeRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        terracottaCodeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        terracottaCodeRow.Children.Add(terracottaCode);
        var copy = Action(
            "Copy",
            (_, _) =>
            {
                var value = terracottaCode?.Text;
                if (!terracotta.Connected || string.IsNullOrWhiteSpace(value))
                    return;
                if (!DesktopClipboard.TrySetText(value, out var failure))
                    throw new IOException(failure);
                ShowToast(Ui("Copied"));
            }
        );
        Grid.SetColumn(copy, 1);
        terracottaCodeRow.Children.Add(copy);
        ready.Children.Add(terracottaCodeRow);
        terracottaActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        terracottaActions.Children.Add(
            Action(
                "Leave room",
                async (_, _) => await TerracottaOperation("Leaving room…", () => terracotta.Leave())
            )
        );
        terracottaActions.Children.Add(
            Action(
                "Stop Terracotta",
                async (_, _) =>
                    await TerracottaOperation("Stopping Terracotta…", () => terracotta.Stop())
            )
        );
        ready.Children.Add(terracottaActions);
        PageBody.Children.Add(CreateCard(ready, new Thickness(12, 10, 12, 10), 6));
        var players = new StackPanel { Spacing = 12 };
        players.Children.Add(Text("Players", 18, true));
        players.Children.Add(terracottaMembers);
        terracottaMembersCard = Card(players);
        PageBody.Children.Add(terracottaMembersCard);

        var lobby = new StackPanel { Spacing = 12 };
        lobby.Children.Add(
            Text(
                "Terracotta is third-party open-source software. For issues with its networking component, contact the upstream project."
            )
        );
        lobby.Children.Add(
            Text(
                "Connection quality depends on the participants’ networks. Use compatible game and mod versions and follow applicable local laws."
            )
        );
        lobby.Children.Add(
            TerracottaLobbyEntry(
                "I want to host",
                "Create a room and share its invitation code with friends.",
                "\uE80F",
                "host"
            )
        );
        lobby.Children.Add(
            TerracottaLobbyEntry(
                "I want to join",
                "Enter the host’s invitation code to join their world.",
                "\uE710",
                "guest"
            )
        );
        if (OperatingSystem.IsMacOS())
            lobby.Children.Add(
                Action(
                    "Install macOS application",
                    async (_, _) => await terracotta.InstallMacApplication(default)
                )
            );
        terracottaChoices = Card(lobby);
        PageBody.Children.Add(terracottaChoices);
        var attribution = new StackPanel { Spacing = 8 };
        attribution.Children.Add(Text("Terracotta · © burningtnt · AGPL-3.0-or-later"));
        attribution.Children.Add(
            new HyperlinkButton
            {
                Content = Ui("Source and license"),
                NavigateUri = new Uri(TerracottaManager.SourceUrl),
            }
        );
        PageBody.Children.Add(Card(attribution));
        UpdateTerracotta();
    }

    Button TerracottaLobbyEntry(string title, string subtitle, string glyph, string view)
    {
        var button = Action(
            title,
            async (_, _) =>
            {
                if (terracottaBusy || terracottaInviteOpen)
                    return;
                if (view == "host")
                    await TerracottaOperation(
                        "Starting Terracotta…",
                        () => startTerracottaHost(TerracottaPlayer(), default)
                    );
                else
                    await JoinTerracottaDialog();
            }
        );
        button.Tag = view;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(12, 8, 12, 8);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20 });
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(Text(title, 16, true));
        labels.Children.Add(Text(subtitle, 12));
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        var arrow = new FontIcon { Glyph = "\uE72A", FontSize = 18 };
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        button.Content = row;
        return button;
    }

    string TerracottaPlayer() =>
        store.Config.Accounts.FirstOrDefault(a => a.Key == store.Config.SelectedAccount)?.Name
        ?? "Player";

    async Task TerracottaOperation(string operation, Func<Task> action)
    {
        if (terracottaBusy)
            return;
        terracottaBusy = true;
        terracottaOperation = operation;
        UpdateTerracotta();
        try
        {
            await action();
        }
        finally
        {
            terracottaBusy = false;
            UpdateTerracotta();
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
            var input = new TextBox
            {
                Header = Ui("Room code"),
                PlaceholderText = Ui("Paste the invitation code"),
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var dialog = new ContentDialog
            {
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui("Join room"),
                Content = input,
                PrimaryButtonText = Ui("Join room"),
                CloseButtonText = Ui("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = Content.XamlRoot,
            };
            if (store.Config.Settings.ColorPreset != "native")
            {
                dialog.Background = ComboSurfaceBrush;
                dialog.Foreground = ComboTextBrush;
                dialog.Resources["ContentDialogBackground"] = ComboSurfaceBrush;
                dialog.Resources["ContentDialogForeground"] = ComboTextBrush;
                input.Foreground = ComboTextBrush;
            }
            input.TextChanged += (_, _) =>
            {
                try
                {
                    _ = TerracottaManager.Parameter(input.Text);
                    dialog.IsPrimaryButtonEnabled = true;
                }
                catch (IOException)
                {
                    dialog.IsPrimaryButtonEnabled = false;
                }
            };
            if (await showTerracottaInviteDialog(dialog) == ContentDialogResult.Primary)
                invitation = input.Text.Trim();
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
                await terracotta.Start();
                await terracotta.JoinRoom(invitation, TerracottaPlayer());
            }
        );
    }

    internal void BuildSettingsClients()
    {
        var versions = frpc.Installed();
        if (!versions.Contains(store.Config.Settings.FrpcVersion))
            store.Config.Settings.FrpcVersion = versions.FirstOrDefault() ?? "";
        BuildFrpcDownloads(store.Config.Settings, versions);
        var frpcPanel = PageBody.Children[^1];
        PageBody.Children.Remove(frpcPanel);
        var terracottaPanel = TerracottaSettingsCard();
        terracottaPanel.Padding = new Thickness(12);
        var applications = new StackPanel { Spacing = 10, Tag = "third-party-apps" };
        applications.Children.Add(Text("Manage third-party applications", 18, true));
        applications.Children.Add(frpcPanel);
        applications.Children.Add(terracottaPanel);
        PageBody.Children.Add(applications);
        RefreshFrpcPalette();
    }

    internal Border TerracottaSettingsCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Text("Terracotta", 18, true));
        panel.Children.Add(
            Text(
                terracotta.Installed is { } value
                    ? "Terracotta " + value.Version
                    : "Terracotta is not downloaded."
            )
        );
        var actions = new FlowPanel();
        panel.Children.Add(actions);
        actions.Children.Add(
            Action(
                "Download Terracotta",
                async (_, _) =>
                {
                    await Run(terracotta.Install, "Download Terracotta");
                    Render();
                },
                true
            )
        );
        if (terracotta.Installed != null || Directory.Exists(terracotta.Root))
            actions.Children.Add(
                Action(
                    "Remove Terracotta",
                    async (sender, _) =>
                    {
                        if (terracottaBusy)
                            throw new IOException(
                                Ui("Wait for the current Terracotta operation to finish.")
                            );
                        if (
                            !await ConfirmPop(
                                (FrameworkElement)sender,
                                Ui("Remove Terracotta?"),
                                Ui(
                                    "This removes the downloaded component. You can download it again from the toolbox."
                                ),
                                Ui("Remove")
                            )
                        )
                            return;
                        await terracotta.Stop();
                        await Run(terracotta.Remove, "Remove Terracotta");
                        Render();
                    }
                )
            );
        return Card(panel);
    }

    void UpdateTerracotta()
    {
        if (page != "terracotta")
            return;
        if (terracottaDownloaded != (terracotta.Installed != null))
        {
            Render();
            return;
        }
        if (terracottaStatus == null)
            return;
        var state = terracotta.State?["state"]?.GetValue<string>();
        terracottaHeading!.Text = Ui(
            terracottaBusy ? terracottaOperation
            : terracotta.Connected && state != "waiting"
                ? (
                    state == "host-scanning"
                        ? "Scanning for LAN worlds…"
                        : TerracottaStatus.Description(terracotta.State)
                )
            : "Multiplayer component ready"
        );
        terracottaProgress!.IsIndeterminate =
            terracottaBusy || terracotta.Connected && TerracottaStatus.IsPending(state);
        terracottaProgress.Visibility =
            state == "exception" && !terracottaBusy ? Visibility.Collapsed : Visibility.Visible;
        terracottaScanHint!.Visibility =
            terracotta.Connected && state == "host-scanning"
                ? Visibility.Visible
                : Visibility.Collapsed;
        terracottaStatus.Text = Ui(terracotta.Error ?? "");
        terracottaStatus.Visibility =
            terracotta.Error == null ? Visibility.Collapsed : Visibility.Visible;
        if (terracottaChoices != null)
            terracottaChoices.Visibility =
                (terracottaBusy || terracotta.Connected && state != "waiting")
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        if (terracottaActions != null)
        {
            terracottaActions.Visibility =
                terracotta.Connected && state != "waiting" && !terracottaBusy
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        terracottaCode!.Text =
            terracotta.State?["room"]?.GetValue<string>()
            ?? terracotta.State?["url"]?.GetValue<string>()
            ?? "";
        terracottaCode.Visibility =
            !terracotta.Connected || terracottaCode.Text.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        terracottaCodeRow!.Visibility = terracottaCode.Visibility;
        UpdateTerracottaMembers();
    }

    void UpdateTerracottaMembers()
    {
        if (terracottaMembers == null || terracottaMembersCard == null)
            return;
        var names = terracotta.Connected ? TerracottaStatus.Members(terracotta.State) : [];
        terracottaMembersCard.Visibility =
            names.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (names.SequenceEqual(terracottaMemberNames))
            return;
        terracottaMemberNames = names;
        foreach (var old in terracottaMemberCards)
            themeCards.Remove(old);
        terracottaMemberCards.Clear();
        terracottaMembers.Children.Clear();
        terracottaMembers.RowDefinitions.Clear();
        for (var i = 0; i < names.Length; i++)
        {
            if (i % 3 == 0)
                terracottaMembers.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto }
                );
            var label = Text("", 14, true);
            label.Text = names[i];
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(label, names[i]);
            var content = new Grid { ColumnSpacing = 10, MinHeight = 36 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            content.Children.Add(new FontIcon { Glyph = "\uE77B", FontSize = 20 });
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var card = CompactCard(content);
            Grid.SetColumn(card, i % 3);
            Grid.SetRow(card, i / 3);
            terracottaMemberCards.Add(card);
            terracottaMembers.Children.Add(card);
        }
    }
}
