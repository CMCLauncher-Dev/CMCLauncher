using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CMCLauncher.Avalonia.ViewModels;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow : Window
{
    readonly LauncherSession session;
    readonly LayoutTransformControl scaling = new();
    readonly TransitioningContentControl pageContent = new()
    {
        PageTransition = new global::Avalonia.Animation.CrossFade(TimeSpan.FromMilliseconds(320)),
    };
    readonly SplitView drawer = new()
    {
        DisplayMode = SplitViewDisplayMode.Overlay,
        PanePlacement = SplitViewPanePlacement.Right,
        OpenPaneLength = 380,
    };
    readonly StackPanel navigation = new() { Spacing = 4 };
    readonly TextBlock pageTitle = new() { FontSize = 26, FontWeight = FontWeight.SemiBold };
    readonly TextBlock status = new()
    {
        MaxLines = 1,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    readonly ProgressBar progress = new()
    {
        Minimum = 0,
        Maximum = 100,
        Height = 4,
    };
    readonly Border toast = new()
    {
        CornerRadius = new CornerRadius(24),
        Padding = new Thickness(16, 8),
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    readonly Border sidebar;
    CancellationTokenSource? navigationRequest;
    long generation;
    string route = "home";
    bool closing,
        closeSaved;

    string Palette(string token, string fallback)
    {
        var settings = session.Store.Config.Settings;
        var custom = Light ? settings.CustomLightColors : settings.CustomDarkColors;
        var role = token.Replace("Light", "").Replace("Dark", "") switch
        {
            "Bg" => "background",
            "Sidebar" => "sidebar",
            "Surface" => "surface",
            "Text" => "text",
            "Accent" => "accent",
            "Border" => "border",
            "Muted" => "muted",
            _ => token,
        };
        if (
            settings.ColorPreset == "custom"
            && (custom.TryGetValue(role, out var value) || custom.TryGetValue(token, out value))
            && Color.TryParse(value, out _)
        )
            return value;
        return session.Themes.Resolve(settings.ColorPreset, token, Light, fallback);
    }

    public bool Verifying { get; }
    public string? VerificationDirectory { get; }
    IBrush Accent =>
        new SolidColorBrush(
            session.Store.Config.Settings.ColorPreset == "native"
                ? Application.Current?.PlatformSettings?.GetColorValues().AccentColor1
                    ?? Color.Parse("#0078D4")
                : Color.Parse(Palette(Light ? "LightAccent" : "DarkAccent", "#4C8150"))
        );
    bool Light =>
        session.Store.Config.Settings.Theme == "light"
        || session.Store.Config.Settings.Theme == "system"
            && ActualThemeVariant == ThemeVariant.Light;

    string Ui(string key) => session.Ui(key);

    public MainWindow()
    {
        var arguments = Program.Arguments;
        Verifying = arguments.Length == 2 && arguments[0] == "--verify-ui";
        if (Verifying)
            pageContent.PageTransition = null;
        VerificationDirectory = Verifying ? Path.GetFullPath(arguments[1]) : null;
        session = new LauncherSession(
            action => Dispatcher.UIThread.Post(action),
            Verifying ? Path.Combine(VerificationDirectory!, "data") : null,
            Verifying ? verificationTransport : null
        );
        Title = "CMCLauncher";
        MinWidth = 850;
        MinHeight = 600;
        Width = Math.Clamp(session.Store.Config.Settings.WindowWidth, 850, 2400);
        Height = Math.Clamp(session.Store.Config.Settings.WindowHeight, 600, 1600);
        if (
            session.Store.Config.Settings.WindowX is int x
            && session.Store.Config.Settings.WindowY is int y
        )
        {
            var saved = new PixelPoint(x, y);
            var area = (Screens.ScreenFromPoint(saved) ?? Screens.Primary)?.WorkingArea;
            if (area is { } work)
                Position = new PixelPoint(
                    Math.Clamp(x, work.X, Math.Max(work.X, work.Right - (int)Width)),
                    Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - (int)Height))
                );
        }
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (session.Store.Config.Settings.StartMaximized)
            WindowState = WindowState.Maximized;
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        sidebar = new Border
        {
            Padding = new Thickness(12),
            Child = new ScrollViewer { Content = navigation },
        };
        shell.Children.Add(sidebar);
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(28, 22),
        };
        body.Children.Add(pageTitle);
        var scroll = new ScrollViewer
        {
            Content = pageContent,
            Margin = new Thickness(0, 18, 0, 14),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        drawer.Content = scroll;
        Grid.SetRow(drawer, 1);
        body.Children.Add(drawer);
        var activity = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 6,
        };
        activity.Children.Add(status);
        Grid.SetRow(progress, 1);
        activity.Children.Add(progress);
        var cancel = Btn(
            "Cancel",
            () =>
            {
                session.Cancel();
                return Task.CompletedTask;
            }
        );
        cancel.IsVisible = false;
        progress.IsVisible = false;
        Grid.SetColumn(cancel, 1);
        Grid.SetRowSpan(cancel, 2);
        activity.Children.Add(cancel);
        Grid.SetRow(activity, 2);
        body.Children.Add(activity);
        Grid.SetRow(toast, 3);
        body.Children.Add(toast);
        Grid.SetColumn(body, 1);
        shell.Children.Add(body);
        scaling.Child = shell;
        Content = scaling;
        session.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName
                is nameof(session.Status)
                    or nameof(session.Progress)
                    or nameof(session.Busy)
            )
            {
                status.Text = session.Status;
                progress.IsVisible = session.Busy;
                progress.IsIndeterminate = !session.Progress.HasValue;
                progress.Value = session.Progress ?? 0;
                cancel.IsVisible = session.Busy;
            }
            if (e.PropertyName == nameof(session.GameRunning) && session.GameRunning)
            {
                switch (session.Store.Config.Settings.LauncherVisibility)
                {
                    case "minimize":
                        WindowState = WindowState.Minimized;
                        break;
                    case "hide_reopen":
                    case "hide_close":
                        Hide();
                        break;
                    case "close":
                        Close();
                        break;
                }
            }
            if (e.PropertyName == nameof(session.Toast))
                ShowToast(session.Toast);
        };
        session.RestartRequested += () =>
        {
            if (!session.ExitWatchdogRunning)
            {
                var host = LauncherSession.HostExecutable;
                var start = new ProcessStartInfo(host)
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                if (
                    Path.GetFileNameWithoutExtension(host)
                        .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                )
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                Process.Start(start)?.Dispose();
            }
            Close();
        };
        ApplyTheme();
        BuildNavigation();
        Opened += async (_, _) =>
        {
            try
            {
                await session.Core.RefreshImportedMetadata();
                await Navigate("home");
                if (Verifying)
                    await VerifyUi();
                else if (session.Store.Config.Settings.MinecraftDirectorySetupPending)
                    await FirstRun();
                if (!Verifying && session.Store.Config.Settings.CheckLauncherUpdates)
                    _ = CheckDesktopUpdates(false);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        PositionChanged += (_, _) => CapturePlacement();
        SizeChanged += (_, _) => CapturePlacement();
        Closing += async (_, e) =>
        {
            if (closeSaved)
                return;
            e.Cancel = true;
            if (closing)
                return;
            closing = true;
            try
            {
                CapturePlacement();
                await session.Save();
            }
            catch (Exception ex)
            {
                session.Log("ERROR", ex.ToString());
            }
            finally
            {
                closeSaved = true;
                telemetry.Dispose();
                session.Dispose();
                Close();
            }
        };
    }

    void CapturePlacement()
    {
        if (WindowState != WindowState.Normal)
            return;
        var settings = session.Store.Config.Settings;
        settings.WindowX = Position.X;
        settings.WindowY = Position.Y;
        settings.WindowWidth = (int)Width;
        settings.WindowHeight = (int)Height;
    }

    void BuildNavigation()
    {
        navigation.Children.Clear();
        navigation.Children.Add(T("CMCLauncher", 21, true));
        foreach (
            var entry in new (string Title, string Route)[]
            {
                ("Home", "home"),
                ("Instances", "instances"),
                ("Downloads", "downloads"),
                ("Mods", "mod"),
                ("Modpacks", "modpack"),
                ("Data packs", "datapack"),
                ("Resource packs", "resourcepack"),
                ("Shaders", "shader"),
                ("Maps", "world"),
                ("Blueprints", "blueprints"),
                ("Settings", "settings"),
                ("Appearance", "appearance"),
                ("Java", "java"),
                ("Accounts", "accounts"),
                ("Toolbox", "toolbox"),
                ("System status", "stats"),
                ("Tasks", "tasks"),
                ("About", "about"),
            }
        )
        {
            var button = Btn(entry.Title, () => Navigate(entry.Route));
            button.Tag = entry.Route;
            button.FontSize =
                session.Store.Config.Settings.UiFontSize
                * session.Store.Config.Settings.UiMenuScale;
            button.Background = entry.Route == route ? Accent : Brushes.Transparent;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            navigation.Children.Add(button);
        }
    }

    async Task Navigate(string target)
    {
        if (target == "tasks")
        {
            var tasks = Stack(
                T("Tasks", 20, true),
                Btn(
                    "Close",
                    () =>
                    {
                        drawer.IsPaneOpen = false;
                        return Task.CompletedTask;
                    }
                ),
                TasksPage()
            );
            tasks.Margin = new Thickness(16);
            var pane = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            pane.Children.Add(new ScrollViewer { Content = tasks });
            var logs = Btn("", () => Navigate("logs"));
            logs.Content = new global::Avalonia.Controls.Shapes.Path
            {
                Data = global::Avalonia.Media.Geometry.Parse(
                    "M3,1 L13,1 L17,5 L17,19 L3,19 Z M13,1 L13,5 L17,5 M6,9 L14,9 M6,12 L14,12 M6,15 L12,15"
                ),
                Width = 18,
                Height = 20,
            };
            ToolTip.SetTip(logs, Ui("Runtime logs"));
            global::Avalonia.Automation.AutomationProperties.SetName(logs, Ui("Runtime logs"));
            logs.HorizontalAlignment = HorizontalAlignment.Right;
            logs.Margin = new Thickness(12);
            Grid.SetRow(logs, 1);
            pane.Children.Add(logs);
            drawer.Pane = pane;
            drawer.IsPaneOpen = true;
            return;
        }
        navigationRequest?.Cancel();
        navigationRequest?.Dispose();
        navigationRequest = new();
        drawer.IsPaneOpen = false;
        var token = navigationRequest.Token;
        var revision = ++generation;
        route = target;
        session.Terracotta.Active = target == "terracotta";
        foreach (var button in navigation.Children.OfType<Button>())
        {
            var selected =
                button.Tag?.ToString()
                == (target is "frpc" or "pfp" or "terracotta" ? "toolbox" : target);
            button.Background = selected ? Accent : Brushes.Transparent;
            button.Foreground = selected ? Brushes.White : Foreground;
        }
        pageTitle.Text = Ui(
            target switch
            {
                "home" => "Home",
                "instances" => "Instances",
                "downloads" => "Download game",
                "blueprints" => "Blueprints",
                "settings" => "Settings",
                "appearance" => "Appearance",
                "java" => "Java",
                "accounts" => "Accounts",
                "logs" => "Runtime logs",
                "toolbox" => "Toolbox",
                "terracotta" => "Terracotta",
                "frpc" => "frpc",
                "pfp" => "MinecraftPFP",
                "stats" => "System status",
                "tasks" => "Tasks",
                "about" => "About",
                _ => "Community resources",
            }
        );
        if (pageContent.Content == null)
            pageContent.Content = T("Loading…");
        try
        {
            Control page = target switch
            {
                "home" => HomePage(),
                "instances" => await InstancesPage(),
                "downloads" => await DownloadsPage(token),
                "mod" or "modpack" or "datapack" or "resourcepack" or "shader" or "world" =>
                    CommunityPage(target),
                "blueprints" => BlueprintsPage(),
                "settings" => SettingsPage(),
                "appearance" => AppearancePage(),
                "java" => JavaPage(),
                "accounts" => AccountsPage(),
                "logs" => LogsPage(),
                "toolbox" => ToolboxPage(),
                "terracotta" => TerracottaPage(),
                "frpc" => await FrpcPage(token),
                "pfp" => PfpPage(),
                "stats" => StatsPage(),
                "tasks" => TasksPage(),
                "about" => AboutPage(),
                _ => T("Page not found"),
            };
            if (revision == generation)
                pageContent.Content = page;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (revision == generation)
                pageContent.Content = Stack(T(ex.Message), Btn("Retry", () => Navigate(target)));
            session.Log("ERROR", ex.ToString());
        }
    }

    void ApplyTheme()
    {
        var settings = session.Store.Config.Settings;
        RequestedThemeVariant =
            settings.Theme == "light" ? ThemeVariant.Light
            : settings.Theme == "dark" ? ThemeVariant.Dark
            : ThemeVariant.Default;
        string ColorValue(string token, string fallback) => Palette(token, fallback);
        Background = new SolidColorBrush(
            Color.Parse(ColorValue(Light ? "LightBg" : "DarkBg", Light ? "#F6F7F9" : "#17191F"))
        );
        Foreground = new SolidColorBrush(
            Color.Parse(Palette(Light ? "LightText" : "DarkText", Light ? "#202124" : "#F0F0F0"))
        );
        sidebar.Background = new SolidColorBrush(
            Color.Parse(
                ColorValue(Light ? "LightSidebar" : "DarkSidebar", Light ? "#EBEFF3" : "#20232B")
            )
        );
        FontSize = Math.Clamp(settings.UiFontSize, 10, 24);
        if (!string.IsNullOrWhiteSpace(settings.UiFontFamily))
            FontFamily = new FontFamily(settings.UiFontFamily);
        scaling.LayoutTransform = new ScaleTransform(settings.UiScale, settings.UiScale);
        Resources["SystemAccentColor"] = ((SolidColorBrush)Accent).Color;
        Resources["SystemControlHighlightAccentBrush"] = Accent;
        if (
            Application
                .Current?.Styles.OfType<global::Avalonia.Themes.Fluent.FluentTheme>()
                .FirstOrDefault() is
            { } fluent
        )
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var palette = new global::Avalonia.Themes.Fluent.ColorPaletteResources
                {
                    Accent = ((SolidColorBrush)Accent).Color,
                };
                fluent.Palettes[variant] = palette;
            }
        }
        toast.Background = Accent;
    }

    TextBlock T(string text, double size = 14, bool bold = false) =>
        new()
        {
            Text = Ui(text),
            FontSize = size * session.Store.Config.Settings.UiFontSize / 14,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
        };

    StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 10, 8);
            row.Children.Add(control);
        }
        return row;
    }

    Border Card(Control child) =>
        new()
        {
            Child = child,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Light
                ? new SolidColorBrush(Color.Parse("#D9DEE3"))
                : new SolidColorBrush(Color.Parse("#3C414B")),
            Background = new SolidColorBrush(
                Color.Parse(
                    Palette(Light ? "LightSurface" : "DarkSurface", Light ? "#FFFFFF" : "#242730")
                )
            ),
        };

    TextBox Input(string text = "", string? watermark = null, bool multiline = false) =>
        new()
        {
            Text = text,
            Watermark = watermark == null ? null : Ui(watermark),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 180 : 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    Control Field(string name, Control input) => Stack(T(name, 13), input);

    ComboBox Choice<T>(IEnumerable<T> values, T? selected = default)
    {
        var combo = new ComboBox
        {
            ItemsSource = values.ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 130,
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>(
                (value, _) =>
                    this.T(
                        value switch
                        {
                            ThemePreset theme => theme.DisplayName,
                            CustomAppearancePreset preset => preset.Name,
                            CommunityCategory category => category.DisplayName ?? category.Name,
                            JavaInstallation java => java.Version + " · " + java.Vendor,
                            Account account => account.Name
                                + " · "
                                + (
                                    account.Type == "offline" ? Ui("Offline identity")
                                    : account.Type == "microsoft" ? "Microsoft"
                                    : account.AuthServer
                                ),
                            _ => value?.ToString() ?? "",
                        }
                    )
            ),
        };
        if (selected != null)
            combo.SelectedItem = selected;
        if (combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
        return combo;
    }

    Button Btn(string title, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = Ui(title),
            Padding = new Thickness(
                14 * session.Store.Config.Settings.UiButtonScale,
                8 * session.Store.Config.Settings.UiButtonScale
            ),
            MinHeight = 38 * session.Store.Config.Settings.UiButtonScale,
            CornerRadius = new CornerRadius(5),
        };
        if (primary)
        {
            button.Background = Accent;
            button.Foreground = Brushes.White;
        }
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    async Task Error(Exception exception)
    {
        session.Log("ERROR", exception.ToString());
        await Dialog("Operation incomplete", Stack(T(exception.Message)), false);
    }

    async Task<bool> Confirm(string title, string message) =>
        await Dialog(title, Stack(T(message)), true);

    async Task<bool> Dialog(string title, Control content, bool confirm)
    {
        var window = new Window
        {
            Title = Ui(title),
            Width = 540,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            RequestedThemeVariant = RequestedThemeVariant,
            Background = Background,
            Foreground = Foreground,
        };
        var panel = Stack(T(title, 20, true), content);
        panel.Margin = new Thickness(22);
        panel.Children.Add(
            Row(
                Btn(
                    confirm ? "Confirm" : "OK",
                    () =>
                    {
                        window.Close(true);
                        return Task.CompletedTask;
                    },
                    true
                ),
                Btn(
                    "Cancel",
                    () =>
                    {
                        window.Close(false);
                        return Task.CompletedTask;
                    }
                )
            )
        );
        window.Content = new ScrollViewer { Content = panel };
        return await window.ShowDialog<bool>(this);
    }

    async Task<string?> OpenFile(params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter =
                    patterns.Length == 0
                        ? null
                        : [new FilePickerFileType("Files") { Patterns = patterns }],
            }
        );
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task<string?> Folder() =>
        (
            await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { AllowMultiple = false }
            )
        )
            .FirstOrDefault()
            ?.TryGetLocalPath();

    async Task<string?> SaveFile(string name, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                SuggestedFileName = name,
                DefaultExtension = extension.TrimStart('.'),
                FileTypeChoices =
                [
                    new FilePickerFileType(extension) { Patterns = ["*" + extension] },
                ],
            }
        );
        return file?.TryGetLocalPath();
    }

    static void Open(string path) =>
        Process.Start(
            CMCLauncher.Platform.PlatformCommand.SystemEnvironment(
                new ProcessStartInfo(path) { UseShellExecute = true }
            )
        );

    async void ShowToast(string text)
    {
        toast.Child = T(text);
        toast.IsVisible = true;
        await Task.Delay(4000);
        if (session.Toast == text)
            toast.IsVisible = false;
    }
}
