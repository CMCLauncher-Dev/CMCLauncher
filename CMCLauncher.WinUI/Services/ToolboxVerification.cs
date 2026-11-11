using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyToolboxUi(Action<bool, string> check, string directory)
    {
        page = "toolbox";
        expandedNavigation = "more";
        await RenderAsync();
        check(themeCards.Count == 3, "toolbox presents three independent tool cards");
        check(
            navigationButtons.ContainsKey("page:toolbox")
                && !navigationButtons.ContainsKey("page:frpc")
                && !navigationButtons.ContainsKey("page:minecraft-pfp"),
            "tools share one sidebar entry"
        );
        await Capture(Path.Combine(directory, "toolbox.png"));
        var open = Descendants(PageBody)
            .OfType<Button>()
            .Where(b => b.Content?.ToString() == Ui("Open"))
            .ToArray();
        check(open.Length == 3, "each tool card has an open button");
        (
            (IInvokeProvider)new ButtonAutomationPeer(open[1]).GetPattern(PatternInterface.Invoke)
        ).Invoke();
        for (var i = 0; i < 100 && page != "terracotta"; i++)
            await Task.Delay(30);
        check(
            page == "terracotta" && PageBackButton.Visibility == Visibility.Visible,
            "Terracotta card opens its own page with back navigation"
        );
        check(
            terracotta.Installed == null && !terracotta.Connected,
            "opening Terracotta does not install or start the component"
        );
        check(
            terracottaStatus == null && !Descendants(PageBody).OfType<TextBox>().Any(),
            "Terracotta hides all multiplayer controls before download"
        );
        await Capture(Path.Combine(directory, "terracotta-download.png"));
        Directory.CreateDirectory(terracotta.Root);
        await File.WriteAllTextAsync(
            Path.Combine(terracotta.Root, "fixture"),
            "UI fixture; never executed"
        );
        await File.WriteAllTextAsync(
            Path.Combine(terracotta.Root, "current.json"),
            "{\"Version\":\"0.4.2\",\"Executable\":\"fixture\",\"Files\":{\"fixture\":\"unused\"}}"
        );
        await RenderAsync();
        check(
            Descendants(PageBody).OfType<Button>().Count(b => b.Tag is "host" or "guest") == 2
                && !Descendants(PageBody).OfType<TextBox>().Any(t => !t.IsReadOnly),
            "downloaded component shows host and guest lobby entries without forms"
        );
        check(
            Descendants(PageBody)
                .OfType<HyperlinkButton>()
                .Any(b => b.NavigateUri?.AbsoluteUri == TerracottaManager.SourceUrl),
            "lobby displays upstream attribution and source link"
        );
        await Capture(Path.Combine(directory, "terracotta-lobby.png"));
        // Local snapshots and delegates exercise the UI without launching the component.
        var portField = typeof(TerracottaManager).GetField(
            "port",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        )!;
        var stateProperty = typeof(TerracottaManager).GetProperty(nameof(TerracottaManager.State))!;
        var savedHost = startTerracottaHost;
        var savedInvite = showTerracottaInviteDialog;
        var hostCalls = 0;
        startTerracottaHost = async (_, _) =>
        {
            hostCalls++;
            await Task.Delay(80);
            portField.SetValue(terracotta, 1);
            stateProperty.SetValue(
                terracotta,
                System.Text.Json.Nodes.JsonNode.Parse("{\"state\":\"host-scanning\",\"index\":1}")
            );
        };
        var host = Descendants(PageBody).OfType<Button>().Single(b => b.Tag?.ToString() == "host");
        (
            (IInvokeProvider)new ButtonAutomationPeer(host).GetPattern(PatternInterface.Invoke)
        ).Invoke();
        check(
            terracottaProgress!.IsIndeterminate
                && terracottaChoices!.Visibility == Visibility.Collapsed,
            "host click immediately enters progress without a second form"
        );
        var duplicate = false;
        await TerracottaOperation(
            "Starting Terracotta…",
            () =>
            {
                duplicate = true;
                return Task.CompletedTask;
            }
        );
        check(!duplicate, "room actions cannot overlap while starting");
        for (var i = 0; i < 100 && terracottaBusy; i++)
            await Task.Delay(20);
        check(
            hostCalls == 1
                && terracotta.Connected
                && !Descendants(PageBody).OfType<TextBox>().Any(t => !t.IsReadOnly),
            "host entry directly starts one room operation"
        );
        try
        {
            portField.SetValue(terracotta, 1);
            stateProperty.SetValue(
                terracotta,
                System.Text.Json.Nodes.JsonNode.Parse("{\"state\":\"host-scanning\",\"index\":1}")
            );
            await RenderAsync();
            check(
                terracottaProgress!.IsIndeterminate
                    && terracottaChoices!.Visibility == Visibility.Collapsed
                    && terracottaScanHint!.Visibility == Visibility.Visible,
                "scanning shows animated progress and instructions instead of duplicate forms"
            );
            var leave = Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == Ui("Leave room"));
            var stop = Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == Ui("Stop Terracotta"));
            check(
                leave.Parent == stop.Parent
                    && leave.Parent is StackPanel { Orientation: Orientation.Horizontal },
                "leave and stop remain on one horizontal row"
            );
            await Task.Delay(800);
            check(
                terracottaProgress.ActualHeight >= 4 && terracottaProgress.Background != null,
                "waiting progress has a visible themed track"
            );
            await Capture(Path.Combine(directory, "terracotta-scanning.png"));
            stateProperty.SetValue(
                terracotta,
                System.Text.Json.Nodes.JsonNode.Parse(
                    "{\"state\":\"host-ok\",\"index\":2,\"profile_index\":1,\"room\":\"U/fixture\",\"profiles\":[{\"name\":\"Host\"},{\"name\":\"Player Two\"},{\"name\":\"A very long player display name for ellipsis\"},{\"name\":\"Fourth player\"}]}"
                )
            );
            UpdateTerracotta();
            await Task.Delay(100);
            check(
                terracottaMembersCard!.Parent == PageBody
                    && terracottaMembersCard.Visibility == Visibility.Visible
                    && terracottaMembers!.ColumnDefinitions.Count == 3
                    && terracottaMembers.RowDefinitions.Count == 2
                    && terracottaMembers.Children.Count == 4,
                "players use a separate card with three equal columns and additional rows"
            );
            var playerCards = terracottaMembers.Children.OfType<Border>().ToArray();
            check(
                Grid.GetColumn(playerCards[3]) == 0
                    && Grid.GetRow(playerCards[3]) == 1
                    && Math.Abs(playerCards[0].ActualWidth - playerCards[1].ActualWidth) < 1,
                "player cards stay one third width including the last row"
            );
            var trackedCards = themeCards.Count;
            UpdateTerracotta();
            check(
                themeCards.Count == trackedCards,
                "unchanged player updates do not accumulate themed cards"
            );
            await Capture(Path.Combine(directory, "terracotta-players.png"));
            stateProperty.SetValue(
                terracotta,
                System.Text.Json.Nodes.JsonNode.Parse("{\"state\":\"waiting\",\"index\":2}")
            );
            UpdateTerracotta();
            check(
                terracottaActions!.Visibility == Visibility.Collapsed
                    && terracottaChoices.Visibility == Visibility.Visible
                    && !terracottaProgress.IsIndeterminate,
                "leaving a room hides both actions and returns to lobby"
            );
            check(
                terracottaMembersCard.Visibility == Visibility.Collapsed
                    && terracottaMembers.Children.Count == 0,
                "leaving clears and hides the separate players card"
            );
            var inviteCalls = 0;
            showTerracottaInviteDialog = async dialog =>
            {
                inviteCalls++;
                check(
                    !dialog.IsPrimaryButtonEnabled && dialog.Content is TextBox,
                    "join opens an invitation-only modal with confirmation disabled"
                );
                Exception? failure = null;
                dialog.Opened += async (_, _) =>
                {
                    try
                    {
                        check(
                            dialog.RequestedTheme
                                == (lightTheme ? ElementTheme.Light : ElementTheme.Dark)
                                && dialog.ActualTheme
                                    == (lightTheme ? ElementTheme.Light : ElementTheme.Dark),
                            "invitation modal uses the current light or dark theme"
                        );
                        if (store.Config.Settings.ColorPreset != "native")
                            check(
                                ReferenceEquals(dialog.Background, ComboSurfaceBrush)
                                    && ReferenceEquals(dialog.Foreground, ComboTextBrush),
                                "invitation modal uses the custom theme palette"
                            );
                        var input = (TextBox)dialog.Content;
                        input.Text = "   ";
                        await Task.Delay(80);
                        check(
                            !dialog.IsPrimaryButtonEnabled,
                            "blank invitation cannot be submitted"
                        );
                        input.Text = "U/fixture";
                        for (var i = 0; i < 100 && !dialog.IsPrimaryButtonEnabled; i++)
                            await Task.Delay(20);
                        check(
                            dialog.IsPrimaryButtonEnabled,
                            "valid invitation enables confirmation"
                        );
                        await Capture(Path.Combine(directory, "terracotta-invitation.png"));
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        dialog.Hide();
                    }
                };
                await dialog.ShowAsync();
                if (failure != null)
                    throw failure;
                return ContentDialogResult.None;
            };
            await JoinTerracottaDialog();
            check(
                inviteCalls == 1
                    && !terracottaInviteOpen
                    && !terracottaBusy
                    && terracotta.State?["state"]?.ToString() == "waiting",
                "cancelling invitation leaves lobby and core unchanged"
            );
            portField.SetValue(terracotta, 0);
            UpdateTerracotta();
            check(
                !terracottaProgress.IsIndeterminate
                    && terracottaChoices.Visibility == Visibility.Visible,
                "disconnect stops waiting animation and restores the room form"
            );
        }
        finally
        {
            portField.SetValue(terracotta, 0);
            stateProperty.SetValue(terracotta, null);
            startTerracottaHost = savedHost;
            showTerracottaInviteDialog = savedInvite;
        }
        page = "settings";
        await RenderAsync();
        check(
            Descendants(PageBody)
                .OfType<Button>()
                .Any(b => b.Content?.ToString() == Ui("Remove Terracotta")),
            "settings provides component removal"
        );
        await terracotta.Remove();
        page = "terracotta";
        await RenderAsync();
        check(
            terracottaStatus == null && !Descendants(PageBody).OfType<TextBox>().Any(),
            "removing component restores download-only page"
        );
        PageBack_Click(PageBackButton, new RoutedEventArgs());
        for (var i = 0; i < 100 && terracotta.Active; i++)
            await Task.Delay(30);
        check(
            page == "toolbox" && !terracotta.Active,
            "back returns to toolbox and disables foreground polling"
        );
    }
}
