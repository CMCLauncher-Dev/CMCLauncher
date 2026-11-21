using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using CMCLauncher.Avalonia.ViewModels;

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
        LocalizeToggle(autoMemory);
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
        LocalizeToggle(ssl);
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
        LocalizeToggle(gpu);
        var maximize = Choice(
            new[] { "Windowed", "Maximized" },
            settings.StartMaximized ? "Maximized" : "Windowed"
        );
        var visibility = Choice(
            new[] { "keep", "close", "hide_close", "hide_reopen", "minimize" },
            settings.LauncherVisibility
        );
        var mirror = Input(settings.DownloadMirror);
        var introduction = new ToggleSwitch
        {
            Content = Ui("Expand project introduction"),
            IsChecked = settings.ExpandProjectIntroduction,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        LocalizeToggle(introduction);
        var modTranslate = new ToggleSwitch
        {
            Content = Ui("Translate installed mods"),
            IsChecked = settings.ModTranslationEnabled,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        LocalizeToggle(modTranslate);
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
                var mirrorValue = (mirror.Text ?? "").Trim().TrimEnd('/');
                if (
                    mirrorValue.Length == 0
                    || Uri.TryCreate(mirrorValue, UriKind.Absolute, out var mirrorUri)
                        && mirrorUri.Scheme is "http" or "https"
                        && !string.IsNullOrWhiteSpace(mirrorUri.Host)
                )
                    settings.DownloadMirror = mirrorValue;
                settings.ExpandProjectIntroduction = introduction.IsChecked == true;
                settings.ModTranslationEnabled = modTranslate.IsChecked == true;
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
                settings.LauncherLanguage = selected.Code;
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
        var memoryControls = new List<Control> { autoMemory, memoryPanel };
        var platform = LauncherPlatform.Current;
        if (platform.SupportsMemoryOptimization)
        {
            var optimizeMemory = new ToggleSwitch
            {
                Content = Ui("Optimize memory before launching"),
                IsChecked = settings.OptimizeMemoryBeforeLaunch,
            };
            LocalizeToggle(optimizeMemory);
            optimizeMemory.IsCheckedChanged += async (_, _) =>
            {
                settings.OptimizeMemoryBeforeLaunch = optimizeMemory.IsChecked == true;
                await PersistSettings();
            };
            memoryControls.Add(optimizeMemory);
            memoryControls.Add(T("Memory optimization may briefly slow down other open apps.", 12));
        }
        memoryControls.Add(Field("Custom JVM arguments", jvm));
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
            Section("Memory and Java", memoryControls.ToArray()),
            Section(
                "Mod translation",
                modTranslate,
                T(
                    Ui(
                        "Mod language files and list texts are sent to a free translation service only while Mod translation is enabled. Original mod files are never modified."
                    ),
                    12
                )
            ),
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
            LauncherUpdateSettings(),
            ThirdPartyApplicationsCard(),
            T(Ui("Current version:") + " " + LauncherUpdates.CurrentVersion, 12)
        );
    }

    Control LauncherUpdateSettings()
    {
        var settings = session.Store.Config.Settings;
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
        async Task Save()
        {
            settings.CheckLauncherUpdates = checkUpdates.IsChecked == true;
            settings.ReceiveBetaUpdates = beta.IsChecked == true;
            try { await session.Save(); }
            catch (Exception ex) { await Error(ex); }
        }
        checkUpdates.IsCheckedChanged += async (_, _) => await Save();
        beta.IsCheckedChanged += async (_, _) => await Save();
        return Card(Stack(
            T("Launcher updates", 18, true),
            checkUpdates,
            beta,
            Btn("Check for updates", () => CheckDesktopUpdates(true))
        ));
    }

    Control ThirdPartyApplicationsCard()
    {
        return Stack(
            T("Manage third-party applications", 18, true),
            FrpcClientSettings(),
            TerracottaSettingsCard()
        );
    }

    Control AppearancePage()
    {
        var settings = session.Store.Config.Settings;
        session.Themes.Reload();
        var theme = Choice(new[] { "light", "dark", "system" }, settings.Theme);
        theme.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<string>(
            (value, _) => T(value == "system" ? "System" : value == "light" ? "Light" : "Dark")
        );
        var presets = new[]
        {
            new ThemePreset("native", "WinUI3 native", true),
        }
            .Concat(session.Themes.Presets)
            .Append(new ThemePreset("custom", "Custom", false))
            .ToArray();
        var preset = Choice(
            presets, presets.FirstOrDefault(p => p.Id == settings.ColorPreset)
                ?? presets.FirstOrDefault(p => p.Id == "craft")
        );
        var showScrollBars = new ToggleSwitch
        {
            Content = Ui("Show scrollbars"),
            Tag = "show-scrollbars",
            IsChecked = settings.ShowScrollBars,
        };
        LocalizeToggle(showScrollBars);
        showScrollBars.IsCheckedChanged += async (_, _) =>
        {
            settings.ShowScrollBars = showScrollBars.IsChecked == true;
            await session.Save();
            ApplyTheme();
        };
        var scale = Number((decimal)(settings.UiScale * 100), 70, 200, 5);
        var buttons = Number((decimal)(settings.UiButtonScale * 100), 70, 200, 5);
        var menus = Number((decimal)(settings.UiMenuScale * 100), 70, 200, 5);
        var font = Input(settings.UiFontFamily);
        var fontSize = Number((decimal)settings.UiFontSize, 10, 24);
        var saved = Choice(settings.CustomAppearancePresets);
        async Task Apply()
        {
            settings.UiScale = (double)(scale.Value ?? 100) / 100;
            settings.UiButtonScale = (double)(buttons.Value ?? 100) / 100;
            settings.UiMenuScale = (double)(menus.Value ?? 100) / 100;
            settings.UiFontFamily = font.Text ?? "Inter";
            settings.UiFontSize = (double)(fontSize.Value ?? 14);
            await session.Save();
            ApplyTheme();
            await Navigate("appearance");
        }
        var colorMode = Choice(new[] { "light", "dark" }, Light ? "light" : "dark");
        var swatches = Stack();
        var colors = Stack(Field("Edit colors", colorMode), swatches);
        colors.IsVisible = settings.ColorPreset == "custom";
        void DrawColors()
        {
            swatches.Children.Clear();
            var dictionary = colorMode.SelectedIndex == 0
                ? settings.CustomLightColors
                : settings.CustomDarkColors;
            var light = colorMode.SelectedIndex == 0;
            foreach (var (key, label, lightDefault, darkDefault) in new[]
            {
                ("background", "Background", "#F6F7F9", "#17191F"),
                ("sidebar", "Sidebar", "#EEF0F4", "#1C1D25"),
                ("surface", "Surface", "#FFFFFF", "#242730"),
                ("text", "Text", "#202124", "#F0F0F0"),
                ("muted", "Secondary text", "#555866", "#B7B9C2"),
                ("border", "Border", "#E6E8F0", "#3A3B4B"),
                ("accent", "Accent", "#416D37", "#536DBD"),
            })
            {
                var fallback = light ? lightDefault : darkDefault;
                var swatch = new global::Avalonia.Controls.Border
                {
                    Width = 30,
                    Height = 16,
                    CornerRadius = new global::Avalonia.CornerRadius(2),
                };
                global::Avalonia.Media.Color SelectedColor() =>
                    global::Avalonia.Media.Color.TryParse(dictionary.GetValueOrDefault(key), out var color)
                        ? color
                        : global::Avalonia.Media.Color.Parse(fallback);
                swatch.Background = new global::Avalonia.Media.SolidColorBrush(SelectedColor());
                var button = Btn(label, async () =>
                {
                    var picker = new ColorView
                    {
                        Color = SelectedColor(),
                        IsAlphaEnabled = false,
                    };
                    if (!await Dialog(label, picker, true))
                        return;
                    var color = picker.Color;
                    dictionary[key] = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    swatch.Background = new global::Avalonia.Media.SolidColorBrush(color);
                    await SavePalette();
                });
                button.Content = swatch;
                button.MinWidth = 44;
                button.MinHeight = 28;
                button.Padding = new global::Avalonia.Thickness(4);
                ToolTip.SetTip(button, Ui(label));
                global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(label));
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var caption = T(label);
                caption.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(caption);
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                swatches.Children.Add(row);
            }
        }
        async Task SavePalette()
        {
            ApplyTheme();
            try { await session.Save(); }
            catch (Exception ex) { await Error(ex); }
        }
        DrawColors();
        colorMode.SelectionChanged += (_, _) => DrawColors();
        theme.SelectionChanged += async (_, _) =>
        {
            if (theme.SelectedItem is string value)
            {
                settings.Theme = value;
                await SavePalette();
            }
        };
        preset.SelectionChanged += async (_, _) =>
        {
            if (preset.SelectedItem is ThemePreset value)
            {
                settings.ColorPreset = value.Id;
                colors.IsVisible = value.Id == "custom";
                await SavePalette();
            }
        };
        return Stack(
            Card(Stack(Field("Appearance", theme), Field("Preset", preset), colors)),
            Card(
                Stack(
                    showScrollBars,
                    Field("Control size / %", scale),
                    Field("Button size / %", buttons),
                    Field("Menu size / %", menus),
                    Field("Font family", font),
                    Field("Font size", fontSize),
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
                                settings.ShowScrollBars = value.ShowScrollBars;
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
                                        ShowScrollBars = settings.ShowScrollBars,
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
        var settings = session.Store.Config.Settings;
        void ReportJava(string message)
        {
            session.ReportStatus(message);
            session.Log("INFO", message);
        }
        Button JavaAction(string label, string glyph, Func<Task> action)
        {
            var button = Btn(label, action);
            var icon = NavigationIcon(glyph);
            icon.Width = icon.Height = 16;
            button.Content = icon;
            button.MinWidth = button.MinHeight = 28;
            button.Padding = new global::Avalonia.Thickness(6);
            button.Background = global::Avalonia.Media.Brushes.Transparent;
            button.BorderThickness = new global::Avalonia.Thickness(0);
            ToolTip.SetTip(button, Ui(label));
            global::Avalonia.Automation.AutomationProperties.SetName(button, Ui(label));
            return button;
        }

        void Draw()
        {
            list.Children.Clear();
            if (!string.IsNullOrWhiteSpace(settings.JavaPath) &&
                !settings.JavaInstallations.Any(java => java.Path == settings.JavaPath))
                list.Children.Add(T(session.Locales.Format(settings.LauncherLanguage,
                    "Manual Java setting: {0} (import it for full details)", settings.JavaPath), 12));
            if (settings.JavaInstallations.Count == 0)
                list.Children.Add(T("No Java added yet. Import or scan local installations.", 13));

            foreach (var java in settings.JavaInstallations.OrderByDescending(j => j.Major).ThenBy(j => j.Vendor))
            {
                var detail = Stack(T($"Java {java.Version} · {java.Vendor}", 14, true),
                    T(java.Architecture + " · " + java.Path, 12));
                detail.Spacing = 5;
                if (JavaManager.AffectedByJava4Shell(java))
                {
                    var warning = T("This Java version is affected by the Java4Shell vulnerability (CVE-2026-114938). Update to a fixed release or the launcher will disable its fast Base64 path automatically.", 12, true);
                    warning.Foreground = global::Avalonia.Media.Brush.Parse(Light ? "#8B171F" : "#FFB4BC");
                    detail.Children.Add(new Border
                    {
                        Padding = new global::Avalonia.Thickness(10, 6),
                        CornerRadius = new global::Avalonia.CornerRadius(4),
                        Background = global::Avalonia.Media.Brush.Parse(Light ? "#FFEBED" : "#401820"),
                        Child = warning,
                    });
                }
                if (!File.Exists(java.Path))
                    detail.Children.Add(T("File does not exist. Re-import or remove the entry.", 12));
                var actions = Row(
                    JavaAction("Update Java", "M19,10 A7,7 0 1 0 18,16 M19,5 V10 H14", async () =>
                    {
                        if (JavaManager.UpdateBrand(session.Store, java) == null)
                        {
                            ShowToast(Ui("Automatic updates are unavailable for this Java brand, version or architecture."));
                            return;
                        }
                        if (JavaManager.ManagedBrand(session.Store, java) == null &&
                            !await Confirm("Update Java", "CMCLauncher will install the update separately and switch its Java entry. The imported files will be kept."))
                            return;
                        session.EnsureFilesIdle();
                        bool updated = false;
                        if (await session.Run("Update Java", async ct =>
                            updated = await JavaManager.UpdateJava(session.Core, session.Store, java, ct, ReportJava)))
                            ShowToast(Ui(updated ? "Java updated" : "No newer Java release found"));
                        Draw();
                    }),
                    JavaAction("Move Java", "M3,18 V6 H9 L11,8 H21 V18 H3 Z M11,13 H18 M15,10 L18,13 L15,16", async () =>
                    {
                        session.EnsureFilesIdle();
                        if (await Folder() is not { } parent)
                            return;
                        var removedSource = false;
                        if (await session.Run("Move Java", async ct =>
                        {
                            session.EnsureFilesIdle();
                            removedSource = await JavaManager.MigrateJava(session.Store, java, parent, ct);
                        }))
                            ShowToast(Ui(removedSource ? "Java moved successfully." : "Switched to the new Java folder. Original files were kept."));
                        Draw();
                    }),
                    JavaAction("Remove from list", "M5,12 H19", async () =>
                    {
                        if (!await Confirm("Remove Java entry?", java.Path))
                            return;
                        JavaManager.Remove(settings, java);
                        await session.Save();
                        Draw();
                    }),
                    JavaAction("Delete Java folder", "M4,6 H20 M9,6 V3 H15 V6 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17", async () =>
                    {
                        session.EnsureFilesIdle();
                        var folder = JavaManager.JavaDirectoryForDeletion(session.Store, java);
                        if (!await Dialog("Delete Java folder",
                            T(Ui("Move this Java installation to the Recycle Bin? Other apps using it may stop working.") + Environment.NewLine + folder), true, danger: true))
                            return;
                        await session.Run("Delete Java folder", async ct =>
                        {
                            session.EnsureFilesIdle();
                            if (!string.Equals(folder, JavaManager.JavaDirectoryForDeletion(session.Store, java),
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                                throw new IOException("The Java installation folder changed.");
                            ct.ThrowIfCancellationRequested();
                            await Trash(folder);
                            if (Directory.Exists(folder))
                                throw new IOException("The Java installation folder was not deleted.");
                            JavaManager.Remove(settings, java);
                            await session.Save();
                        });
                        Draw();
                    })
                );
                actions.VerticalAlignment = VerticalAlignment.Center;
                foreach (var action in actions.Children)
                    action.Margin = new global::Avalonia.Thickness(2, 0);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
                row.Children.Add(detail);
                Grid.SetColumn(actions, 1);
                row.Children.Add(actions);
                var card = Card(row);
                card.Padding = new global::Avalonia.Thickness(11, 7);
                list.Children.Add(card);
            }
        }

        async Task DownloadJava()
        {
            var versions = Choice(new[] { 8, 11, 17, 21, 25 }, 21);
            var brands = Choice(JavaManager.DownloadBrands(21));
            versions.SelectionChanged += (_, _) =>
            {
                if (versions.SelectedItem is not int version)
                    return;
                var available = JavaManager.DownloadBrands(version).ToArray();
                var previous = (brands.SelectedItem as JavaManager.JavaBrand)?.Id;
                brands.ItemsSource = available;
                brands.SelectedItem = available.FirstOrDefault(brand => brand.Id == previous) ?? available[0];
            };
            string? parent = null;
            var location = T(Path.Combine(session.Store.Root, "runtimes"), 12);
            var choose = Btn("Choose folder", async () =>
            {
                if (await Folder() is { } folder)
                {
                    parent = folder;
                    location.Text = folder;
                }
            });
            if (!await Dialog("Download Java", Stack(Field("Java version", versions), Field("Java brand", brands), choose, location), true, confirmLabel: "Download Java")
                || versions.SelectedItem is not int major || brands.SelectedItem is not JavaManager.JavaBrand brand)
                return;
            await session.Run(session.Locales.Format(settings.LauncherLanguage, "Download Java {0}", major), async ct =>
            {
                var java = await JavaManager.Download(session.Core, session.Store, major, ct, ReportJava, brand.Id, parent);
                JavaManager.Register(settings, java);
                await session.Save();
            });
            Draw();
        }

        async Task Scan(string? folder = null)
        {
            await session.Run(
                "Scan Java",
                async ct =>
                {
                    var candidates = await Task.Run(
                        () => JavaManager.Candidates(session.Store, folder, ct).ToArray(),
                        ct
                    );
                    foreach (var path in candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (
                            session.Store.Config.Settings.IgnoredJavaPaths.Contains(
                                path,
                                StringComparer.Ordinal
                            )
                        )
                            continue;
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
        Draw();
        return Stack(
            Row(
                Btn("Import Java", async () =>
                {
                    var path = await OpenFile();
                    if (path == null)
                        return;
                    await session.Run("Import Java", async ct =>
                    {
                        JavaManager.Register(settings, await JavaManager.Inspect(path, ct));
                        await session.Save();
                    });
                    Draw();
                }),
                Btn("Scan Java", () => Scan()),
                Btn("Scan folder", async () =>
                {
                    if (await Folder() is { } folder)
                        await Scan(folder);
                }),
                Btn("Download Java", DownloadJava, true),
                Btn("Use Java from PATH", async () =>
                {
                    var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                        .Select(entry => Path.Combine(entry.Trim().Trim('"'), JavaManager.JavaExecutable))
                        .Where(File.Exists)
                        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                        .ToArray();
                    if (paths.Length == 0)
                        throw new IOException(Ui("Java not found in PATH."));
                    await session.Run("Import Java from PATH", async ct =>
                    {
                        foreach (var path in paths)
                        {
                            ct.ThrowIfCancellationRequested();
                            try { JavaManager.Register(settings, await JavaManager.Inspect(path, ct)); }
                            catch (Exception ex) when (!ct.IsCancellationRequested)
                            {
                                session.Log("DEBUG", ex.Message);
                            }
                        }
                        await session.Save();
                    });
                    Draw();
                })
            ),
            list
        );
    }

    Control AccountsPage()
    {
        var accounts = session.Store.Config.Accounts;
        var saved = Stack(T("已保存的账号", 18, true));
        var add = Stack(T("添加账号", 18, true));
        var visible = accounts.Where(a => a.Type != "microsoft" || MicrosoftAuth.IsVerifiedMinecraftPlayer(a)).ToArray();
        if (visible.Length == 0)
            saved.Children.Add(Card(T("还没有账号，请在下方登录 Microsoft 账号。", 13)));

        async Task SaveAccount(Account identity)
        {
            MicrosoftAuth.EnsureAccountAllowed(accounts, identity);
            accounts.RemoveAll(a => a.Key == identity.Key);
            accounts.Add(identity);
            session.Account = identity;
            await session.Save();
        }

        foreach (var identity in visible)
        {
            var allowed = MicrosoftAuth.CanUseAccount(accounts, identity);
            var current = session.Account?.Key == identity.Key && allowed;
            var use = Btn(current ? "当前使用" : "使用此账号", async () =>
            {
                MicrosoftAuth.EnsureAccountAllowed(accounts, identity);
                session.Account = identity;
                await session.Save();
                await Navigate("accounts");
            }, current);
            use.Tag = "use-account:" + identity.Key;
            use.IsEnabled = allowed;
            var actions = Row(use, ConfirmationButton(
                "Remove account", Ui("Remove account?") + " " + identity.Name,
                async () =>
                {
                    await session.RemoveAccount(identity);
                    await Navigate("accounts");
                }, "Remove"));
            var provider = identity.Type switch
            {
                "microsoft" => "Microsoft",
                "yggdrasil" => identity.AuthServer,
                _ => "离线账号",
            };
            var identityRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            identityRow.Children.Add(AccountAvatar(identity, 40));
            var details = Stack(T(identity.Name, 16, true), T(provider, 11));
            details.Spacing = 2;
            Grid.SetColumn(details, 1);
            identityRow.Children.Add(details);
            var cardBody = Stack(identityRow);
            if (!allowed)
                cardBody.Children.Add(T(identity.Type == "yggdrasil"
                    ? MicrosoftAuth.ExternalLockedMessage
                    : "context.accounts.this_account_must_be_verified_again_through_microsoft_sign_in", 12));
            saved.Children.Add(Card(AccountColumns(cardBody, actions, 620, rightAlign: true)));
        }

        var loginStatus = T("", 12);
        var codeText = Input();
        codeText.IsReadOnly = true;
        var verificationUrl = "";
        async Task CopyCode()
        {
            if (Clipboard == null || string.IsNullOrEmpty(codeText.Text))
                return;
            await Clipboard.SetTextAsync(codeText.Text);
            loginStatus.Text = Ui("Device code copied. Paste it in your browser to continue.");
        }
        void OpenSignIn()
        {
            if (MicrosoftAuth.IsVerificationUrl(verificationUrl))
                Open(verificationUrl);
        }
        var codeActions = Stack(
            T("请在浏览器中输入设备代码："),
            codeText,
            Row(Btn("复制设备代码", CopyCode), Btn("重新打开浏览器", () =>
            {
                OpenSignIn();
                return Task.CompletedTask;
            })),
            Btn("Open sign-in page", () =>
            {
                OpenSignIn();
                return Task.CompletedTask;
            })
        );
        codeActions.IsVisible = false;
        loginStatus.IsVisible = false;
        var microsoft = Btn("Microsoft 设备代码登录", async () =>
        {
            loginStatus.IsVisible = true;
            loginStatus.Text = Ui("Requesting a device code…");
            codeActions.IsVisible = false;
            verificationUrl = "";
            codeText.Text = "";
            try
            {
                var completed = await session.Run("Microsoft sign-in", async ct =>
                {
                    var identity = await new MicrosoftAuth(session.Core).Login(
                        MicrosoftAuth.ConfiguredClientId,
                        (value, url) => Dispatcher.UIThread.Post(async () =>
                        {
                            if (ct.IsCancellationRequested || route != "accounts")
                                return;
                            codeText.Text = value;
                            verificationUrl = url;
                            codeActions.IsVisible = true;
                            try { await CopyCode(); }
                            catch { loginStatus.Text = Ui("Could not copy automatically. Copy the device code above manually."); }
                            try { OpenSignIn(); }
                            catch { loginStatus.Text = Ui("The browser did not open automatically. Use the sign-in link below."); }
                        }),
                        ct,
                        stage => Dispatcher.UIThread.Post(() => loginStatus.Text = Ui(stage))
                    );
                    await SaveAccount(identity);
                });
                if (completed)
                    await Navigate("accounts");
                else
                    loginStatus.Text = Ui("Sign-in canceled");
            }
            catch
            {
                loginStatus.Text = Ui("Sign-in incomplete. See the error details; browser authorization must still be verified by Xbox and Minecraft services.");
                throw;
            }
        }, true);
        add.Children.Add(Card(Stack(T("Microsoft", 16, true),
            T("使用拥有 Minecraft 的 Microsoft 账号登录。", 12), microsoft, loginStatus, codeActions)));

        var offline = Stack(T("离线身份", 16, true));
        if (MicrosoftAuth.CanUseOffline(accounts))
        {
            var name = Input("", Ui("3–16 letters, numbers, or underscores"));
            name.MaxLength = 16;
            offline.Children.Add(Field("Offline player name", name));
            offline.Children.Add(Btn("Add offline account", async () =>
            {
                await SaveAccount(Account.Offline(name.Text?.Trim() ?? ""));
                await Navigate("accounts");
            }));
        }
        else
            offline.Children.Add(T(MicrosoftAuth.OfflineLockedMessage, 12));

        var server = Input("", "https://example.com/api/yggdrasil/");
        var preset = Btn("LittleSkin", () =>
        {
            server.Text = "https://littleskin.cn/api/yggdrasil";
            return Task.CompletedTask;
        });
        var login = Input("", Ui("Username or email"));
        var password = Input();
        password.PasswordChar = '●';
        var external = Stack(T("External sign-in (authlib-injector)", 16, true));
        var unlocked = MicrosoftAuth.CanUseExternal(accounts);
        if (!unlocked)
            external.Children.Add(T(MicrosoftAuth.ExternalLockedMessage, 12));
        var fields = Stack(
            Field("Authentication server", AccountColumns(server, preset, 0, rightAlign: true)),
            Field("External account", login),
            Field("Password", password),
            Btn("Sign in to external account", async () =>
            {
                MicrosoftAuth.EnsureExternalAllowed(accounts);
                try
                {
                    if (await session.Run("External sign-in", async ct =>
                    {
                        using var auth = new ExternalAuth(session.Store.Config.Settings);
                        var identity = await auth.Login(accounts, server.Text ?? "", login.Text ?? "", password.Text ?? "",
                            async profiles =>
                            {
                                var picker = Choice(profiles);
                                return await Dialog("Select a player profile", picker, true)
                                    ? picker.SelectedItem as ExternalProfile : null;
                            }, ct);
                        await SaveAccount(identity);
                    }))
                        await Navigate("accounts");
                }
                finally { password.Text = ""; }
            }, true)
        );
        fields.IsEnabled = unlocked;
        external.Children.Add(fields);
        add.Children.Add(AccountColumns(Card(external), Card(offline), 620));
        return Stack(saved, add);
    }

    static Grid AccountColumns(Control left, Control right, double narrowWidth, bool rightAlign = false)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(rightAlign ? "*,Auto" : "*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 12,
        };
        left.VerticalAlignment = VerticalAlignment.Top;
        right.VerticalAlignment = rightAlign ? VerticalAlignment.Center : VerticalAlignment.Top;
        grid.Children.Add(left);
        grid.Children.Add(right);
        void Arrange(bool narrow)
        {
            grid.RowSpacing = narrow ? 12 : 0;
            Grid.SetColumnSpan(left, narrow ? 2 : 1);
            Grid.SetColumnSpan(right, narrow ? 2 : 1);
            Grid.SetColumn(right, narrow ? 0 : 1);
            Grid.SetRow(right, narrow ? 1 : 0);
            right.HorizontalAlignment = rightAlign
                ? narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right
                : HorizontalAlignment.Stretch;
        }
        Arrange(false);
        grid.SizeChanged += (_, e) => Arrange(e.NewSize.Width < narrowWidth);
        return grid;
    }

    Control LogsPage()
    {
        var debug = new ToggleSwitch
        {
            IsChecked = session.Store.Config.Settings.DebugLogging,
            OnContent = "DEBUG",
            OffContent = "DEBUG",
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "debug-logging",
        };
        LocalizeToggle(debug);
        ToolTip.SetTip(debug, Ui("DEBUG mode"));
        global::Avalonia.Automation.AutomationProperties.SetName(debug, Ui("DEBUG mode"));
        debug.IsCheckedChanged += async (_, _) =>
        {
            session.Store.Config.Settings.DebugLogging = debug.IsChecked == true;
            await session.Save();
        };
        var view = Input("", multiline: true);
        view.IsReadOnly = true;
        view.MinHeight = 360;
        view.MaxHeight = 650;
        view.FontFamily = new global::Avalonia.Media.FontFamily(
            "Cascadia Mono, Consolas, monospace"
        );
        view.Tag = "runtime-log-view";
        void Refresh() => view.Text = string.Join(Environment.NewLine, session.Logs);
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Tag = "logs-toolbar",
        };
        toolbar.Children.Add(debug);
        var refresh = Btn(
            "Refresh",
            () =>
            {
                Refresh();
                return Task.CompletedTask;
            }
        );
        Grid.SetColumn(refresh, 1);
        toolbar.Children.Add(refresh);
        var folder = Btn(
            "Open log folder",
            () =>
            {
                var path = Path.Combine(session.Store.Root, "logs");
                Directory.CreateDirectory(path);
                Open(path);
                return Task.CompletedTask;
            }
        );
        Grid.SetColumn(folder, 2);
        toolbar.Children.Add(folder);
        var dirty = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (dirty)
            {
                dirty = false;
                Refresh();
            }
        };
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) =>
            dirty = true;
        var body = Stack(toolbar, view);
        body.AttachedToVisualTree += (_, _) =>
        {
            session.Logs.CollectionChanged += changed;
            Refresh();
            timer.Start();
        };
        body.DetachedFromVisualTree += (_, _) =>
        {
            session.Logs.CollectionChanged -= changed;
            timer.Stop();
        };
        return body;
    }

    Control TasksPage()
    {
        var updates = new List<Action>();
        var empty = T("No tasks in progress", 14);
        void RefreshCards()
        {
            empty.IsVisible = !session.Operations.Any(item => item.IsRunning);
            foreach (var update in updates.ToArray())
                update();
        }
        var list = new ItemsControl { ItemsSource = session.Operations };
        list.ItemTemplate =
            new global::Avalonia.Controls.Templates.FuncDataTemplate<OperationState>(
                (item, _) =>
                {
                    var title = T(item.Title, 17, true);
                    var stage = T("", 13);
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
                    var panel = Stack(title, stage, bar);
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
                    elapsed.VerticalAlignment = VerticalAlignment.Center;
                    var footer = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 12,
                    };
                    footer.Children.Add(elapsed);
                    var cancellationRequested = item.Stage == Ui("Cancelling…");
                    var cancel = Btn(
                        "Cancel task",
                        () =>
                        {
                            if (session.Active == item)
                            {
                                cancellationRequested = true;
                                session.Cancel();
                                item.Stage = Ui("Cancelling…");
                            }
                            return Task.CompletedTask;
                        }
                    );
                    cancel.HorizontalAlignment = HorizontalAlignment.Right;
                    cancel.VerticalAlignment = VerticalAlignment.Center;
                    buttonAvailability.Add(
                        cancel,
                        () => item.IsRunning && session.Active == item && !cancellationRequested
                    );
                    Grid.SetColumn(cancel, 1);
                    footer.Children.Add(cancel);
                    panel.Children.Add(footer);
                    var heading = T("Recent tasks", 15, true);
                    var wrapper = Stack(heading, Card(panel));
                    void Update()
                    {
                        title.Text = item.IsRunning ? item.Title : item.Title + " · " + item.State;
                        title.FontSize =
                            (item.IsRunning ? 17 : 14)
                            * session.Store.Config.Settings.UiFontSize
                            / 14;
                        panel.Spacing = item.IsRunning ? 10 : 4;
                        stage.IsVisible = item.IsRunning || item.Stage != item.State;
                        bar.IsVisible = item.IsRunning;
                        cancel.IsVisible = item.IsRunning && session.Active == item;
                        cancel.IsEnabled =
                            item.IsRunning && session.Active == item && !cancellationRequested;
                        heading.IsVisible =
                            !item.IsRunning
                            && ReferenceEquals(
                                session.Operations.FirstOrDefault(task => !task.IsRunning),
                                item
                            );
                    }
                    System.ComponentModel.PropertyChangedEventHandler changed = (_, args) =>
                    {
                        if (args.PropertyName is nameof(item.State) or nameof(item.IsRunning))
                            RefreshCards();
                        else if (args.PropertyName == nameof(item.Stage))
                            Update();
                    };
                    wrapper.AttachedToVisualTree += (_, _) =>
                    {
                        updates.Add(Update);
                        item.PropertyChanged += changed;
                        RefreshCards();
                    };
                    wrapper.DetachedFromVisualTree += (_, _) =>
                    {
                        updates.Remove(Update);
                        item.PropertyChanged -= changed;
                    };
                    Update();
                    return wrapper;
                }
            );
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            foreach (var item in session.Operations)
                item.Tick();
            RefreshCards();
        };
        System.Collections.Specialized.NotifyCollectionChangedEventHandler collectionChanged = (
            _,
            _
        ) => RefreshCards();
        list.AttachedToVisualTree += (_, _) =>
        {
            session.Operations.CollectionChanged += collectionChanged;
            RefreshCards();
            timer.Start();
        };
        list.DetachedFromVisualTree += (_, _) =>
        {
            session.Operations.CollectionChanged -= collectionChanged;
            timer.Stop();
        };
        return Stack(empty, list);
    }
}
