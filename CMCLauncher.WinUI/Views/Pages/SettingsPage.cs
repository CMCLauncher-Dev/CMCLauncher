using CMCLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed class SettingsPage : CMCPage
{
    public SettingsPage(MainWindow window)
        : base(window) { }

    public override void Build()
    {
        Header(Ui("Launcher settings"), "");
        var settings = Store.Config.Settings;
        PageBody.Children.Add(Window.TerracottaSettingsCard());

        var launcherLanguage = new BelowComboBox
        {
            Header = Ui("Launcher language"),
            ItemsSource = Window.InterfaceLanguages,
            SelectedItem =
                Window.InterfaceLanguages.FirstOrDefault(language =>
                    language.Code == settings.LauncherLanguage
                ) ?? Window.InterfaceLanguages.FirstOrDefault(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        launcherLanguage.SelectionChanged += async (_, _) =>
        {
            if (
                launcherLanguage.SelectedItem is not InterfaceLanguage language
                || language.Code == settings.LauncherLanguage
            )
                return;
            await SwitchLanguage(language.Code);
        };

        var language = new BelowComboBox
        {
            Header = Ui("Game language"),
            ItemsSource = new[]
            {
                Ui("context.settingspage.system_default"),
                Ui("Simplified Chinese"),
                Ui("Traditional Chinese"),
                "English",
                Ui("Japanese"),
                Ui("context.settingspage.keep_current"),
            },
            SelectedIndex = settings.GameLanguage switch
            {
                "zh_cn" => 1,
                "zh_tw" => 2,
                "en_us" => 3,
                "ja_jp" => 4,
                "preserve" => 5,
                _ => 0,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var memory = new NumberBox
        {
            Header = Ui("Maximum memory / MB"),
            Value = settings.MemoryMb,
            Minimum = 512,
            Maximum = 65536,
            SmallChange = 512,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var downloadThreads = new NumberBox
        {
            Header = Ui("Download threads"),
            Value = LauncherCore.DownloadConcurrency(settings),
            Minimum = 1,
            Maximum = 256,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };

        var automaticMemory = new ToggleSwitch
        {
            Header = Ui("Game memory"),
            OnContent = Ui("Automatic [context 467]"),
            OffContent = Ui("Manual"),
            IsOn = settings.AutoMemory,
        };
        var memoryHint = Text("", 12);
        void RefreshMemory()
        {
            memory.IsEnabled = !automaticMemory.IsOn;
            memoryHint.Text = automaticMemory.IsOn
                ? (
                    UiFormat(
                        "Recommended: {0:N0} MB. Recalculated at each launch based on physical and available memory.",
                        GameMemory.Resolve(new Settings())
                    )
                )
                : Ui("Set the maximum Java heap size manually.");
        }
        automaticMemory.Toggled += (_, _) => RefreshMemory();
        RefreshMemory();

        var customJvmArgs = new TextBox
        {
            Header = Ui("Custom JVM arguments"),
            PlaceholderText = "-Dexample=1 -XX:+UseG1GC",
            Text = settings.CustomJvmArgs,
            AcceptsReturn = false,
        };
        var jvmHint = Text("", 12);

        var proxy = new TextBox
        {
            Header = Ui("Proxy address"),
            PlaceholderText = "http://127.0.0.1:7890",
            Text = settings.Proxy,
        };
        var proxyMode = new BelowComboBox
        {
            Tag = "proxy-mode",
            Header = Ui("Proxy mode"),
            ItemsSource = new[] { Ui("System proxy"), Ui("No proxy"), Ui("Manual proxy") },
            SelectedIndex = NetworkProxy.Mode(settings) switch
            {
                "direct" => 1,
                "custom" => 2,
                _ => 0,
            },
        };
        proxy.IsEnabled = proxyMode.SelectedIndex == 2;
        var proxyUser = new TextBox
        {
            Header = Ui("Proxy username"),
            Text = settings.ProxyUsername,
        };
        var proxyPassword = new PasswordBox { Header = Ui("Proxy password") };
        try
        {
            proxyPassword.Password = NetworkProxy.ReadPassword(settings);
        }
        catch
        {
            AddLog("Proxy credentials could not be read. Re-enter the proxy password.");
        }
        var bypassHosts = new TextBox
        {
            Header = Ui("Bypass proxy for these hosts"),
            Text = settings.ProxyBypassHosts,
            PlaceholderText = "localhost; *.example.com; 192.168.*",
            AcceptsReturn = true,
            MinHeight = 70,
            MaxHeight = 160,
        };
        var bypassLocal = new CheckBox
        {
            Content = Ui("Bypass proxy for local addresses"),
            IsChecked = settings.ProxyBypassLocal,
        };
        var directFallback = new CheckBox
        {
            Content = Ui("Allow direct fallback for game downloads when the proxy is unreachable"),
            IsChecked = settings.ProxyDirectFallback,
        };
        var applyProxy = new Button
        {
            Content = Ui("Apply proxy settings"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var testProxy = new Button
        {
            Content = Ui("Apply and test connection"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var bmcl = new BelowComboBox
        {
            Header = Ui("Game download source"),
            Tag = "game-download-source",
            ItemsSource = new[]
            {
                Ui("Prefer mirror sources"),
                Ui("Prefer official sources; switch to mirrors if slow"),
                Ui("Prefer official sources"),
            },
            SelectedIndex = settings.EffectiveGameDownloadSource switch
            {
                "mirror" => 0,
                "auto" => 1,
                _ => 2,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        bmcl.SelectionChanged += async (_, _) =>
        {
            if (bmcl.SelectedIndex < 0)
                return;
            settings.GameDownloadSource = bmcl.SelectedIndex switch
            {
                0 => "mirror",
                1 => "auto",
                _ => "official",
            };
            settings.UseBmclApi = bmcl.SelectedIndex != 2;
            await PersistSettings();
        };
        var downloadMirror = new TextBox
        {
            Header = Ui("Update acceleration (blank auto-selects fastest)"),
            PlaceholderText = "https://ghproxy.com/",
            Text = settings.DownloadMirror,
            AcceptsReturn = false,
        };
        var ignoreSslErrors = new ToggleSwitch
        {
            Header = Ui("Ignore SSL certificate errors"),
            IsOn = settings.IgnoreSslErrors,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        var sslWarning = Text(
            Ui("Skipping certificate validation reduces connection security."),
            12
        );

        var windowMode = new BelowComboBox
        {
            Header = Ui("Default window mode"),
            ItemsSource = new[] { Ui("Windowed"), Ui("Maximized") },
            SelectedIndex = settings.StartMaximized ? 1 : 0,
        };
        windowMode.SelectionChanged += async (_, _) =>
        {
            if (windowMode.SelectedIndex < 0)
                return;
            settings.StartMaximized = windowMode.SelectedIndex == 1;
            await PersistSettings();
        };

        var projectIntro = new ToggleSwitch
        {
            Header = Ui("Expand project descriptions by default [context 568]"),
            IsOn = settings.ExpandProjectIntroduction,
            OnContent = Ui("Expand"),
            OffContent = Ui("Collapse"),
        };

        var localizedWindow = new BelowComboBox
        {
            Header = Ui("Default window mode"),
            ItemsSource = new[] { Ui("Windowed"), Ui("Maximized") },
            SelectedIndex = windowMode.SelectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        localizedWindow.SelectionChanged += async (_, _) =>
        {
            if (localizedWindow.SelectedIndex >= 0)
            {
                windowMode.SelectedIndex = localizedWindow.SelectedIndex;
                settings.StartMaximized = localizedWindow.SelectedIndex == 1;
                await PersistSettings();
            }
        };

        var launcherVisibility = new BelowComboBox
        {
            Header = Ui("Launcher visibility"),
            ItemsSource = new[]
            {
                Ui("Launcher stays visible"),
                Ui("Close launcher after launch"),
                Ui("Hide launcher, close when game exits"),
                Ui("Hide launcher, reopen when game exits"),
                Ui("Minimize launcher after launch"),
            },
            SelectedIndex = settings.LauncherVisibility switch
            {
                "close" => 1,
                "hide_close" => 2,
                "hide_reopen" => 3,
                "minimize" => 4,
                _ => 0,
            },
        };
        launcherVisibility.SelectionChanged += async (_, _) =>
        {
            if (launcherVisibility.SelectedIndex < 0)
                return;
            settings.LauncherVisibility = launcherVisibility.SelectedIndex switch
            {
                1 => "close",
                2 => "hide_close",
                3 => "hide_reopen",
                4 => "minimize",
                _ => "keep",
            };
            await PersistSettings();
        };

        var highPerformanceGpu = new ToggleSwitch
        {
            Header = Ui("Prefer high-performance GPU for games"),
            IsOn = settings.PreferHighPerformanceGpu,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        highPerformanceGpu.Toggled += async (_, _) =>
        {
            try
            {
                settings.PreferHighPerformanceGpu = highPerformanceGpu.IsOn;
                await Store.Save();
                if (!highPerformanceGpu.IsOn)
                    await GameGpuPreference.Restore(Store);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var gamePriority = new BelowComboBox
        {
            Header = Ui("Game process priority"),
            ItemsSource = new[]
            {
                Ui("Normal priority"),
                Ui("Above normal priority"),
                Ui("High priority"),
                Ui("Realtime priority"),
                Ui("Below normal priority"),
                Ui("Idle priority"),
            },
            SelectedIndex = settings.PlayerPriority switch
            {
                "above" => 1,
                "high" => 2,
                "realtime" => 3,
                "below" => 4,
                "idle" => 5,
                _ => 0,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        gamePriority.SelectionChanged += async (_, _) =>
        {
            if (gamePriority.SelectedIndex < 0)
                return;
            settings.PlayerPriority = gamePriority.SelectedIndex switch
            {
                1 => "above",
                2 => "high",
                3 => "realtime",
                4 => "below",
                5 => "idle",
                _ => "normal",
            };
            await PersistSettings();
        };

        var gameWindowTitle = new TextBox
        {
            Header = Ui("Custom game window title"),
            PlaceholderText = Ui("Blank keeps the game default"),
            Text = settings.GameWindowTitle,
            AcceptsReturn = false,
        };
        gameWindowTitle.TextChanged += async (_, _) =>
        {
            settings.GameWindowTitle = gameWindowTitle.Text.TrimStart();
            await PersistSettings();
        };

        var localizedGameLanguage = new BelowComboBox
        {
            Header = Ui("Game language"),
            ItemsSource = new[]
            {
                Ui("context.settingspage.system_default"),
                Ui("Simplified Chinese"),
                Ui("Traditional Chinese"),
                "English",
                Ui("Japanese"),
                Ui("context.settingspage.keep_current"),
            },
            SelectedIndex = language.SelectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        localizedGameLanguage.SelectionChanged += async (_, _) =>
        {
            if (localizedGameLanguage.SelectedIndex >= 0)
            {
                language.SelectedIndex = localizedGameLanguage.SelectedIndex;
                settings.GameLanguage = language.SelectedIndex switch
                {
                    1 => "zh_cn",
                    2 => "zh_tw",
                    3 => "en_us",
                    4 => "ja_jp",
                    5 => "preserve",
                    _ => "auto",
                };
                await PersistSettings();
            }
        };

        var proxyHint = Text("", 12);
        proxyHint.Tag = "proxy-validation-hint";
        proxyHint.Visibility = Visibility.Collapsed;
        var proxyField = new StackPanel { Spacing = 4, Tag = "proxy-input-group" };
        proxyField.Children.Add(proxy);
        proxyField.Children.Add(proxyHint);

        Settings ProxySnapshot() =>
            new Settings
            {
                Proxy = settings.Proxy,
                ProxyMode = settings.ProxyMode,
                ProxyUsername = settings.ProxyUsername,
                ProxyPasswordProtected = settings.ProxyPasswordProtected,
                ProxyBypassHosts = settings.ProxyBypassHosts,
                ProxyBypassLocal = settings.ProxyBypassLocal,
                ProxyDirectFallback = settings.ProxyDirectFallback,
            };
        void CopyProxy(Settings value)
        {
            settings.Proxy = value.Proxy;
            settings.ProxyMode = value.ProxyMode;
            settings.ProxyUsername = value.ProxyUsername;
            settings.ProxyPasswordProtected = value.ProxyPasswordProtected;
            settings.ProxyBypassHosts = value.ProxyBypassHosts;
            settings.ProxyBypassLocal = value.ProxyBypassLocal;
            settings.ProxyDirectFallback = value.ProxyDirectFallback;
        }
        async Task<bool> ApplyProxy(bool advanced)
        {
            var previous = ProxySnapshot();
            var candidate = ProxySnapshot();
            try
            {
                if (Window.activeWork != null)
                    throw new InvalidOperationException(
                        Ui("Finish active tasks before changing proxy settings.")
                    );
                candidate.ProxyMode = proxyMode.SelectedIndex switch
                {
                    1 => "direct",
                    2 => "custom",
                    _ => "system",
                };
                candidate.Proxy = proxy.Text.Trim();
                if (advanced)
                {
                    candidate.ProxyUsername = proxyUser.Text.Trim();
                    candidate.ProxyPasswordProtected =
                        proxyPassword.Password.Length == 0
                            ? null
                            : NetworkProxy.ProtectPassword(proxyPassword.Password);
                    candidate.ProxyBypassHosts = bypassHosts.Text.Trim();
                    candidate.ProxyBypassLocal = bypassLocal.IsChecked == true;
                    candidate.ProxyDirectFallback = directFallback.IsChecked == true;
                }
                _ = NetworkProxy.Create(candidate);
                CopyProxy(candidate);
                Core.ConfigureNetwork();
                try
                {
                    await Store.Save();
                }
                catch
                {
                    CopyProxy(previous);
                    Core.ConfigureNetwork();
                    throw;
                }
                proxyHint.Text = "";
                proxyHint.Visibility = Visibility.Collapsed;
                return true;
            }
            catch (Exception ex)
            {
                CopyProxy(previous);
                proxyHint.Text = Ui(ex.Message);
                proxyHint.Visibility = Visibility.Visible;
                return false;
            }
        }
        proxyMode.SelectionChanged += async (_, _) =>
        {
            proxy.IsEnabled = proxyMode.SelectedIndex == 2;
            await ApplyProxy(false);
        };
        applyProxy.Click += async (_, _) =>
        {
            if (await ApplyProxy(true))
                Window.ShowToast(Ui("Proxy settings saved"));
        };
        testProxy.Click += async (_, _) =>
        {
            if (!await ApplyProxy(true))
                return;
            testProxy.IsEnabled = false;
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
                Window.ShowToast(Ui("Connection test succeeded"));
            }
            catch (Exception ex)
            {
                proxyHint.Text =
                    ex is OperationCanceledException
                        ? Ui("Connection test timed out.")
                        : Ui("Connection test failed.") + " " + ex.Message;
                proxyHint.Visibility = Visibility.Visible;
            }
            finally
            {
                testProxy.IsEnabled = true;
            }
        };
        void Section(string title, params UIElement[] controls)
        {
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(Text(Ui(title), 18, true));
            foreach (var control in controls)
                content.Children.Add(control);
            PageBody.Children.Add(Card(content));
        }

        Section("Language and interface", launcherLanguage, localizedWindow, projectIntro);
        Section(
            "Game launch",
            localizedGameLanguage,
            launcherVisibility,
            gameWindowTitle,
            highPerformanceGpu,
            Text(
                "Sets Windows graphics preference for the Java used at the next launch. Actual GPU selection depends on Windows and the driver.",
                12
            ),
            gamePriority
        );
        Section("Memory and Java", automaticMemory, memory, memoryHint, customJvmArgs, jvmHint);
        var advancedNetwork = new StackPanel { Spacing = 12 };
        advancedNetwork.Children.Add(proxyUser);
        advancedNetwork.Children.Add(proxyPassword);
        advancedNetwork.Children.Add(bypassHosts);
        advancedNetwork.Children.Add(bypassLocal);
        advancedNetwork.Children.Add(directFallback);
        advancedNetwork.Children.Add(applyProxy);
        advancedNetwork.Children.Add(testProxy);
        advancedNetwork.Children.Add(
            Text(
                "Authentication and bypass rules apply to manual proxy mode. Connection tests do not use direct fallback.",
                12
            )
        );
        advancedNetwork.Children.Add(ignoreSslErrors);
        advancedNetwork.Children.Add(sslWarning);
        Section(
            "Downloads and network",
            downloadThreads,
            bmcl,
            proxyMode,
            proxyField,
            downloadMirror,
            new Expander
            {
                Header = Ui("Advanced network options"),
                Tag = "advanced-network-settings",
                Content = advancedNetwork,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            }
        );

        async Task PersistSettings()
        {
            try
            {
                await Store.Save();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        }

        language.SelectionChanged += async (_, _) =>
        {
            if (language.SelectedIndex < 0)
                return;
            settings.GameLanguage = language.SelectedIndex switch
            {
                1 => "zh_cn",
                2 => "zh_tw",
                3 => "en_us",
                4 => "ja_jp",
                5 => "preserve",
                _ => "auto",
            };
            await PersistSettings();
        };
        projectIntro.Toggled += async (_, _) =>
        {
            settings.ExpandProjectIntroduction = projectIntro.IsOn;
            await PersistSettings();
        };
        automaticMemory.Toggled += async (_, _) =>
        {
            settings.AutoMemory = automaticMemory.IsOn;
            await PersistSettings();
        };
        memory.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(memory.Value) || memory.Value < 512 || memory.Value > 65536)
                return;
            settings.MemoryMb = (int)memory.Value;
            await PersistSettings();
        };
        downloadThreads.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(downloadThreads.Value))
                return;
            var value = Math.Clamp((int)Math.Round(downloadThreads.Value), 1, 256);
            if (downloadThreads.Value != value)
                downloadThreads.Value = value;
            settings.DownloadThreads = value;
            await PersistSettings();
        };

        void RefreshJvmHint()
        {
            var tokens = LauncherCore.SplitJvmArgs(customJvmArgs.Text);
            jvmHint.Text =
                tokens.Count > 0
                    ? UiFormat(
                        "Custom JVM arguments ({0}): {1}",
                        tokens.Count,
                        string.Join(" ", tokens)
                    )
                    : "";
        }
        customJvmArgs.TextChanged += async (_, _) =>
        {
            settings.CustomJvmArgs = customJvmArgs.Text.Trim();
            await PersistSettings();
            RefreshJvmHint();
        };

        proxy.TextChanged += async (_, _) => await ApplyProxy(false);
        downloadMirror.TextChanged += async (_, _) =>
        {
            var value = downloadMirror.Text.Trim().TrimEnd('/');
            if (
                value.Length > 0
                && (
                    !Uri.TryCreate(value, UriKind.Absolute, out var mirrorUri)
                    || mirrorUri.Scheme is not ("http" or "https")
                    || string.IsNullOrWhiteSpace(mirrorUri.Host)
                )
            )
                return;
            settings.DownloadMirror = value;
            await PersistSettings();
        };
        ignoreSslErrors.Toggled += async (_, _) =>
        {
            settings.IgnoreSslErrors = ignoreSslErrors.IsOn;
            Core.ConfigureNetwork();
            await PersistSettings();
        };

        PageBody.Children.Add(
            Text(UiFormat("Current version: {0}", LauncherUpdates.CurrentVersion), 12)
        );
    }
}
