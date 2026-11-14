using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMCLauncher.Avalonia.ViewModels;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    static readonly DataFormat<string> homeCardDragFormat =
        DataFormat.CreateStringApplicationFormat("CMCLauncher.HomeCard");
    bool homeCatalogDragging;
    (string Id, string? Target, bool After)? pendingHomeDrop;

    bool homeCustomizeMode;
    static readonly (string Id, string Title)[] homeCardCatalog =
    [
        ("account", "账号"),
        ("game", "游戏"),
        ("performance", "性能监视器"),
        ("note", "笔记"),
        ("news", "Minecraft 新闻"),
    ];

    Control HomePage()
    {
        var settings = session.Store.Config.Settings;
        settings.HomeCards ??= [];
        if (settings.HomeCards.Count == 0)
            settings.HomeCards.AddRange([new() { Id = "account" }, new() { Id = "game" }]);
        var area = Stack();
        area.MaxWidth = 860;
        area.HorizontalAlignment = HorizontalAlignment.Stretch;
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };
        if (homeCustomizeMode)
            toolbar.Children.Add(Btn("Add cards", OpenHomeCardDrawer));
        var edit = Btn(
            homeCustomizeMode ? "Done" : "Customize home",
            async () =>
            {
                homeCustomizeMode = !homeCustomizeMode;
                await Navigate("home");
            }
        );
        edit.Name = "HomeCustomizeButton";
        Grid.SetColumn(edit, 1);
        toolbar.Children.Add(edit);
        area.Children.Add(toolbar);
        var dashboard = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
            RowSpacing = 12,
            Name = "HomeDashboard",
        };
        DragDrop.SetAllowDrop(dashboard, homeCustomizeMode);
        dashboard.AddHandler(
            DragDrop.DragOverEvent,
            (_, e) =>
            {
                if (
                    !homeCustomizeMode
                    || !homeCatalogDragging
                    || e.DataTransfer.TryGetValue(homeCardDragFormat) is not string id
                )
                    return;
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
                var point = e.GetPosition(dashboard);
                foreach (var child in dashboard.Children)
                    child.Opacity =
                        child.Bounds.Contains(point) && child.Tag?.ToString() != id ? .65 : 1;
            },
            RoutingStrategies.Bubble,
            true
        );
        dashboard.AddHandler(
            DragDrop.DropEvent,
            (_, e) =>
            {
                if (
                    !homeCustomizeMode
                    || !homeCatalogDragging
                    || e.DataTransfer.TryGetValue(homeCardDragFormat) is not string id
                    || !homeCardCatalog.Any(c => c.Id == id)
                )
                    return;
                e.Handled = true;
                var point = e.GetPosition(dashboard);
                var target = dashboard.Children.FirstOrDefault(child =>
                    child.Bounds.Contains(point)
                );
                var after =
                    target != null
                    && (
                        Grid.GetColumnSpan(target) == 1
                            ? point.X > target.Bounds.Center.X
                            : point.Y > target.Bounds.Center.Y
                    );
                pendingHomeDrop = (id, target?.Tag?.ToString(), after);
            },
            RoutingStrategies.Bubble,
            true
        );
        var placements = settings
            .HomeCards.Where(p => homeCardCatalog.Any(c => c.Id == p.Id))
            .ToArray();
        foreach (var placement in placements)
        {
            var card = HomeCard(placement);
            card.Name = "HomeCard_" + placement.Id;
            card.MinHeight =
                placement.Id == "game"
                    ? 220 + (Math.Clamp(placement.Height, 1, 3) - 1) * 70
                    : 105 + (Math.Clamp(placement.Height, 1, 3) - 1) * 65;
            Control item = card;
            if (homeCustomizeMode)
            {
                var tools = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("32,32,32,*,*,32"),
                    ColumnSpacing = 6,
                    Name = "HomeCardTools",
                };
                var grip = new Border
                {
                    Width = 32,
                    Height = 32,
                    Background = Accent,
                    Opacity = .7,
                    CornerRadius = new CornerRadius(5),
                    Child = T("☰"),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                ToolTip.SetTip(grip, Ui("Drag to move"));
                tools.Children.Add(grip);
                var index = settings.HomeCards.IndexOf(placement);
                var up = HomeTool(
                    "↑",
                    "上移",
                    () =>
                        MoveHomeCard(
                            placement.Id,
                            index > 0 ? settings.HomeCards[index - 1].Id : placement.Id,
                            false
                        )
                );
                up.IsEnabled = index > 0;
                Grid.SetColumn(up, 1);
                tools.Children.Add(up);
                var down = HomeTool(
                    "↓",
                    "下移",
                    () =>
                        MoveHomeCard(
                            placement.Id,
                            index + 1 < settings.HomeCards.Count
                                ? settings.HomeCards[index + 1].Id
                                : placement.Id,
                            true
                        )
                );
                down.IsEnabled = index + 1 < settings.HomeCards.Count;
                Grid.SetColumn(down, 2);
                tools.Children.Add(down);
                var width = Choice(
                    new[] { "半宽", "全宽" },
                    placement.Width == 1 ? "半宽" : "全宽"
                );
                var height = Choice(
                    new[] { "紧凑", "标准", "宽松" },
                    new[] { "紧凑", "标准", "宽松" }[Math.Clamp(placement.Height, 1, 3) - 1]
                );
                width.MinWidth = height.MinWidth = 0;
                width.SelectionChanged += async (_, _) =>
                {
                    var previous = placement.Width;
                    placement.Width = width.SelectedIndex + 1;
                    try
                    {
                        await SaveHomeDashboard();
                    }
                    catch (Exception ex)
                    {
                        placement.Width = previous;
                        await Error(ex);
                    }
                };
                height.SelectionChanged += async (_, _) =>
                {
                    var previous = placement.Height;
                    placement.Height = height.SelectedIndex + 1;
                    try
                    {
                        await SaveHomeDashboard();
                    }
                    catch (Exception ex)
                    {
                        placement.Height = previous;
                        await Error(ex);
                    }
                };
                var widthField = Field("宽度", width);
                var heightField = Field("高度", height);
                Grid.SetColumn(widthField, 3);
                Grid.SetColumn(heightField, 4);
                tools.Children.Add(widthField);
                tools.Children.Add(heightField);
                if (placement.Id is not ("account" or "game"))
                {
                    var remove = HomeTool(
                        "×",
                        "删除",
                        async () =>
                        {
                            settings.HomeCards.Remove(placement);
                            await SaveHomeDashboard();
                        }
                    );
                    Grid.SetColumn(remove, 5);
                    tools.Children.Add(remove);
                }
                var wrapper = new StackPanel { Spacing = 8, Children = { tools, card } };
                item = wrapper;
                EnableHomeDrag(grip, placement.Id, dashboard);
                EnableHomeDrag(card, placement.Id, dashboard);
            }
            item.Tag = placement.Id;
            dashboard.Children.Add(item);
        }
        void ArrangeHome()
        {
            dashboard.RowDefinitions.Clear();
            var row = 0;
            var column = 0;
            var narrow = dashboard.Bounds.Width > 0 && dashboard.Bounds.Width < 560;
            for (var i = 0; i < placements.Length; i++)
            {
                var full = narrow || placements[i].Width != 1;
                if (full && column == 1)
                {
                    row++;
                    column = 0;
                }
                while (dashboard.RowDefinitions.Count <= row)
                    dashboard.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var item = dashboard.Children[i];
                Grid.SetRow(item, row);
                Grid.SetColumn(item, column);
                Grid.SetColumnSpan(item, full ? 2 : 1);
                if (full || column == 1)
                {
                    row++;
                    column = 0;
                }
                else
                    column = 1;
            }
        }
        dashboard.SizeChanged += (_, e) =>
        {
            if ((e.PreviousSize.Width < 560) != (e.NewSize.Width < 560))
                ArrangeHome();
        };
        ArrangeHome();
        area.Children.Add(dashboard);
        return area;
    }

    Button HomeTool(string glyph, string title, Func<Task> action)
    {
        var button = Btn("", action);
        button.Content = T(glyph, 14);
        button.Width = button.Height = button.MinWidth = button.MinHeight = 32;
        button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Bottom;
        ToolTip.SetTip(button, Ui(title));
        global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(title));
        return button;
    }

    async Task SaveHomeDashboard()
    {
        await session.Save();
        await Navigate("home");
    }

    async Task MoveHomeCard(string id, string? target, bool after)
    {
        var cards = session.Store.Config.Settings.HomeCards;
        var before = cards.ToArray();
        if (!HomeCardLayout.Move(cards, id, target, after))
            return;
        try
        {
            await SaveHomeDashboard();
        }
        catch
        {
            cards.Clear();
            cards.AddRange(before);
            throw;
        }
    }

    Task OpenHomeCardDrawer()
    {
        var body = Stack(
            T("Add cards", 20, true),
            Btn(
                "Close",
                () =>
                {
                    drawer.IsPaneOpen = false;
                    return Task.CompletedTask;
                }
            )
        );
        foreach (var entry in homeCardCatalog)
        {
            if (
                entry.Id is "account" or "game"
                || session.Store.Config.Settings.HomeCards.Any(p => p.Id == entry.Id)
            )
                continue;
            var add = Btn(
                Ui("Add") + " " + Ui(entry.Title),
                async () =>
                {
                    if (session.Store.Config.Settings.HomeCards.Any(p => p.Id == entry.Id))
                        return;
                    session.Store.Config.Settings.HomeCards.Add(
                        new HomeCardPlacement { Id = entry.Id, Width = 1 }
                    );
                    await SaveHomeDashboard();
                }
            );
            add.Tag = "home-add:" + entry.Id;
            var heading = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("32,*"),
                ColumnSpacing = 8,
            };
            var grip = new Border
            {
                Width = 32,
                Height = 32,
                Background = Accent,
                CornerRadius = new CornerRadius(5),
                Child = T("☰"),
            };
            ToolTip.SetTip(grip, Ui("Drag to move"));
            heading.Children.Add(grip);
            var title = T(entry.Title, 16, true);
            Grid.SetColumn(title, 1);
            heading.Children.Add(title);
            EnableCatalogDrag(grip, entry.Id);
            body.Children.Add(Card(Stack(heading, add)));
        }
        body.Margin = new Thickness(16);
        drawer.Pane = new ScrollViewer { Content = body };
        drawer.IsPaneOpen = true;
        return Task.CompletedTask;
    }

    void EnableCatalogDrag(Control handle, string id)
    {
        Point? origin = null;
        handle.PointerPressed += (_, e) =>
        {
            if (
                !homeCustomizeMode
                || session.Busy
                || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
            )
                return;
            origin = e.GetPosition(this);
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += async (_, e) =>
        {
            if (origin is not { } start || homeCatalogDragging)
                return;
            var point = e.GetPosition(this);
            if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) < 6)
                return;
            origin = null;
            homeCatalogDragging = true;
            pendingHomeDrop = null;
            e.Pointer.Capture(null);
            drawer.IsPaneOpen = false;
            try
            {
                var item = new DataTransferItem();
                item.Set(homeCardDragFormat, id);
                var data = new DataTransfer();
                data.Add(item);
                await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
                // Keep the drag source alive until the platform ends the native drag session.
                if (pendingHomeDrop is { } drop)
                    await MoveHomeCard(drop.Id, drop.Target, drop.After);
                else
                    drawer.IsPaneOpen = true;
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
            finally
            {
                homeCatalogDragging = false;
                pendingHomeDrop = null;
                foreach (
                    var child in pageContent
                        .GetVisualDescendants()
                        .OfType<Grid>()
                        .Where(grid => grid.Name == "HomeDashboard")
                        .SelectMany(grid => grid.Children)
                )
                    child.Opacity = 1;
            }
        };
        handle.PointerReleased += (_, e) =>
        {
            origin = null;
            e.Pointer.Capture(null);
        };
        handle.PointerCaptureLost += (_, _) => origin = null;
    }

    void EnableHomeDrag(Control handle, string id, Grid dashboard)
    {
        Point? origin = null;
        bool dragging = false;
        handle.PointerPressed += (_, e) =>
        {
            if (
                !homeCustomizeMode
                || session.Busy
                || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
            )
                return;
            if (
                handle is not Border { Child: TextBlock }
                && e.Source is Visual source
                && source
                    .GetVisualAncestors()
                    .TakeWhile(v => v != handle)
                    .Any(v => v is Button or TextBox or SelectingItemsControl)
            )
                return;
            origin = e.GetPosition(dashboard);
            dragging = false;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (origin is not { } start)
                return;
            var point = e.GetPosition(dashboard);
            if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) < 6)
                return;
            dragging = true;
            foreach (var child in dashboard.Children)
                child.Opacity =
                    child.Bounds.Contains(point) && child.Tag?.ToString() != id ? .65 : 1;
        };
        handle.PointerReleased += async (_, e) =>
        {
            var moved = dragging;
            var point = e.GetPosition(dashboard);
            var target = dashboard.Children.FirstOrDefault(c => c.Bounds.Contains(point));
            origin = null;
            dragging = false;
            e.Pointer.Capture(null);
            foreach (var child in dashboard.Children)
                child.Opacity = 1;
            if (!moved || target == null)
                return;
            e.Handled = true;
            try
            {
                await MoveHomeCard(
                    id,
                    target.Tag?.ToString(),
                    Grid.GetColumnSpan(target) == 1
                        ? point.X > target.Bounds.Center.X
                        : point.Y > target.Bounds.Center.Y
                );
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            origin = null;
            dragging = false;
            foreach (var child in dashboard.Children)
                child.Opacity = 1;
        };
    }

    Border HomeCard(HomeCardPlacement placement)
    {
        var settings = session.Store.Config.Settings;
        var body = new StackPanel { Spacing = 12 };
        Control content = body;
        if (placement.Id == "account")
        {
            var account =
                session.Account is { } candidate
                && MicrosoftAuth.CanUseAccount(session.Store.Config.Accounts, candidate)
                    ? candidate
                    : null;
            var identity = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("38,*,Auto"),
                ColumnSpacing = 12,
            };
            identity.Children.Add(AccountAvatar(account, 32));
            var info = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    T(account?.Name ?? "未登录", 18, true),
                    T(
                        account?.Type == "microsoft" ? "Microsoft 正版账号"
                            : account?.Type == "yggdrasil" ? "External account"
                            : account?.Type == "offline" ? "离线账号"
                            : "登录正版账号或添加离线身份",
                        11
                    ),
                },
            };
            Grid.SetColumn(info, 1);
            identity.Children.Add(info);
            var manage = Btn(account == null ? "添加账号" : "账号管理", () => Navigate("accounts"));
            Grid.SetColumn(manage, 2);
            identity.Children.Add(manage);
            body.Children.Add(identity);
            if (placement.Height >= 2)
            {
                var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
                body.Children.Remove(identity);
                layout.Children.Add(identity);
                var actions = HomeAccountActions(account);
                actions.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(actions, 1);
                layout.Children.Add(actions);
                content = layout;
            }
        }
        else if (placement.Id == "game")
        {
            var current = session.Selected;
            body.Children.Add(T("当前游戏版本", 12));
            var title = T(current?.Name ?? "还没有游戏版本", 23, true);
            title.MaxLines = 1;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(title);
            body.Children.Add(
                T(
                    current == null
                        ? "下载 Minecraft，或添加已有的 .minecraft 文件夹。"
                        : $"Minecraft {current.GameVersion} · {current.Loader}",
                    13
                )
            );
            body.Children.Add(
                Row(
                    Btn("切换版本", () => Navigate("instances")),
                    current == null
                        ? Btn("添加本地游戏", ImportLocal)
                        : Btn("版本设置", InstanceSettings)
                )
            );
            var state = T("", 12);
            var launchProgress = new ProgressBar
            {
                Tag = "home-launch-progress",
                Minimum = 0,
                Maximum = 100,
                Height = 5,
                Foreground = Accent,
            };
            var launch = Btn(
                current == null ? "下载游戏" : "启动游戏",
                current == null
                    ? () => Navigate("downloads")
                    : () => session.Run("Launch game", session.LaunchSelected),
                true
            );
            buttonAvailability.Add(launch, () => !session.Busy && !session.GameRunning);
            launch.MinWidth = 130;
            launch.HorizontalAlignment = HorizontalAlignment.Right;
            var footer = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12,
            };
            var cancel = HomeTool(
                "×",
                "取消启动",
                () =>
                {
                    session.Cancel();
                    return Task.CompletedTask;
                }
            );
            var launchPercent = T("0%", 12);
            launchPercent.MinWidth = 32;
            launchPercent.VerticalAlignment = VerticalAlignment.Center;
            cancel.Width = cancel.MinWidth = 28;
            cancel.Height = cancel.MinHeight = 28;
            cancel.Padding = new Thickness(5);
            var launchRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 8,
            };
            launchRow.Children.Add(launchProgress);
            Grid.SetColumn(launchPercent, 1);
            launchRow.Children.Add(launchPercent);
            Grid.SetColumn(cancel, 2);
            launchRow.Children.Add(cancel);
            var readiness = Stack(state, launchRow);
            footer.Children.Add(readiness);
            Grid.SetColumn(launch, 1);
            footer.Children.Add(launch);
            body.Children.Add(footer);
            void RefreshLaunch()
            {
                state.Text = session.Launching
                    ? session.Status
                    : Ui(
                        current == null ? "从选择版本开始"
                        : session.Account == null ? "添加账号后即可启动"
                        : "已准备就绪"
                    );
                launchRow.IsVisible = session.Launching;
                launchProgress.IsIndeterminate = false;
                launchPercent.Text = (session.Progress ?? 0).ToString("0") + "%";
                launchProgress.Value = session.Progress ?? 0;
                launch.IsEnabled = !session.Busy && !session.GameRunning;
            }
            void Changed(object? _, System.ComponentModel.PropertyChangedEventArgs e) =>
                RefreshLaunch();
            body.AttachedToVisualTree += (_, _) =>
            {
                session.PropertyChanged += Changed;
                RefreshLaunch();
            };
            body.DetachedFromVisualTree += (_, _) => session.PropertyChanged -= Changed;
            RefreshLaunch();
        }
        else if (placement.Id == "note")
        {
            body.Children.Add(T("笔记", 18, true));
            var note = Input(settings.HomeNote, "Write a note…", true);
            note.MinHeight = 80;
            note.LostFocus += async (_, _) =>
            {
                settings.HomeNote = note.Text ?? "";
                try
                {
                    await session.Save();
                }
                catch (Exception ex)
                {
                    await Error(ex);
                }
            };
            body.Children.Add(note);
        }
        else if (placement.Id == "news")
        {
            body.Children.Add(T("Minecraft 新闻", 18, true));
            body.Children.Add(T("浏览 Minecraft 官方新闻与更新动态。", 12));
            body.Children.Add(
                Row(
                    Btn(
                        "官方新闻",
                        () =>
                        {
                            Open("https://www.minecraft.net/en-us/articles");
                            return Task.CompletedTask;
                        }
                    ),
                    Btn(
                        "游戏更新",
                        () =>
                        {
                            Open("https://www.minecraft.net/en-us/updates");
                            return Task.CompletedTask;
                        }
                    )
                )
            );
        }
        else if (placement.Id == "performance")
        {
            body.Children.Add(T("性能监视器", 18, true));
            var app = T("", 12);
            var cpu = T("", 12);
            var memory = T("", 12);
            var cpuBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 5,
                Foreground = Accent,
            };
            var memoryBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 5,
                Foreground = Accent,
            };
            body.Children.Add(app);
            body.Children.Add(cpu);
            body.Children.Add(cpuBar);
            body.Children.Add(memory);
            body.Children.Add(memoryBar);
            var metrics = new SystemStatusState();
            void Update()
            {
                using var process = Process.GetCurrentProcess();
                app.Text = Ui("启动器内存") + $": {process.WorkingSet64 / 1048576d:N0} MB";
                cpu.Text =
                    Ui("CPU 占用")
                    + ": "
                    + (metrics.Cpu is double value ? $"{value:N1}%" : Ui("Unavailable"));
                cpuBar.Value = metrics.Cpu ?? 0;
                var physical = LauncherPlatform.Current.PhysicalMemory();
                var load = physical is { TotalMb: > 0 } pair
                    ? 100d * (pair.TotalMb - pair.AvailableMb) / pair.TotalMb
                    : 0;
                memory.Text =
                    Ui("设备内存") + ": " + (physical != null ? $"{load:N0}%" : Ui("Unavailable"));
                memoryBar.IsVisible = physical != null;
                memoryBar.Value = load;
            }
            body.AttachedToVisualTree += (_, _) =>
            {
                metrics.Updated += Update;
                metrics.Start(
                    action => Dispatcher.UIThread.Post(action),
                    Path.Combine(session.Store.Root, "cache", "gpu-adapters.json"),
                    Verifying
                );
                Update();
            };
            body.DetachedFromVisualTree += (_, _) =>
            {
                metrics.Updated -= Update;
                metrics.Dispose();
            };
        }
        var card = Card(content);
        card.Padding =
            placement.Id == "account" && placement.Height >= 2
                ? new Thickness(18, 18, 18, 0)
                : new Thickness(18);
        return card;
    }

    Grid HomeAccountActions(Account? account)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"),
            ColumnSpacing = 6,
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Name = "HomeAccountActions",
        };
        var definitions = new (string Title, string Path, string? Url)[]
        {
            (
                "Change skin",
                "M7,2 L4,3 L1,7 L4,10 L6,8 L6,22 L18,22 L18,8 L20,10 L23,7 L20,3 L17,2 C17,8 7,8 7,2 Z",
                "https://www.minecraft.net/en-us/msaprofile/mygames/editskin"
            ),
            (
                "Change cape",
                "M12,2 L21,6 L21,13 C21,18 16,21 12,23 C8,21 3,18 3,13 L3,6 Z",
                "https://www.minecraft.net/en-us/msaprofile/mygames/editskin"
            ),
            (
                "Change player name",
                "M4,17 L17,4 L20,4 L23,7 L23,10 L10,23 L3,24 Z M16,5 L22,11 M4,17 L10,23",
                "https://www.minecraft.net/en-us/msaprofile/mygames/editprofile"
            ),
            (
                "Refresh account",
                "M20,8 A9,9 0 0 0 4,6 M4,6 L4,2 M4,6 L8,6 M4,16 A9,9 0 0 0 20,18 M20,18 L20,22 M20,18 L16,18",
                null
            ),
            (
                "Add account",
                "M9,3 A4,4 0 1 0 9,11 A4,4 0 1 0 9,3 M2,21 L2,17 C2,12 16,12 16,17 M19,12 L19,22 M14,17 L24,17",
                null
            ),
            (
                "Redeem code",
                "M3,9 L21,9 L21,22 L3,22 Z M1,5 L23,5 L23,10 L1,10 Z M12,5 L12,22 M12,5 C3,-3 0,5 12,5 C24,-3 24,5 12,5",
                "https://www.minecraft.net/en-us/redeem"
            ),
        };
        for (var i = 0; i < definitions.Length; i++)
        {
            var index = i;
            var entry = definitions[i];
            var button = Btn(
                "",
                async () =>
                {
                    if (entry.Url != null)
                    {
                        Open(entry.Url);
                        return;
                    }
                    if (index == 4)
                    {
                        await Navigate("accounts");
                        return;
                    }
                    if (account == null)
                        return;
                    await session.Run(
                        "Refresh account",
                        async ct =>
                        {
                            Account refreshed;
                            if (account.Type == "microsoft")
                                refreshed = await new MicrosoftAuth(session.Core).Refresh(
                                    account,
                                    MicrosoftAuth.ConfiguredClientId,
                                    ct,
                                    force: true
                                );
                            else
                            {
                                using var external = new ExternalAuth(
                                    session.Store.Config.Settings
                                );
                                refreshed = await external.Refresh(
                                    session.Store.Config.Accounts,
                                    account,
                                    ct
                                );
                            }
                            session.Store.Config.Accounts.RemoveAll(a => a.Key == account.Key);
                            session.Store.Config.Accounts.Add(refreshed);
                            session.Account = refreshed;
                            await session.Save();
                        }
                    );
                    await Navigate("home");
                }
            );
            var icon = NavigationIcon(entry.Path);
            icon.Width = icon.Height = 18;
            button.Content = icon;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Height = button.MinHeight = 34;
            button.MinWidth = 0;
            button.Padding = new Thickness(4, 5);
            button.CornerRadius = new CornerRadius(8);
            ToolTip.SetTip(button, Ui(entry.Title));
            global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(entry.Title));
            bool Available() =>
                !session.Busy
                && (
                    index <= 2 ? account?.Type == "microsoft"
                    : index == 3 ? account?.Type is "microsoft" or "yggdrasil"
                    : true
                );
            buttonAvailability.Add(button, Available);
            void Availability() => button.IsEnabled = Available();
            void Changed(object? _, System.ComponentModel.PropertyChangedEventArgs e) =>
                Availability();
            button.AttachedToVisualTree += (_, _) =>
            {
                session.PropertyChanged += Changed;
                Availability();
            };
            button.DetachedFromVisualTree += (_, _) => session.PropertyChanged -= Changed;
            Availability();
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
        }
        return grid;
    }
}
