using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyInterfaceLayout(Action<bool, string> check, string directory)
    {
        page = "settings";
        await RenderAsync();
        check(
            PageBody.Children[^2] is Expander { Tag: "frpc-download-panel", IsExpanded: false }
                && Descendants(PageBody.Children[^1])
                    .OfType<Button>()
                    .Any(b => b.Content?.ToString() == Ui("Download Terracotta")),
            "frpc and Terracotta management are the final settings sections"
        );
        check(
            !navigationButtons.ContainsKey("page:logs"),
            "runtime logs are absent from the sidebar"
        );
        check(
            Grid.GetRow(TaskLogsButton) == 2
                && TaskLogsButton.HorizontalAlignment == HorizontalAlignment.Right,
            "task drawer pins the text-file logs button at the bottom right"
        );
        TaskDrawer.IsPaneOpen = true;
        TaskLogs_Click(TaskLogsButton, new RoutedEventArgs());
        await Task.Delay(100);
        check(
            page == "logs" && !TaskDrawer.IsPaneOpen,
            "drawer logs button opens logs and closes the drawer"
        );
        page = "about";
        await RenderAsync();
        await Task.Delay(100);
        check(
            Descendants(PageBody).OfType<Image>().Count(i => i.Source is BitmapImage)
                >= AboutAcknowledgements.Contributors.Count + AboutAcknowledgements.Projects.Count,
            "about displays contributor avatars and upstream project icons"
        );
        check(
            AboutAcknowledgements.Projects.All(p =>
                File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "about", p.Image))
            ),
            "about project logos are packaged locally"
        );
        var contributors = (FrameworkElement)PageBody.Children[^2];
        PageViewport.ChangeView(
            null,
            Math.Max(
                0,
                contributors
                    .TransformToVisual(PageBody)
                    .TransformPoint(new Windows.Foundation.Point())
                    .Y - 10
            ),
            null,
            true
        );
        await Task.Delay(200);
        await Capture(Path.Combine(directory, "about-projects.png"));
        var saved = store.Config.Settings.HomeCards;
        var customized = homeCustomizeMode;
        try
        {
            store.Config.Settings.HomeCards =
            [
                new()
                {
                    Id = "performance",
                    Width = 1,
                    Height = 1,
                },
            ];
            homeCustomizeMode = true;
            foreach (var width in new[] { 740, 1280 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 820));
                page = "home";
                await RenderAsync();
                await Task.Delay(200);
                PageViewport.UpdateLayout();
                check(
                    !Descendants(PageBody)
                        .OfType<Button>()
                        .Any(b => b.Tag?.ToString()?.StartsWith("home-add:") == true),
                    "unadded cards are absent from the home editor at " + width
                );
                var drag = new Windows.ApplicationModel.DataTransfer.DataPackage();
                PrepareHomeCardDrag(drag, "performance");
                check(
                    IsHomeCardDrag(drag.GetView())
                        && drag.GetView().Contains(HomeCardDragFormat)
                        && (string)await drag.GetView().GetDataAsync(HomeCardDragFormat)
                            == "performance"
                        && drag.RequestedOperation
                            == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move,
                    "native card drag carries a transferable identity at " + width
                );
                homeDraggedCardId = null;
                OpenHomeCardDrawer(homeAddCardButton!);
                await Task.Delay(100);
                var available = Descendants(CommunityFilterDrawerBody)
                    .OfType<Button>()
                    .Where(b => b.Tag?.ToString()?.StartsWith("home-add:") == true)
                    .ToArray();
                check(
                    CommunityFilterDrawer.IsPaneOpen
                        && CommunityFilterDrawerFooter.Visibility == Visibility.Collapsed
                        && available.Length == 2
                        && Descendants(CommunityFilterDrawerBody)
                            .OfType<Border>()
                            .Count(b =>
                                b.CanDrag && b.Tag?.ToString()?.StartsWith("home-drag:") == true
                            ) == 2,
                    "drawer shows only unadded cards with drag support at " + width
                );
                CloseCommunityFilterDrawer();
                await Task.Delay(100);
                var tools = Descendants(PageBody)
                    .OfType<Grid>()
                    .Single(g => g.Tag?.ToString() == "home-card-tools");
                check(
                    tools.ColumnDefinitions.Count == 6
                        && tools.Children.All(c =>
                        {
                            var element = (FrameworkElement)c;
                            var point = element
                                .TransformToVisual(tools)
                                .TransformPoint(new Windows.Foundation.Point());
                            return point.X >= -1
                                && point.X + element.ActualWidth <= tools.ActualWidth + 1;
                        }),
                    "half-width home-card controls stay inside the card at " + width
                );
                check(
                    tools
                        .Children.OfType<FrameworkElement>()
                        .Any(b => b.CanDrag && b.Tag?.ToString() == "home-drag:performance"),
                    "installed cards expose a drag handle at " + width
                );
                check(
                    ((StackPanel)tools.Parent).Children.OfType<Border>().Single().CanDrag,
                    "card surface also accepts drag gestures at " + width
                );
                await Capture(Path.Combine(directory, "home-customize-" + width + ".png"));
            }
            homeCustomizeMode = false;
            store.Config.Settings.HomeCards = [new() { Id = "account", Height = 2 }];
            page = "home";
            await RenderAsync();
            await Task.Delay(100);
            var accountActions = Descendants(PageBody)
                .OfType<Grid>()
                .Single(g => g.Tag?.ToString() == "home-account-actions");
            check(
                accountActions.ColumnDefinitions.Count == 6
                    && accountActions.RowDefinitions.Count == 1
                    && accountActions.Children.Count == 6,
                "standard account card has six equal actions in one row"
            );
            check(
                accountActions
                    .Children.OfType<Button>()
                    .All(b =>
                        b.Content is Microsoft.UI.Xaml.Shapes.Path
                        && ToolTipService.GetToolTip(b) != null
                    ),
                "account actions use icon-only buttons and tooltips"
            );
            SetTaskButtonsEnabled(true);
            check(
                !accountActions.Children.OfType<Button>().First().IsEnabled,
                "task completion preserves account-specific disabled actions"
            );
            PageViewport.UpdateLayout();
            var accountLayout = (Grid)accountActions.Parent;
            var accountCard = (Border)accountLayout.Parent;
            var identity = (FrameworkElement)accountLayout.Children[0];
            var infoBottom =
                identity
                    .TransformToVisual(accountCard)
                    .TransformPoint(new Windows.Foundation.Point())
                    .Y + identity.ActualHeight;
            var actionTop = accountActions
                .TransformToVisual(accountCard)
                .TransformPoint(new Windows.Foundation.Point())
                .Y;
            var above = actionTop - infoBottom;
            var below = accountCard.ActualHeight - actionTop - accountActions.ActualHeight;
            check(
                Math.Abs(above - below) <= 2,
                "account action row has equal visible space above and below"
            );
            await Capture(Path.Combine(directory, "home-account-standard.png"));
        }
        finally
        {
            store.Config.Settings.HomeCards = saved;
            homeCustomizeMode = customized;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1260, 820));
        }
    }
}
