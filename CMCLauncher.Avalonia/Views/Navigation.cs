using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    sealed class NavigationGroupView(Border view, StackPanel content)
    {
        public Border View { get; } = view;
        public StackPanel Content { get; } = content;
        public bool IsExpanded { get; set; }
    }

    sealed class NavigationVisual(
        Button button,
        Border indicator,
        TextBlock label,
        global::Avalonia.Controls.Shapes.Path icon,
        SolidColorBrush background
    )
    {
        public Button Button { get; } = button;
        public Border Indicator { get; } = indicator;
        public TextBlock Label { get; } = label;
        public global::Avalonia.Controls.Shapes.Path Icon { get; } = icon;
        public SolidColorBrush Background { get; } = background;
        public ScaleTransform Scale { get; } = new(1, .55);
        public TranslateTransform Offset { get; } = new();
        public bool Selected { get; set; }
        public bool Hovered { get; set; }
    }

    readonly Dictionary<string, Button> navigationButtons = new();
    readonly Dictionary<string, NavigationVisual> navigationVisuals = new();
    readonly Dictionary<string, NavigationGroupView> navigationGroups = new();
    readonly Dictionary<string, string> navigationParents = new();
    readonly Dictionary<
        object,
        (Stopwatch Clock, double Duration, Action<double> Draw, Action Finish)
    > navigationTweens = new();
    readonly DispatcherTimer navigationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    bool navigationTimerReady;
    string expandedNavigation = "home";
    string? previousRoot,
        previousPage;

    Button? taskNavigationButton;
    Border? taskCountBadge;
    TextBlock? taskCountText;

    void RefreshTaskNavigation()
    {
        if (taskCountBadge == null || taskCountText == null || taskNavigationButton == null)
            return;
        var count = session.Operations.Count(operation => operation.IsRunning);
        taskCountText.Text = count.ToString();
        taskCountBadge.IsVisible = count > 0;
        taskCountBadge.Background = Accent;
        taskCountText.Foreground = Brushes.White;
        taskNavigationButton.Background = drawer.IsPaneOpen
            ? new SolidColorBrush(
                Color.Parse(
                    Palette(
                        Light ? "LightSelection" : "DarkSelection",
                        Light ? "#DDE8D6" : "#2C3550"
                    )
                )
            )
            : Brushes.Transparent;
    }

    void ApplyNavigationMetrics()
    {
        var settings = session.Store.Config.Settings;
        var menuScale = settings.UiMenuScale;
        var fontScale = settings.UiFontSize / 14;
        var factor = Math.Max(menuScale, fontScale);
        if (scaling.Child is Grid shell)
            shell.ColumnDefinitions[0].Width = new GridLength(204 * factor);
        foreach (var (key, visual) in navigationVisuals)
        {
            var child = !key.StartsWith("root:");
            visual.Button.Height = (child ? 28 : 32) * factor;
            visual.Button.MinHeight = (child ? 27 : 30) * factor;
            visual.Button.Padding = child
                ? new Thickness(11 * menuScale, 2 * menuScale, 8 * menuScale, 2 * menuScale)
                : new Thickness(10 * menuScale, 3 * menuScale, 8 * menuScale, 3 * menuScale);
            visual.Label.FontSize = 14 * fontScale;
            visual.Icon.Width = visual.Icon.Height = 16 * fontScale;
        }
        RefreshTaskNavigation();
    }

    Control NavigationLayout()
    {
        var layout = new DockPanel();
        var tasks = Btn("Tasks", () => Navigate("tasks"));
        tasks.Name = "TaskDrawerButton";
        tasks.HorizontalAlignment = HorizontalAlignment.Stretch;
        tasks.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tasks.Padding = new Thickness(10, 8);
        tasks.CornerRadius = new CornerRadius(5);
        tasks.Margin = new Thickness(5, 14, 5, 0);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("16,*,Auto"),
            ColumnSpacing = 10,
        };
        row.Children.Add(NavigationIcon(LauncherNavigation.TaskIcon));
        var label = T("任务", 14);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        taskCountText = T("", 12, true);
        taskCountText.HorizontalAlignment = HorizontalAlignment.Center;
        taskCountText.VerticalAlignment = VerticalAlignment.Center;
        taskCountBadge = new Border
        {
            Child = taskCountText,
            MinWidth = 22,
            Height = 22,
            Padding = new Thickness(5, 0),
            CornerRadius = new CornerRadius(11),
            IsVisible = false,
        };
        Grid.SetColumn(taskCountBadge, 2);
        row.Children.Add(taskCountBadge);
        taskNavigationButton = tasks;
        session.Operations.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
                foreach (ViewModels.OperationState item in e.OldItems)
                    item.PropertyChanged -= OperationChanged;
            if (e.NewItems != null)
                foreach (ViewModels.OperationState item in e.NewItems)
                    item.PropertyChanged += OperationChanged;
            RefreshTaskNavigation();
        };
        void OperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.OperationState.IsRunning))
                RefreshTaskNavigation();
        }
        drawer.PropertyChanged += (_, e) =>
        {
            if (e.Property == SplitView.IsPaneOpenProperty)
                RefreshTaskNavigation();
        };
        tasks.Content = row;
        DockPanel.SetDock(tasks, Dock.Bottom);
        layout.Children.Add(tasks);
        layout.Children.Add(
            new ScrollViewer
            {
                Content = navigation,
                HorizontalScrollBarVisibility = global::Avalonia
                    .Controls
                    .Primitives
                    .ScrollBarVisibility
                    .Hidden,
                VerticalScrollBarVisibility = global::Avalonia
                    .Controls
                    .Primitives
                    .ScrollBarVisibility
                    .Hidden,
            }
        );
        return layout;
    }

    void BuildNavigation()
    {
        navigationTimer.Stop();
        navigationTweens.Clear();
        navigation.Children.Clear();
        navigationButtons.Clear();
        navigationVisuals.Clear();
        navigationGroups.Clear();
        navigationParents.Clear();
        navigation.Children.Add(
            NavigationButton(
                "主页",
                LauncherNavigation.HomeIcon,
                "root:home",
                () =>
                {
                    if (route == "home" && expandedNavigation == "home")
                        return Task.CompletedTask;
                    expandedNavigation = "home";
                    return Navigate("home");
                }
            )
        );
        foreach (var group in LauncherNavigation.Groups)
        {
            var root = NavigationButton(
                group.Title,
                group.IconPath,
                "root:" + group.Key,
                () =>
                {
                    expandedNavigation = expandedNavigation == group.Key ? "" : group.Key;
                    SyncNavigation();
                    return Task.CompletedTask;
                }
            );
            navigation.Children.Add(root);
            var children = new StackPanel { Spacing = 2, Margin = new Thickness(12, 1, 0, 5) };
            foreach (var entry in group.Entries)
            {
                if (entry.AvaloniaRoute == "mod")
                {
                    var heading = T("Community resources", 11, true);
                    heading.Margin = new Thickness(11, 10, 0, 4);
                    heading.Foreground = new SolidColorBrush(
                        Color.Parse(
                            Palette(
                                Light
                                    ? "LightCommunityResourcesHeading"
                                    : "DarkCommunityResourcesHeading",
                                Light ? "#4C8150" : "#A5C995"
                            )
                        )
                    );
                    children.Children.Add(heading);
                }
                navigationParents[entry.AvaloniaRoute] = group.Key;
                children.Children.Add(
                    NavigationButton(
                        entry.Title,
                        entry.IconPath,
                        entry.AvaloniaRoute,
                        () =>
                            route == entry.AvaloniaRoute && expandedNavigation == group.Key
                                ? Task.CompletedTask
                                : Navigate(entry.AvaloniaRoute),
                        true
                    )
                );
            }
            var viewport = new Border
            {
                Child = children,
                Height = 0,
                Opacity = 0,
                IsVisible = false,
                ClipToBounds = true,
            };
            navigationGroups[group.Key] = new(viewport, children);
            navigation.Children.Add(viewport);
        }
        if (
            sidebar.Child is DockPanel layout
            && layout.Children[0] is Button { Content: Grid taskRow }
        )
            taskRow.Children.OfType<TextBlock>().Single().Text = Ui("任务");
        previousRoot = previousPage = null;
        ApplyNavigationMetrics();
        SyncNavigation(false, true);
    }

    global::Avalonia.Controls.Shapes.Path NavigationIcon(string path) =>
        new()
        {
            Data = Geometry.Parse(path),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Stroke = Foreground,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

    Button NavigationButton(
        string title,
        string path,
        string key,
        Func<Task> action,
        bool child = false
    )
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4,18,*,14"),
            ColumnSpacing = 9,
            MinHeight = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var indicator = new Border
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = RelativePoint.Center,
        };
        row.Children.Add(indicator);
        var icon = NavigationIcon(path);
        Grid.SetColumn(icon, 1);
        row.Children.Add(icon);
        var label = T(title, 14);

        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        if (key.StartsWith("root:") && key != "root:home")
        {
            var chevron = NavigationIcon("M4,8 L12,16 L20,8");
            chevron.Width = chevron.Height = 10;
            chevron.Tag = "chevron";
            Grid.SetColumn(chevron, 3);
            row.Children.Add(chevron);
        }
        var brush = new SolidColorBrush(Colors.Transparent);
        var button = new Button
        {
            Content = row,
            Tag = key,
            Background = brush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = child ? new Thickness(11, 2, 8, 2) : new Thickness(10, 3, 8, 3),
            MinHeight = child ? 27 : 30,
            Height = child ? 28 : 32,
        };
        // A stable brush prevents the native hover/pressed template from flashing a stale selection.
        foreach (var state in new[] { ":pointerover", ":pressed" })
            button.Styles.Add(
                new Style(s =>
                    s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>()
                )
                {
                    Setters = { new Setter(ContentPresenter.BackgroundProperty, brush) },
                }
            );
        var visual = new NavigationVisual(button, indicator, label, icon, brush);
        indicator.RenderTransform = new TransformGroup
        {
            Children = { visual.Scale, visual.Offset },
        };
        button.PointerEntered += (_, _) =>
        {
            visual.Hovered = true;
            PaintNavigation(visual);
        };
        button.PointerExited += (_, _) =>
        {
            visual.Hovered = false;
            PaintNavigation(visual);
        };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(title));
        navigationButtons[key] = button;
        navigationVisuals[key] = visual;
        return button;
    }

    void PaintNavigation(NavigationVisual visual)
    {
        visual.Background.Color =
            visual.Selected
                ? Color.Parse(
                    Palette(
                        Light ? "LightSelection" : "DarkSelection",
                        Light ? "#E2E3E9" : "#2D2D32"
                    )
                )
            : visual.Hovered
                ? Color.Parse(
                    Palette(Light ? "LightSurface" : "DarkSurface", Light ? "#EDEDF2" : "#25252B")
                )
            : Colors.Transparent;
        visual.Indicator.Background = Accent;
        var selectedText = new SolidColorBrush(
            Color.Parse(
                Palette(Light ? "DarkAccentText" : "LightAccentText", Light ? "#30592B" : "#91A9F0")
            )
        );
        visual.Label.Foreground = visual.Selected
            ? selectedText
            : new SolidColorBrush(
                Color.Parse(
                    Palette(
                        Light ? "LightTextCaption" : "DarkTextCaption",
                        Light ? "#41424D" : "#E5E5EE"
                    )
                )
            );
        visual.Icon.Stroke = visual.Selected
            ? selectedText
            : new SolidColorBrush(
                Color.Parse(
                    Palette(Light ? "LightTextSub" : "DarkTextSub", Light ? "#4A4B58" : "#D1D1DC")
                )
            );
    }

    void TweenNavigation(object key, double milliseconds, Action<double> draw, Action finish)
    {
        navigationTweens[key] = (Stopwatch.StartNew(), milliseconds, draw, finish);
        if (!navigationTimerReady)
        {
            navigationTimerReady = true;
            navigationTimer.Tick += (_, _) =>
            {
                foreach (var (target, tween) in navigationTweens.ToArray())
                {
                    var t = Math.Clamp(
                        tween.Clock.Elapsed.TotalMilliseconds / tween.Duration,
                        0,
                        1
                    );
                    tween.Draw(1 - Math.Pow(1 - t, 3));
                    if (t >= 1)
                    {
                        navigationTweens.Remove(target);
                        tween.Finish();
                    }
                }
                if (navigationTweens.Count == 0)
                    navigationTimer.Stop();
            };
            Closed += (_, _) =>
            {
                navigationTimer.Stop();
                navigationTweens.Clear();
            };
        }
        navigationTimer.Start();
    }

    void SetGroupExpanded(NavigationGroupView group, bool expanded, bool animate)
    {
        if (group.IsExpanded == expanded)
            return;
        group.IsExpanded = expanded;
        var view = group.View;
        var start = view.IsVisible ? view.Bounds.Height : 0;
        var opacity = view.Opacity;
        navigationTweens.Remove(view);
        view.IsVisible = group.Content.IsVisible = true;
        view.IsHitTestVisible = expanded;
        group.Content.Measure(
            new Size(Math.Max(1, sidebar.Bounds.Width - 20), double.PositiveInfinity)
        );
        var target = expanded ? group.Content.DesiredSize.Height : 0;
        void Finish()
        {
            view.Height = expanded ? double.NaN : 0;
            view.Opacity = expanded ? 1 : 0;
            view.IsVisible = group.Content.IsVisible = expanded;
        }
        if (!animate)
        {
            Finish();
            return;
        }
        view.Height = start;
        TweenNavigation(
            view,
            210,
            t =>
            {
                view.Height = start + (target - start) * t;
                view.Opacity = opacity + ((expanded ? 1 : 0) - opacity) * t;
            },
            Finish
        );
    }

    void SyncNavigation(bool animate = true, bool expandForRoute = false)
    {
        var selected = route is "frpc" or "pfp" or "terracotta" ? "toolbox" : route;
        var group = selected is "home" or "instances" or "instance-settings" or "modpack-export"
            ? "home"
            : navigationParents.GetValueOrDefault(selected, selected == "logs" ? "" : "more");
        if (expandForRoute && navigationGroups.ContainsKey(group))
            expandedNavigation = group;
        foreach (var (key, submenu) in navigationGroups)
            SetGroupExpanded(submenu, expandedNavigation == key, animate && IsLoaded);
        var rootKey = "root:" + group;
        var keys = navigationButtons.Keys.ToList();
        foreach (var (key, visual) in navigationVisuals)
        {
            var active = route != "logs" && (key == rootKey || key == selected);
            if (visual.Selected != active)
            {
                var previous = key.StartsWith("root:") ? previousRoot : previousPage;
                var offset =
                    previous != null && keys.Contains(previous)
                        ? Math.Sign(keys.IndexOf(previous) - keys.IndexOf(key)) * 8
                        : 0;
                var opacity = visual.Indicator.Opacity;
                var scale = visual.Scale.ScaleY;
                if (active && opacity < .01)
                    visual.Offset.Y = offset;
                var y = visual.Offset.Y;
                visual.Selected = active;
                navigationTweens.Remove(visual.Indicator);
                void Finish()
                {
                    visual.Indicator.Opacity = active ? 1 : 0;
                    visual.Scale.ScaleY = active ? 1 : .55;
                    visual.Offset.Y = 0;
                }
                if (animate && IsLoaded)
                    TweenNavigation(
                        visual.Indicator,
                        180,
                        t =>
                        {
                            visual.Indicator.Opacity = opacity + ((active ? 1 : 0) - opacity) * t;
                            visual.Scale.ScaleY = scale + ((active ? 1 : .55) - scale) * t;
                            visual.Offset.Y = y * (1 - t);
                        },
                        Finish
                    );
                else
                    Finish();
            }
            PaintNavigation(visual);
            if (
                visual.Button.Content is Grid row
                && row
                    .Children.OfType<global::Avalonia.Controls.Shapes.Path>()
                    .LastOrDefault(p => p.Tag?.ToString() == "chevron")
                    is { } chevron
            )
                chevron.Data = Geometry.Parse(
                    expandedNavigation == key.Replace("root:", "")
                        ? "M4,16 L12,8 L20,16"
                        : "M4,8 L12,16 L20,8"
                );
        }
        previousRoot = route == "logs" ? null : rootKey;
        previousPage = route == "logs" ? null : selected;
    }
}
