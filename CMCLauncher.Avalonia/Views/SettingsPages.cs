using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal step = 1) =>
        new()
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = step,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    Control SettingsPage()
    {
        var settings = session.Store.Config.Settings;
        var languages = session.Locales.Languages.ToArray();
        var language = Choice(
            languages,
            languages.FirstOrDefault(l => l.Code == settings.LauncherLanguage)
        );
        var memory = Number(settings.MemoryMb, 512, 65536, 256);
        var autoMemory = new CheckBox
        {
            Content = Ui("Automatic memory allocation"),
            IsChecked = settings.AutoMemory,
        };
        var threads = Number(settings.DownloadThreads, 1, 256);
        var source = Choice(
            new[] { "official", "auto", "mirror" },
            settings.EffectiveGameDownloadSource
        );
        var proxyMode = Choice(new[] { "system", "direct", "custom" }, NetworkProxy.Mode(settings));
        var address = Input(settings.Proxy, "http://127.0.0.1:7890");
        var username = Input(settings.ProxyUsername);
        var password = Input();
        password.PasswordChar = '●';
        var clearPassword = new CheckBox { Content = Ui("Clear saved proxy password") };
        var bypass = Input(settings.ProxyBypassHosts);
        var bypassLocal = new CheckBox
        {
            Content = Ui("Bypass proxy for local addresses"),
            IsChecked = settings.ProxyBypassLocal,
        };
        var fallback = new CheckBox
        {
            Content = Ui("Use direct connection if proxy is unavailable"),
            IsChecked = settings.ProxyDirectFallback,
        };
        var ssl = new CheckBox
        {
            Content = Ui("Ignore SSL certificate errors"),
            IsChecked = settings.IgnoreSslErrors,
        };
        var jvm = Input(settings.CustomJvmArgs, multiline: true);
        var gameLanguage = Input(settings.GameLanguage);
        var windowTitle = Input(settings.GameWindowTitle);
        var priority = Choice(
            new[] { "normal", "high", "above", "below", "idle", "realtime" },
            settings.PlayerPriority
        );
        var gpu = new CheckBox
        {
            Content = Ui("Prefer high-performance GPU"),
            IsChecked = settings.PreferHighPerformanceGpu,
        };
        var maximize = new CheckBox
        {
            Content = Ui("Start maximized"),
            IsChecked = settings.StartMaximized,
        };
        var visibility = Choice(
            new[] { "keep", "minimize", "hide_reopen", "hide_close", "close" },
            settings.LauncherVisibility
        );
        var checkUpdates = new CheckBox
        {
            Content = Ui("Check for updates on startup"),
            IsChecked = settings.CheckLauncherUpdates,
        };
        var beta = new CheckBox
        {
            Content = Ui("Receive Beta releases"),
            IsChecked = settings.ReceiveBetaUpdates,
        };
        var mirror = Input(settings.DownloadMirror);
        var introduction = new CheckBox
        {
            Content = Ui("Expand project introduction"),
            IsChecked = settings.ExpandProjectIntroduction,
        };
        return Stack(
            Card(
                Stack(
                    Field("Language", language),
                    maximize,
                    Field("Launcher visibility", visibility),
                    checkUpdates,
                    beta,
                    Field("Update download mirror", mirror),
                    introduction
                )
            ),
            Card(
                Stack(
                    T("Game", 18, true),
                    autoMemory,
                    Field("Memory / MiB", memory),
                    Field("Game language", gameLanguage),
                    Field("Java arguments", jvm),
                    Field("Game window title", windowTitle),
                    Field("Process priority", priority),
                    gpu
                )
            ),
            Card(
                Stack(
                    T("Network", 18, true),
                    Field("Download source", source),
                    Field("Concurrent downloads", threads),
                    Field("Proxy mode", proxyMode),
                    Field("Proxy address", address),
                    new Expander
                    {
                        Header = Ui("Advanced proxy settings"),
                        Content = Stack(
                            Field("Username", username),
                            Field("Password", password),
                            clearPassword,
                            Field("Bypass hosts", bypass),
                            bypassLocal,
                            fallback
                        ),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    },
                    ssl
                )
            ),
            Btn(
                "Save",
                async () =>
                {
                    var mode = proxyMode.SelectedItem?.ToString() ?? "system";
                    if (mode == "custom")
                        _ = NetworkProxy.Address(address.Text ?? "");
                    settings.LauncherLanguage =
                        (language.SelectedItem as InterfaceLanguage)?.Code ?? "en_us";
                    settings.MemoryMb = (int)(memory.Value ?? 2048);
                    settings.AutoMemory = autoMemory.IsChecked == true;
                    settings.DownloadThreads = (int)(threads.Value ?? 16);
                    settings.GameDownloadSource = source.SelectedItem?.ToString() ?? "official";
                    settings.ProxyMode = mode;
                    settings.Proxy = address.Text ?? "";
                    settings.ProxyUsername = username.Text ?? "";
                    settings.ProxyBypassHosts = bypass.Text ?? "";
                    settings.ProxyBypassLocal = bypassLocal.IsChecked == true;
                    settings.ProxyDirectFallback = fallback.IsChecked == true;
                    if (clearPassword.IsChecked == true)
                        settings.ProxyPasswordProtected = null;
                    if (!string.IsNullOrEmpty(password.Text))
                        settings.ProxyPasswordProtected = NetworkProxy.ProtectPassword(
                            password.Text
                        );
                    settings.IgnoreSslErrors = ssl.IsChecked == true;
                    settings.CustomJvmArgs = jvm.Text ?? "";
                    settings.GameLanguage = gameLanguage.Text ?? "auto";
                    settings.GameWindowTitle = windowTitle.Text ?? "";
                    settings.PlayerPriority = priority.SelectedItem?.ToString() ?? "normal";
                    if (settings.PreferHighPerformanceGpu && gpu.IsChecked != true)
                        await GameGpuPreference.Restore(session.Store);
                    settings.PreferHighPerformanceGpu = gpu.IsChecked == true;
                    settings.StartMaximized = maximize.IsChecked == true;
                    settings.LauncherVisibility = visibility.SelectedItem?.ToString() ?? "keep";
                    settings.CheckLauncherUpdates = checkUpdates.IsChecked == true;
                    settings.ReceiveBetaUpdates = beta.IsChecked == true;
                    settings.DownloadMirror = mirror.Text ?? "";
                    settings.ExpandProjectIntroduction = introduction.IsChecked == true;
                    await session.Save();
                    session.ConfigureNetwork();
                    BuildNavigation();
                    await Navigate("settings");
                },
                true
            ),
            FrpcClientSettings(),
            TerracottaSettingsCard()
        );
    }

    Control AppearancePage()
    {
        var settings = session.Store.Config.Settings;
        var theme = Choice(new[] { "system", "light", "dark" }, settings.Theme);
        var presets = new[]
        {
            new ThemePreset("native", "WinUI native", true),
            new ThemePreset("custom", "Custom", false),
        }
            .Concat(session.Themes.Presets)
            .ToArray();
        var preset = Choice(presets, presets.FirstOrDefault(p => p.Id == settings.ColorPreset));
        var scale = Number((decimal)(settings.UiScale * 100), 70, 200, 5);
        var buttons = Number((decimal)(settings.UiButtonScale * 100), 70, 200, 5);
        var menus = Number((decimal)(settings.UiMenuScale * 100), 70, 200, 5);
        var font = Input(settings.UiFontFamily);
        var fontSize = Number((decimal)settings.UiFontSize, 10, 24);
        var saved = Choice(settings.CustomAppearancePresets);
        async Task Apply()
        {
            settings.Theme = theme.SelectedItem?.ToString() ?? "system";
            settings.ColorPreset = (preset.SelectedItem as ThemePreset)?.Id ?? "craft";
            settings.UiScale = (double)(scale.Value ?? 100) / 100;
            settings.UiButtonScale = (double)(buttons.Value ?? 100) / 100;
            settings.UiMenuScale = (double)(menus.Value ?? 100) / 100;
            settings.UiFontFamily = font.Text ?? "Inter";
            settings.UiFontSize = (double)(fontSize.Value ?? 14);
            await session.Save();
            ApplyTheme();
            await Navigate("appearance");
        }
        var colors = Stack();
        foreach (
            var (label, dictionary) in new[]
            {
                ("Light colors", settings.CustomLightColors),
                ("Dark colors", settings.CustomDarkColors),
            }
        )
        {
            var panel = Stack();
            foreach (
                var key in new[]
                {
                    "background",
                    "sidebar",
                    "surface",
                    "text",
                    "muted",
                    "border",
                    "accent",
                }
            )
            {
                var input = Input(dictionary.GetValueOrDefault(key, ""), "#RRGGBB");
                panel.Children.Add(Field(key, input));
                input.LostFocus += (_, _) =>
                {
                    if (global::Avalonia.Media.Color.TryParse(input.Text, out _))
                        dictionary[key] = input.Text!;
                    else if (string.IsNullOrWhiteSpace(input.Text))
                        dictionary.Remove(key);
                };
            }
            colors.Children.Add(new Expander { Header = Ui(label), Content = panel });
        }
        return Stack(
            Card(
                Stack(
                    Field("Appearance", theme),
                    Field("Preset", preset),
                    Field("UI scale / %", scale),
                    Field("Button scale / %", buttons),
                    Field("Menu scale / %", menus),
                    Field("Font family", font),
                    Field("Font size", fontSize),
                    colors,
                    Btn("Apply", Apply, true)
                )
            ),
            Card(
                Stack(
                    Field("Saved appearance presets", saved),
                    Row(
                        Btn(
                            "Load",
                            async () =>
                            {
                                if (saved.SelectedItem is not CustomAppearancePreset value)
                                    return;
                                settings.Theme = value.Theme;
                                settings.ColorPreset = value.ColorPreset;
                                settings.UiScale = value.UiScale;
                                settings.UiButtonScale = value.UiButtonScale;
                                settings.UiMenuScale = value.UiMenuScale;
                                settings.UiFontFamily = value.UiFontFamily;
                                settings.UiFontSize = value.UiFontSize;
                                settings.CustomLightColors = new(value.CustomLightColors);
                                settings.CustomDarkColors = new(value.CustomDarkColors);
                                await session.Save();
                                ApplyTheme();
                                await Navigate("appearance");
                            }
                        ),
                        Btn(
                            "Save as",
                            async () =>
                            {
                                var name = Input();
                                if (!await Dialog("Preset name", name, true))
                                    return;
                                settings.CustomAppearancePresets.Add(
                                    new CustomAppearancePreset
                                    {
                                        Name = name.Text ?? "",
                                        Theme = settings.Theme,
                                        ColorPreset = settings.ColorPreset,
                                        UiScale = settings.UiScale,
                                        UiButtonScale = settings.UiButtonScale,
                                        UiMenuScale = settings.UiMenuScale,
                                        UiFontFamily = settings.UiFontFamily,
                                        UiFontSize = settings.UiFontSize,
                                        CustomLightColors = new(settings.CustomLightColors),
                                        CustomDarkColors = new(settings.CustomDarkColors),
                                    }
                                );
                                await session.Save();
                                await Navigate("appearance");
                            }
                        ),
                        Btn(
                            "Delete",
                            async () =>
                            {
                                if (saved.SelectedItem is CustomAppearancePreset value)
                                {
                                    settings.CustomAppearancePresets.Remove(value);
                                    await session.Save();
                                    await Navigate("appearance");
                                }
                            }
                        )
                    )
                )
            )
        );
    }

    Control JavaPage()
    {
        var list = Stack();
        var major = Choice(new[] { 8, 17, 21, 25 }, 21);
        void Draw()
        {
            list.Children.Clear();
            foreach (var java in session.Store.Config.Settings.JavaInstallations.ToArray())
                list.Children.Add(
                    Card(
                        Stack(
                            T(java.Version + " · " + java.Vendor, 15, true),
                            T(java.Path, 12),
                            Row(
                                Btn(
                                    "Use Java",
                                    async () =>
                                    {
                                        session.Store.Config.Settings.JavaPath = java.Path;
                                        await session.Save();
                                    }
                                ),
                                Btn(
                                    "Remove from list",
                                    async () =>
                                    {
                                        JavaManager.Remove(session.Store.Config.Settings, java);
                                        await session.Save();
                                        Draw();
                                    }
                                )
                            )
                        )
                    )
                );
        }
        Draw();
        return Stack(
            Row(
                Btn(
                    "Scan Java",
                    async () =>
                    {
                        await session.Run(
                            "Scan Java",
                            async ct =>
                            {
                                var candidates = await Task.Run(
                                    () => JavaManager.Candidates(session.Store, null, ct).ToArray(),
                                    ct
                                );
                                foreach (var path in candidates)
                                {
                                    try
                                    {
                                        JavaManager.Register(
                                            session.Store.Config.Settings,
                                            await JavaManager.Inspect(path, ct)
                                        );
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        throw;
                                    }
                                    catch (Exception ex)
                                    {
                                        session.Log("DEBUG", ex.Message);
                                    }
                                }
                                await session.Save();
                            }
                        );
                        Draw();
                    }
                ),
                Btn(
                    "Import Java",
                    async () =>
                    {
                        var path = await OpenFile();
                        if (path != null)
                        {
                            await session.Run(
                                "Import Java",
                                async ct =>
                                {
                                    JavaManager.Register(
                                        session.Store.Config.Settings,
                                        await JavaManager.Inspect(path, ct)
                                    );
                                    await session.Save();
                                }
                            );
                            Draw();
                        }
                    }
                )
            ),
            Row(
                major,
                Btn(
                    "Download Temurin",
                    async () =>
                    {
                        await session.Run(
                            "Download Java",
                            async ct =>
                            {
                                var java = await JavaManager.Download(
                                    session.Core,
                                    session.Store,
                                    (int)major.SelectedItem!,
                                    ct,
                                    value => session.Log("INFO", value)
                                );
                                JavaManager.Register(session.Store.Config.Settings, java);
                                await session.Save();
                            }
                        );
                        Draw();
                    },
                    true
                )
            ),
            list
        );
    }

    Control AccountsPage()
    {
        var body = Stack();
        var code = T("");
        var signInUrl = "https://www.microsoft.com/link";
        body.Children.Add(
            Card(
                Stack(
                    T("Microsoft", 18, true),
                    code,
                    Row(
                        Btn(
                            "Sign in with Microsoft",
                            async () =>
                            {
                                await session.Run(
                                    "Microsoft sign-in",
                                    async ct =>
                                    {
                                        var identity = await new MicrosoftAuth(session.Core).Login(
                                            "",
                                            (value, url) =>
                                                Dispatcher.UIThread.Post(() =>
                                                {
                                                    code.Text = value + "\n" + url;
                                                    signInUrl = url;
                                                }),
                                            ct
                                        );
                                        session.Store.Config.Accounts.RemoveAll(a =>
                                            a.Type == identity.Type && a.Uuid == identity.Uuid
                                        );
                                        session.Store.Config.Accounts.Add(identity);
                                        session.Account = identity;
                                        await session.Save();
                                    }
                                );
                                await Navigate("accounts");
                            },
                            true
                        ),
                        Btn(
                            "Open sign-in page",
                            () =>
                            {
                                Open(signInUrl);
                                return Task.CompletedTask;
                            }
                        )
                    )
                )
            )
        );
        var offlineName = Input("", "Player name");
        var offline = Btn(
            "Add offline account",
            async () =>
            {
                if (!MicrosoftAuth.CanUseOffline(session.Store.Config.Accounts))
                    throw new IOException(MicrosoftAuth.OfflineLockedMessage);
                var identity = Account.Offline(offlineName.Text?.Trim() ?? "");
                session.Store.Config.Accounts.Add(identity);
                session.Account = identity;
                await session.Save();
                await Navigate("accounts");
            }
        );
        offline.IsEnabled = MicrosoftAuth.CanUseOffline(session.Store.Config.Accounts);
        body.Children.Add(Card(Stack(T("Offline identity", 18, true), offlineName, offline)));
        var server = Input("https://littleskin.cn/api/yggdrasil");
        var login = Input();
        var password = Input();
        password.PasswordChar = '●';
        body.Children.Add(
            Card(
                new Expander
                {
                    Header = Ui("External sign-in (authlib-injector)"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = Stack(
                        Field("Authentication server", server),
                        Field("Username", login),
                        Field("Password", password),
                        Btn(
                            "Sign in",
                            async () =>
                            {
                                await session.Run(
                                    "External sign-in",
                                    async ct =>
                                    {
                                        using var external = new ExternalAuth(
                                            session.Store.Config.Settings
                                        );
                                        var identity = await external.Login(
                                            session.Store.Config.Accounts,
                                            server.Text ?? "",
                                            login.Text ?? "",
                                            password.Text ?? "",
                                            async profiles =>
                                            {
                                                var picker = Choice(profiles);
                                                return await Dialog("Choose profile", picker, true)
                                                    ? picker.SelectedItem as ExternalProfile
                                                    : null;
                                            },
                                            ct
                                        );
                                        password.Text = "";
                                        session.Store.Config.Accounts.Add(identity);
                                        session.Account = identity;
                                        await session.Save();
                                    }
                                );
                                await Navigate("accounts");
                            },
                            true
                        )
                    ),
                }
            )
        );
        foreach (var identity in session.Store.Config.Accounts.ToArray())
            body.Children.Add(
                Card(
                    Stack(
                        T(identity.ToString(), 16, true),
                        Row(
                            Btn(
                                "Use account",
                                async () =>
                                {
                                    session.Account = identity;
                                    await session.Save();
                                }
                            ),
                            Btn(
                                "Remove account",
                                async () =>
                                {
                                    if (!await Confirm("Remove account?", identity.Name))
                                        return;
                                    session.Store.Config.Accounts.Remove(identity);
                                    if (session.Account == identity)
                                        session.Account =
                                            session.Store.Config.Accounts.FirstOrDefault();
                                    await session.Save();
                                    await Navigate("accounts");
                                }
                            )
                        )
                    )
                )
            );
        return body;
    }

    Control LogsPage()
    {
        var debug = new CheckBox
        {
            Content = Ui("DEBUG mode"),
            IsChecked = session.Store.Config.Settings.DebugLogging,
        };
        debug.IsCheckedChanged += async (_, _) =>
        {
            session.Store.Config.Settings.DebugLogging = debug.IsChecked == true;
            await session.Save();
        };
        var list = new ListBox
        {
            ItemsSource = session.Logs,
            MinHeight = 400,
            MaxHeight = 650,
        };
        return Stack(
            Row(
                debug,
                Btn(
                    "Copy logs",
                    async () =>
                    {
                        if (Clipboard != null)
                            await Clipboard.SetTextAsync(
                                string.Join(Environment.NewLine, session.Logs)
                            );
                    }
                ),
                Btn(
                    "Export logs",
                    async () =>
                    {
                        var path = await SaveFile("CMCLauncher-log", ".log");
                        if (path != null)
                            await File.WriteAllLinesAsync(path, session.Logs);
                    }
                ),
                Btn(
                    "Clear",
                    () =>
                    {
                        session.Logs.Clear();
                        return Task.CompletedTask;
                    }
                )
            ),
            list
        );
    }

    Control TasksPage()
    {
        var list = new ItemsControl { ItemsSource = session.Operations };
        list.ItemTemplate =
            new global::Avalonia.Controls.Templates.FuncDataTemplate<CMCLauncher.Avalonia.ViewModels.OperationState>(
                (item, _) =>
                {
                    var stage = T("");
                    stage.Bind(
                        TextBlock.TextProperty,
                        new global::Avalonia.Data.Binding(nameof(item.Stage))
                    );
                    var bar = new ProgressBar
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Height = 5,
                    };
                    bar.Bind(
                        ProgressBar.ValueProperty,
                        new global::Avalonia.Data.Binding(nameof(item.Percent))
                    );
                    bar.Bind(
                        ProgressBar.IsIndeterminateProperty,
                        new global::Avalonia.Data.Binding(nameof(item.IsIndeterminate))
                    );
                    var panel = Stack(T(item.Title, 17, true), stage, bar);
                    var received = T("", 12);
                    received.Bind(
                        TextBlock.TextProperty,
                        new global::Avalonia.Data.Binding(nameof(item.ReceivedText))
                    );
                    received.Bind(
                        IsVisibleProperty,
                        new global::Avalonia.Data.Binding(nameof(item.HasReceived))
                    );
                    panel.Children.Add(received);
                    var elapsed = T("", 12);
                    elapsed.Bind(
                        TextBlock.TextProperty,
                        new global::Avalonia.Data.Binding(nameof(item.Elapsed))
                    );
                    panel.Children.Add(elapsed);
                    if (item == session.Active)
                    {
                        var cancel = Btn(
                            "Cancel",
                            () =>
                            {
                                if (session.Active == item)
                                    session.Cancel();
                                return Task.CompletedTask;
                            }
                        );
                        cancel.Bind(
                            IsVisibleProperty,
                            new global::Avalonia.Data.Binding(nameof(item.IsRunning))
                        );
                        cancel.HorizontalAlignment = HorizontalAlignment.Right;
                        panel.Children.Add(cancel);
                    }
                    return Card(panel);
                }
            );
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            foreach (var item in session.Operations)
                item.Tick();
        };
        list.AttachedToVisualTree += (_, _) => timer.Start();
        list.DetachedFromVisualTree += (_, _) => timer.Stop();
        return list;
    }
}
