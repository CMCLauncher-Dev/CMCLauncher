using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    const string HomeCardDragFormat = "CMCLauncher.HomeCard";
    Button? homeAddCardButton;
    string? homeDraggedCardId;
    bool homeNativeDragRunning,
        homeDropHandled;

    static bool IsHomeCardDrag(DataPackageView data) =>
        data.Contains(HomeCardDragFormat) || data.Properties.ContainsKey(HomeCardDragFormat);

    void PrepareHomeCardDrag(DataPackage data, string id)
    {
        homeDraggedCardId = id;
        homeDropHandled = false;
        data.SetData(HomeCardDragFormat, id);
        data.Properties[HomeCardDragFormat] = id;
        data.RequestedOperation = store.Config.Settings.HomeCards.Any(c => c.Id == id)
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
    }

    Border HomeCardDragHandle(string id)
    {
        var handle = new Border
        {
            Width = 32,
            Height = 32,
            MinWidth = 32,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = ComboSurfaceBrush,
            BorderBrush = ComboBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Tag = "home-drag:" + id,
            Child = new FontIcon
            {
                Glyph = "\uE700",
                FontSize = 14,
                Foreground = ComboTextBrush,
            },
        };
        ToolTipService.SetToolTip(handle, Ui("Drag to move"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(handle, Ui("Drag to move"));
        EnableHomeCardDrag(handle, id);
        return handle;
    }

    void EnableHomeCardDrag(FrameworkElement element, string id)
    {
        element.CanDrag = true;
        Windows.Foundation.Point? origin = null;
        element.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(element);
            if (
                !homeCustomizeMode
                || page != "home"
                || task != null
                || !point.Properties.IsLeftButtonPressed
            )
                return;
            origin = point.Position;
            element.CapturePointer(args.Pointer);
            args.Handled = true;
        };
        element.PointerMoved += async (_, args) =>
        {
            if (origin is not { } start || homeNativeDragRunning)
                return;
            var point = args.GetCurrentPoint(element);
            if (
                !(
                    point.Properties.IsLeftButtonPressed
                    || point.IsInContact
                        && !point.Properties.IsRightButtonPressed
                        && !point.Properties.IsMiddleButtonPressed
                )
            )
            {
                origin = null;
                return;
            }
            if (
                Math.Abs(point.Position.X - start.X) < 6
                && Math.Abs(point.Position.Y - start.Y) < 6
            )
                return;
            origin = null;
            homeNativeDragRunning = true;
            args.Handled = true;
            element.ReleasePointerCapture(args.Pointer);
            try
            {
                await element.StartDragAsync(point);
            }
            catch (Exception ex)
            {
                RuntimeLog.Write("HOME DRAG", ex.ToString());
                await Error(ex);
            }
            finally
            {
                homeNativeDragRunning = false;
                homeDraggedCardId = null;
                homeDropHandled = false;
            }
        };
        element.PointerReleased += (_, args) =>
        {
            origin = null;
            element.ReleasePointerCapture(args.Pointer);
        };
        element.PointerCanceled += (_, _) => origin = null;
        element.PointerCaptureLost += (_, _) => origin = null;
        element.DragStarting += (_, args) =>
        {
            if (!homeCustomizeMode || page != "home" || task != null)
            {
                args.Cancel = true;
                homeNativeDragRunning = false;
                return;
            }
            homeNativeDragRunning = true;
            PrepareHomeCardDrag(args.Data, id);
            CloseCommunityFilterDrawer();
        };
        element.DropCompleted += (_, _) =>
        {
            homeNativeDragRunning = false;
            homeDraggedCardId = null;
            homeDropHandled = false;
        };
    }

    void HomeCard_DragOver(object sender, DragEventArgs args)
    {
        if (!homeCustomizeMode || page != "home" || task != null || !IsHomeCardDrag(args.DataView))
            return;
        var id = args.DataView.Properties.TryGetValue(HomeCardDragFormat, out var value)
            ? value as string
            : homeDraggedCardId;
        args.AcceptedOperation = store.Config.Settings.HomeCards.Any(c => c.Id == id)
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
        args.DragUIOverride.Caption = Ui("Move card here");
        args.Handled = true;
    }

    async Task DropHomeCard(DragEventArgs args, FrameworkElement target, string? targetId)
    {
        if (
            homeDropHandled
            || !homeCustomizeMode
            || page != "home"
            || task != null
            || !IsHomeCardDrag(args.DataView)
        )
            return;
        args.Handled = true;
        homeDropHandled = true;
        var deferral = args.GetDeferral();
        var cards = store.Config.Settings.HomeCards;
        var before = cards.ToArray();
        bool changed = false;
        Exception? failure = null;
        try
        {
            var data = args.DataView.Properties.TryGetValue(HomeCardDragFormat, out var metadata)
                ? metadata
                : await args.DataView.GetDataAsync(HomeCardDragFormat);
            if (data is not string id)
                return;
            var position = args.GetPosition(target);
            var after =
                targetId != null && Grid.GetColumnSpan(target) == 1
                    ? position.X > target.ActualWidth / 2
                    : position.Y > target.ActualHeight / 2;
            changed = HomeCardLayout.Move(cards, id, targetId, after);
            if (changed)
                await store.Save();
        }
        catch (Exception ex)
        {
            cards.Clear();
            cards.AddRange(before);
            failure = ex;
        }
        finally
        {
            deferral.Complete();
        }
        // Finish the native drag before replacing its source or opening a dialog.
        if (failure != null)
            DispatcherQueue.TryEnqueue(async () => await Error(failure));
        else if (changed && page == "home")
            DispatcherQueue.TryEnqueue(Render);
    }

    void OpenHomeCardDrawer(Button opener)
    {
        var content = new StackPanel { Spacing = 12 };
        foreach (
            var (id, label) in new[]
            {
                ("performance", "性能监视器"),
                ("note", "笔记"),
                ("news", "Minecraft 新闻"),
            }
        )
        {
            if (store.Config.Settings.HomeCards.Any(c => c.Id == id))
                continue;
            var add = Action(
                Ui("Add") + " " + Ui(label),
                async (_, _) =>
                {
                    if (!HomeCardLayout.Move(store.Config.Settings.HomeCards, id))
                        return;
                    await SaveHomeLayout();
                    Render();
                }
            );
            add.Tag = "home-add:" + id;
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            row.Children.Add(HomeCardDragHandle(id));
            Grid.SetColumn(add, 1);
            row.Children.Add(add);
            var available = Card(row);
            EnableHomeCardDrag(available, id);
            content.Children.Add(available);
        }
        if (content.Children.Count == 0)
            content.Children.Add(Text("All available cards have been added."));
        OpenCommunityFilterDrawer(content, opener, () => Task.CompletedTask, () => { });
        CommunityFilterDrawerTitle.Text = Ui("Add cards");
        CommunityFilterDrawerHint.Text = Ui("Click to add, or drag a card onto the home page.");
        CommunityFilterDrawerFooter.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(CommunityFilterCloseButton, Ui("Close"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            CommunityFilterCloseButton,
            Ui("Close")
        );
    }
}
