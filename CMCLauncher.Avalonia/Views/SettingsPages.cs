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
        void Labels(ComboBox combo, Dictionary<string, string> labels)
        {
            combo.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>(
                (value, _) =>
                    T(labels.GetValueOrDefault(value?.ToString() ?? "", value?.ToString() ?? ""))
            );
        }
        var memory = new Slider
        {
            Minimum = 512,
            Maximum = Math.Clamp(
                Math.Max(
                    LauncherPlatform.Current.PhysicalMemory()?.TotalMb ?? 65536,
                    settings.MemoryMb
                ),
                512,
                65536
            ),
            Value = settings.MemoryMb,
            SmallChange = 256,
            LargeChange = 1024,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
        };
        var memoryValue = T("", 16, true);
        var memoryProgress = new Border
        {
            Height = 10,
            MinWidth = 0,
            Background = Accent,
        };
        var usedMemory = T("", 13);
        var memoryTrack = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 0,
            Height = 10,
        };
        var usedSegment = new Border
        {
            Background = Accent,
            Opacity = .45,
            CornerRadius = new CornerRadius(3),
        };
        var freeSegment = new Border
        {
            Background = Accent,
            Opacity = .12,
            CornerRadius = new CornerRadius(3),
        };
        Grid.SetColumn(memoryProgress, 1);
        Grid.SetColumn(freeSegment, 2);
        memoryTrack.Children.Add(usedSegment);
        memoryTrack.Children.Add(memoryProgress);
        memoryTrack.Children.Add(freeSegment);
        var memoryLegend = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        memoryValue.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(memoryValue, 1);
        memoryLegend.Children.Add(usedMemory);
        memoryLegend.Children.Add(memoryValue);
        var autoMemory = new ToggleSwitch
        {
            Content = Ui("Automatic memory allocation"),
            IsChecked = settings.AutoMemory,
            OnContent = Ui("Automatic [context 467]"),
            OffContent = Ui("Manual"),
        };
        var memoryPanel = Stack(T("Memory / MiB"), memoryLegend, memoryTrack, memory);
        var memoryAnimation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var animationClock = new System.Diagnostics.Stopwatch();
        double[] animationStart = new double[3],
            animationTarget = new double[3];
        bool memoryDrawn = false;
        void DrawMemory(double[] values)
        {
            for (var i = 0; i < 3; i++)
                memoryTrack.ColumnDefinitions[i].Width = new GridLength(
                    values[i],
                    GridUnitType.Star
                );
        }
        memoryAnimation.Tick += (_, _) =>
        {
            var t = Math.Clamp(animationClock.Elapsed.TotalMilliseconds / 450, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            DrawMemory(
                Enumerable
                    .Range(0, 3)
                    .Select(i =>
                        animationStart[i] + (animationTarget[i] - animationStart[i]) * eased
                    )
                    .ToArray()
            );
            if (t >= 1)
                memoryAnimation.Stop();
        };
        void AnimateMemory((double Used, double Allocated, double Free) distribution)
        {
            var next = new[] { distribution.Used, distribution.Allocated, distribution.Free };
            // Retarget from the displayed widths when another update arrives during an animation.
            memoryAnimation.Stop();
            animationStart = memoryTrack.ColumnDefinitions.Select(c => c.Width.Value).ToArray();
            animationTarget = next;
            if (!memoryDrawn || !memoryPanel.IsLoaded)
                DrawMemory(next);
            else
            {
                animationClock.Restart();
                memoryAnimation.Start();
            }
            memoryDrawn = true;
        }
        var memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void RefreshMemory()
        {
            var automatic = autoMemory.IsChecked == true;
            memory.IsEnabled = !automatic;
            memory.IsVisible = !automatic;
            var physical = LauncherPlatform.Current.PhysicalMemory();
            var total = Math.Max(1, physical?.TotalMb ?? (long)memory.Maximum);
            var available = Math.Clamp(physical?.AvailableMb ?? total, 0, total);
            var used = total - available;
            usedMemory.Text = session.Locales.Format(
                settings.LauncherLanguage,
                "Used: {0:N1} / {1:N1} GB",
                used / 1024d,
                total / 1024d
            );
            var allocation = automatic
                ? GameMemory.Resolve(new Settings())
                : (int)Math.Round(memory.Value);
            var distribution = GameMemory.Distribution(total, available, allocation);
            AnimateMemory(distribution);
            usedSegment.IsVisible = used > 0;

            memoryValue.Text = session.Locales.Format(
                settings.LauncherLanguage,
                "Game allocation: {0:N1} GB",
                allocation / 1024d
            );
        }
        memoryTimer.Tick += (_, _) => RefreshMemory();
        memoryPanel.AttachedToVisualTree += (_, _) =>
        {
            RefreshMemory();
            if (autoMemory.IsChecked == true)
                memoryTimer.Start();
        };
        memoryPanel.DetachedFromVisualTree += (_, _) =>
        {
            memoryTimer.Stop();
            memoryAnimation.Stop();
        };
        autoMemory.IsCheckedChanged += (_, _) =>
        {
            RefreshMemory();
            if (autoMemory.IsChecked == true && memoryPanel.IsLoaded)
                memoryTimer.Start();
            else
                memoryTimer.Stop();
        };
        memory.ValueChanged += (_, _) => RefreshMemory();
        RefreshMemory();
        var threads = Number(settings.DownloadThreads, 1, 256);
        var source = Choice(
            new[] { "mirror", "auto", "official" },
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
        var ssl = new ToggleSwitch
        {
            Content = Ui("Ignore SSL certificate errors"),
            IsChecked = settings.IgnoreSslErrors,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        var jvm = Input(settings.CustomJvmArgs, "-Dexample=1 -XX:+UseG1GC");
        var gameLanguage = Choice(
            new[] { "auto", "zh_cn", "zh_tw", "en_us", "ja_jp", "preserve" },
            settings.GameLanguage
        );
        var windowTitle = Input(settings.GameWindowTitle);
        var priority = Choice(
            new[] { "normal", "above", "high", "realtime", "below", "idle" },
            settings.PlayerPriority
        );
        var gpu = new ToggleSwitch
        {
            Content = Ui("Prefer high-performance GPU"),
            IsChecked = settings.PreferHighPerformanceGpu,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        var maximize = Choice(
            new[] { "Windowed", "Maximized" },
            settings.StartMaximized ? "Maximized" : "Windowed"
        );
        var visibility = Choice(
            new[] { "keep", "close", "hide_close", "hide_reopen", "minimize" },
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
        var introduction = new ToggleSwitch
        {
            Content = Ui("Expand project introduction"),
            IsChecked = settings.ExpandProjectIntroduction,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        string NetworkSettings() =>
            JsonSerializer.Serialize(
                new
                {
                    ProxyMode = NetworkProxy.Mode(settings),
                    settings.Proxy,
                    settings.ProxyUsername,
                    settings.ProxyPasswordProtected,
                    settings.ProxyBypassHosts,
                    settings.ProxyBypassLocal,
                    settings.ProxyDirectFallback,
                    settings.IgnoreSslErrors,
                    GameDownloadSource = settings.EffectiveGameDownloadSource,
                    settings.DownloadMirror,
                }
            );
        Labels(
            source,
            new()
            {
                ["mirror"] = "Prefer mirror sources",
                ["auto"] = "Prefer official sources; switch to mirrors if slow",
                ["official"] = "Prefer official sources",
            }
        );
        Labels(
            proxyMode,
            new()
            {
                ["system"] = "System proxy",
                ["direct"] = "No proxy",
                ["custom"] = "Manual proxy",
            }
        );
        Labels(
            gameLanguage,
            new()
            {
                ["auto"] = "context.settingspage.system_default",
                ["zh_cn"] = "Simplified Chinese",
                ["zh_tw"] = "Traditional Chinese",
                ["en_us"] = "English",
                ["ja_jp"] = "Japanese",
                ["preserve"] = "context.settingspage.keep_current",
            }
        );
        var saveGate = new SemaphoreSlim(1, 1);
        async Task SaveSettings(bool rebuild = false, bool applyNetwork = false)
        {
            await saveGate.WaitAsync();
            try
            {
                var previousNetwork = NetworkSettings();
                var mode = proxyMode.SelectedItem?.ToString() ?? "system";
                if (applyNetwork && mode == "custom")
                    _ = NetworkProxy.Address(address.Text ?? "");
                settings.LauncherLanguage =
                    (language.SelectedItem as InterfaceLanguage)?.Code ?? "en_us";
                settings.MemoryMb = (int)Math.Round(memory.Value);
                settings.AutoMemory = autoMemory.IsChecked == true;
                settings.DownloadThreads = (int)(threads.Value ?? 16);
                settings.GameDownloadSource = source.SelectedItem?.ToString() ?? "official";
                if (applyNetwork)
                {
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
                }
                settings.CustomJvmArgs = (jvm.Text ?? "").Trim();
                settings.GameLanguage = gameLanguage.SelectedItem?.ToString() ?? "auto";
                settings.GameWindowTitle = (windowTitle.Text ?? "").TrimStart();
                settings.PlayerPriority = priority.SelectedItem?.ToString() ?? "normal";
                if (settings.PreferHighPerformanceGpu && gpu.IsChecked != true)
                    await GameGpuPreference.Restore(session.Store);
                settings.PreferHighPerformanceGpu = gpu.IsChecked == true;
                settings.StartMaximized = maximize.SelectedItem?.ToString() == "Maximized";
                settings.LauncherVisibility = visibility.SelectedItem?.ToString() ?? "keep";
                settings.CheckLauncherUpdates = checkUpdates.IsChecked == true;
                settings.ReceiveBetaUpdates = beta.IsChecked == true;
                var mirrorValue = (mirror.Text ?? "").Trim().TrimEnd('/');
                if (
                    mirrorValue.Length == 0
                    || Uri.TryCreate(mirrorValue, UriKind.Absolute, out var mirrorUri)
                        && mirrorUri.Scheme is "http" or "https"
                        && !string.IsNullOrWhiteSpace(mirrorUri.Host)
                )
                    settings.DownloadMirror = mirrorValue;
                settings.ExpandProjectIntroduction = introduction.IsChecked == true;
                await session.Save();
                if (NetworkSettings() != previousNetwork)
                    session.ConfigureNetwork();
                if (rebuild)
                {
                    BuildNavigation();
                    await Navigate("settings");
                }
            }
            finally
            {
                saveGate.Release();
            }
        }
        async Task PersistSettings()
        {
            try
            {
                await SaveSettings();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        }
        language.SelectionChanged += async (_, _) =>
        {
            if (
                language.SelectedItem is not InterfaceLanguage selected
                || selected.Code == settings.LauncherLanguage
            )
                return;
            try
            {
                await SaveSettings(rebuild: true);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        foreach (var combo in new[] { maximize, visibility, priority, gameLanguage, source })
            combo.SelectionChanged += async (_, _) => await PersistSettings();
        foreach (
            var toggle in new global::Avalonia.Controls.Primitives.ToggleButton[]
            {
                autoMemory,
                gpu,
                checkUpdates,
                beta,
                introduction,
            }
        )
            toggle.IsCheckedChanged += async (_, _) => await PersistSettings();
        foreach (var input in new[] { jvm, windowTitle, mirror })
            input.TextChanged += async (_, _) => await PersistSettings();
        memory.ValueChanged += async (_, _) => await PersistSettings();
        threads.ValueChanged += async (_, _) => await PersistSettings();
        address.IsEnabled = proxyMode.SelectedItem?.ToString() == "custom";
        proxyMode.SelectionChanged += async (_, _) =>
        {
            address.IsEnabled = proxyMode.SelectedItem?.ToString() == "custom";
            // Switching modes must not validate an unfinished manual address.
            if (address.IsEnabled)
                return;
            try
            {
                await SaveSettings(applyNetwork: true);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var proxyHint = T("", 12);
        proxyHint.IsVisible = false;
        async Task<bool> ApplyProxy()
        {
            try
            {
                await SaveSettings(applyNetwork: true);
                proxyHint.IsVisible = false;
                return true;
            }
            catch (Exception ex)
            {
                proxyHint.Text = ex.Message;
                proxyHint.IsVisible = true;
                return false;
            }
        }
        var applyProxy = Btn(
            "Apply proxy settings",
            async () =>
            {
                if (await ApplyProxy())
                    ShowToast(Ui("Proxy settings saved"));
            }
        );
        var testProxy = Btn(
            "Apply and test connection",
            async () =>
            {
                if (!await ApplyProxy())
                    return;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var selected = NetworkProxy.Create(settings);
                    using var transport = new System.Net.Http.HttpClientHandler
                    {
                        Proxy = selected,
                        UseProxy = selected != null,
                    };
                    using var client = new System.Net.Http.HttpClient(transport);
                    using var response = await client.GetAsync(
                        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                        System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token
                    );
                    response.EnsureSuccessStatusCode();
                    ShowToast(Ui("Connection test succeeded"));
                }
                catch (Exception ex)
                {
                    proxyHint.Text =
                        Ui(
                            ex is OperationCanceledException
                                ? "Connection test timed out."
                                : "Connection test failed."
                        )
                        + " "
                        + ex.Message;
                    proxyHint.IsVisible = true;
                }
            }
        );
        Control Section(string title, params Control[] controls) =>
            Card(Stack(new[] { T(title, 18, true) }.Cast<Control>().Concat(controls).ToArray()));
        return Stack(
            Section(
                "Language and interface",
                Field("Launcher language", language),
                Field("Default window mode", maximize),
                introduction
            ),
            Section(
                "Game launch",
                Field("Game language", gameLanguage),
                Field("Launcher visibility", visibility),
                Field("Custom game window title", windowTitle),
                gpu,
                Field("Process priority", priority)
            ),
            Section("Memory and Java", autoMemory, memoryPanel, Field("Custom JVM arguments", jvm)),
            Section(
                "Downloads and network",
                Field("Download threads", threads),
                Field("Game download source", source),
                Field("Proxy mode", proxyMode),
                Field("Proxy address", address),
                proxyHint,
                Field("Update download mirror", mirror),
                new Expander
                {
                    Header = Ui("Advanced network options"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = Stack(
                        Field("Username", username),
                        Field("Password", password),
                        clearPassword,
                        Field("Bypass hosts", bypass),
                        bypassLocal,
                        fallback,
                        Row(applyProxy, testProxy),
                        T(
                            "Authentication and bypass rules apply to manual proxy mode. Connection tests do not use direct fallback.",
                            12
                        ),
                        ssl,
                        T("Skipping certificate validation reduces connection security.", 12)
                    ),
                }
            ),
            Section(
                "Launcher updates",
                checkUpdates,
                beta,
                Btn("Check for updates", () => CheckDesktopUpdates(true))
            ),
            ThirdPartyApplicationsCard(),
            T(Ui("Current version:") + " " + LauncherUpdates.CurrentVersion, 12)
        );
    }

    Control ThirdPartyApplicationsCard()
    {
        var frpc = (Border)FrpcClientSettings();
        var frpcContent = frpc.Child!;
        frpc.Child = null;
        var terracotta = (Border)TerracottaSettingsCard();
        var terracottaContent = terracotta.Child!;
        terracotta.Child = null;
        return Card(
            Stack(T("Manage third-party applications", 18, true), frpcContent, terracottaContent)
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
                Btn(
                    "Use Java from PATH",
                    async () =>
                    {
                        var path = (Environment.GetEnvironmentVariable("PATH") ?? "")
                            .Split(Path.PathSeparator)
                            .Select(entry =>
                                Path.Combine(entry.Trim().Trim('"'), JavaManager.JavaExecutable)
                            )
                            .FirstOrDefault(File.Exists);
                        if (path == null)
                            throw new IOException(Ui("No Java installation was found on PATH."));
                        await session.Run(
                            "Import Java",
                            async ct =>
                            {
                                var java = await JavaManager.Inspect(path, ct);
                                JavaManager.Register(session.Store.Config.Settings, java);
                                session.Store.Config.Settings.JavaPath = java.Path;
                                await session.Save();
                            }
                        );
                        Draw();
                    }
                ),
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
                                    value =>
                                    {
                                        session.ReportStatus(value);
                                        session.Log("INFO", value);
                                    }
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
        var deviceCode = "";
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
                                                    deviceCode = value;
                                                    signInUrl = url;
                                                }),
                                            ct,
                                            session.ReportStatus
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
                            "复制设备代码",
                            async () =>
                            {
                                if (Clipboard != null && deviceCode.Length > 0)
                                    await Clipboard.SetTextAsync(deviceCode);
                            }
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
        foreach (var card in body.Children.OfType<Border>())
            if (card.Child is Expander { Content: StackPanel external })
            {
                var allowed = MicrosoftAuth.CanUseExternal(session.Store.Config.Accounts);
                foreach (var signIn in external.Children.OfType<Button>())
                    signIn.IsEnabled = allowed;
                if (!allowed)
                    external.Children.Add(T(MicrosoftAuth.ExternalLockedMessage, 12));
            }
        foreach (var identity in session.Store.Config.Accounts.ToArray())
        {
            var use = Btn(
                "Use account",
                async () =>
                {
                    MicrosoftAuth.EnsureAccountAllowed(session.Store.Config.Accounts, identity);
                    session.Account = identity;
                    await session.Save();
                    await Navigate("accounts");
                }
            );
            use.Tag = "use-account:" + identity.Key;
            use.IsEnabled = MicrosoftAuth.CanUseAccount(session.Store.Config.Accounts, identity);
            body.Children.Add(
                Card(
                    Stack(
                        Row(AccountAvatar(identity, 40), T(identity.ToString(), 16, true)),
                        Row(
                            use,
                            ConfirmationButton(
                                "Remove account",
                                Ui("Remove account?") + " " + identity.Name,
                                async () =>
                                {
                                    await session.RemoveAccount(identity);
                                    await Navigate("accounts");
                                },
                                "Remove"
                            )
                        )
                    )
                )
            );
        }
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
