using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    const string DefaultFontFamily = "Segoe UI Variable Text";

    // 界面为中文时，把常见中文字体族名显示为中文名；键为字体族名，值为中文显示名。
    static readonly Dictionary<string, string> ChineseFontDisplayNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Microsoft YaHei"] = "微软雅黑",
        ["Microsoft YaHei UI"] = "微软雅黑 UI",
        ["Microsoft YaHei & Microsoft YaHei UI"] = "微软雅黑",
        ["Microsoft JhengHei"] = "微软正黑体",
        ["Microsoft JhengHei UI"] = "微软正黑体",
        ["SimSun"] = "宋体",
        ["NSimSun"] = "新宋体",
        ["SimHei"] = "黑体",
        ["KaiTi"] = "楷体",
        ["FangSong"] = "仿宋",
        ["DengXian"] = "等线",
        ["YouYuan"] = "幼圆",
    };

    // 当前为中文界面时返回中文显示名，否则保持字体族名不变。
    string LocalizedFontName(string family)
    {
        if (
            store.Config.Settings.LauncherLanguage.StartsWith(
                "zh",
                StringComparison.OrdinalIgnoreCase
            ) && ChineseFontDisplayNames.TryGetValue(family.Trim(), out var chinese)
        )
            return chinese;
        return family;
    }

    // 把下拉框里的显示名反查回字体族名（用于存入 UiFontFamily）；未知名称原样返回。
    string FontFamilyForDisplay(string display)
    {
        if (
            store.Config.Settings.LauncherLanguage.StartsWith(
                "zh",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            foreach (var pair in ChineseFontDisplayNames)
            {
                if (pair.Value == display)
                    return pair.Key;
            }
        }
        return display;
    }

    // 字体下拉框：每个选项都用对应的真实字体渲染文本，便于直观挑选。
    // 由 BelowComboBox.ItemFontFactory 在项容器上直接应用字体，无需模板或转换器。
    sealed class OriginalAppearance
    {
        public double FontSize,
            Height,
            MinHeight,
            MinWidth;
        public Thickness Padding;
    }

    // 按控件保存原始尺寸，每次都从原值缩放，避免反复应用后越变越大；控件回收时记录随之释放。
    readonly ConditionalWeakTable<DependencyObject, OriginalAppearance> originalAppearances = new();

    // TextBlock 的原始字号用强引用保留，避免弱引用缓存随控件移出可视树被回收后，
    // 再次应用时误把已缩放的当前值当作原始值，导致字号逐次累积缩小。
    readonly Dictionary<TextBlock, double> originalTextSizes = new();

    // 遍历页面持有的内容，包括抽屉 Pane；无需进入每个原生控件的内部模板。
    static IEnumerable<DependencyObject> AppearanceElements(DependencyObject node)
    {
        yield return node;
        IEnumerable<DependencyObject> children = node switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => new[] { child },
            SplitView split => new[] { split.Content, split.Pane }.OfType<DependencyObject>(),
            ScrollViewer { Content: DependencyObject content } => new[] { content },
            Frame { Content: DependencyObject content } => new[] { content },
            Page { Content: DependencyObject content } => new[] { content },
            ContentControl { Content: DependencyObject content } => new[] { content },
            _ => [],
        };
        foreach (var child in children)
        foreach (var descendant in AppearanceElements(child))
            yield return descendant;
    }

    bool startupGlassSuspended;
    bool GlassActive => store.Config.Settings.MicaEffect && !startupGlassSuspended;

    void InitializeVisibleBackdrop(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            return;
        Activated -= InitializeVisibleBackdrop;
        if (!store.Config.Settings.MicaEffect || Verifying)
            return;
        startupGlassSuspended = true;
        ApplyBackdrop(false);
        ApplyTheme();
        CompositionTarget.Rendering += CompleteVisibleBackdrop;
        Closed += (_, _) => CompositionTarget.Rendering -= CompleteVisibleBackdrop;
    }

    void CompleteVisibleBackdrop(object? sender, object args)
    {
        CompositionTarget.Rendering -= CompleteVisibleBackdrop;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Content == null)
                return;
            startupGlassSuspended = false;
            ApplyBackdrop(GlassActive);
            ApplyTheme();
        });
    }

    void ApplyBackdrop(bool glass)
    {
        if (Content == null)
            return;
        UpdateClientFrame(glass && !startupGlassSuspended);
        // CI 验证无交互会话，跳过透明样式处理。
        if (Verifying)
            return;
        // 该窗口是 WS_EX_LAYERED 分层透明窗口，系统背景材质（Mica/Acrylic）
        // 在分层合成路径下会退化成不透明白底，因此始终保持 TransparentBackdrop，
        // 真正的背景模糊交由 DWM 的 AccentPolicy 在分层窗口上合成。
        if (SystemBackdrop is not TransparentBackdrop)
            SystemBackdrop = new TransparentBackdrop();
        ApplyWindowBlur(glass && store.Config.Settings.GlassBackgroundBlur);
    }

    // 背景模糊与卡片不透明度相互独立：模糊由 DWM 对窗口背后内容做 BlurBehind，
    // 卡片表面的半透明（GlassOpacityPercent）始终叠加在模糊之上由 UI 单独控制。
    void ApplyWindowBlur(bool blur)
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var policy = new AccentPolicy
        {
            AccentState = blur ? AccentState.ACCENT_ENABLE_BLURBEHIND : AccentState.ACCENT_DISABLED,
            AccentFlags = 0,
            GradientColor = 0,
            AnimationId = 0,
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = pointer,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hWnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowCompositionAttribute(
        IntPtr hWnd,
        ref WindowCompositionAttributeData data
    );

    const int WCA_ACCENT_POLICY = 19;

    // 继承 SystemBackdrop，但不附加任何系统背景材质（不创建 Mica/Acrylic 控制器）。
    // 这一版本的 Windows App SDK 不再分派 CompositionBackdropController，
    // SystemBackdrop 基类的 OnTargetConnected 无返回值；回调保持空实现，
    // 合成器因此不会为窗口绘制不透明底色，配合各面板/卡片完全透明背景即成纯透明窗口。
    sealed class TransparentBackdrop : SystemBackdrop
    {
        protected override void OnTargetConnected(
            ICompositionSupportsSystemBackdrop connectedTarget,
            XamlRoot xamlRoot
        )
        {
        }

        protected override void OnTargetDisconnected(
            ICompositionSupportsSystemBackdrop connectedTarget
        )
        {
        }
    }

    void ApplyPersonalization()
    {
        if (Content == null)
            return;
        var settings = store.Config.Settings;
        ApplyBackdrop(settings.MicaEffect);
        ScrollBarAppearance.Show = settings.ShowScrollBars;
        var scale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, .8, 1.5) : 1;
        var buttonScale = double.IsFinite(settings.UiButtonScale)
            ? Math.Clamp(settings.UiButtonScale, .8, 1.5)
            : 1;
        var menuScale = double.IsFinite(settings.UiMenuScale)
            ? Math.Clamp(settings.UiMenuScale, .8, 1.5)
            : 1;
        var fontScale =
            (double.IsFinite(settings.UiFontSize) ? Math.Clamp(settings.UiFontSize, 12, 22) : 14)
            / 14;
        var family = new FontFamily(
            string.IsNullOrWhiteSpace(settings.UiFontFamily)
                ? DefaultFontFamily
                : settings.UiFontFamily
        );
        var elements = AppearanceElements(Content).ToArray();
        // Capture original sizes before applying inherited control fonts.
        foreach (var element in elements)
        {
            if (element is TextBlock t)
            {
                if (!originalTextSizes.TryGetValue(t, out _))
                    originalTextSizes[t] = t.FontSize;
            }
            else if (element is Control c)
            {
                // Lazy content may reach personalization before its theme template is applied.
                // Read themed defaults before storing local size and padding overrides.
                c.ApplyTemplate();
                originalAppearances.GetValue(
                    c,
                    _ =>
                        new()
                        {
                            FontSize = c.FontSize,
                            Height = c.Height,
                            MinHeight = c.MinHeight,
                            MinWidth = c.MinWidth,
                            Padding = c.Padding,
                        }
                );
            }
        }
        foreach (var element in elements)
        {
            if (
                element is TextBlock text
                && !text.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
            )
            {
                var original = originalTextSizes.TryGetValue(text, out var baseSize)
                    ? new OriginalAppearance { FontSize = baseSize }
                    : originalAppearances.GetValue(
                        text,
                        _ => new() { FontSize = text.FontSize }
                    );
                text.FontSize = original.FontSize * fontScale;
                text.FontFamily = family;
            }
            else if (
                element is Control control
                && control is not (Frame or Page or ScrollViewer)
                && !control.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
            )
            {
                var original = originalAppearances.GetValue(
                    control,
                    _ =>
                        new()
                        {
                            FontSize = control.FontSize,
                            Height = control.Height,
                            MinHeight = control.MinHeight,
                            MinWidth = control.MinWidth,
                            Padding = control.Padding,
                        }
                );
                control.FontSize = original.FontSize * fontScale;
                control.FontFamily = family;
                if (
                    control
                    is Button
                        or ComboBox
                        or TextBox
                        or PasswordBox
                        or NumberBox
                        or ToggleSwitch
                )
                {
                    var controlScale =
                        navigationButtons.Values.Contains(control) ? menuScale
                        : control is Button ? buttonScale
                        : scale;
                    control.Padding = new Thickness(
                        original.Padding.Left * controlScale,
                        original.Padding.Top * controlScale,
                        original.Padding.Right * controlScale,
                        original.Padding.Bottom * controlScale
                    );
                    control.MinHeight = original.MinHeight * Math.Max(controlScale, fontScale);
                    control.MinWidth = original.MinWidth * controlScale;
                    if (!double.IsNaN(original.Height))
                        control.Height = original.Height * Math.Max(controlScale, fontScale);
                }
            }
        }
        if (Sidebar.Parent is Grid layout)
        {
            var width = 204 * Math.Max(menuScale, fontScale);
            layout.ColumnDefinitions[0].Width = new GridLength(width);
            CustomTitleBar.ColumnDefinitions[0].Width = new GridLength(width);
        }
        UpdateTaskDrawerWidth();
    }

    void Personalization()
    {
        themeCatalog.Reload();
        ApplyTheme();
        Header(Ui("Personalization"), "");
        var settings = store.Config.Settings;
        var theme = new BelowComboBox
        {
            Header = Ui("Appearance"),
            ItemsSource = new[] { Ui("Light"), Ui("Dark"), Ui("System") },
            SelectedIndex = settings.Theme switch
            {
                "light" => 0,
                "system" => 2,
                _ => 1,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        theme.SelectionChanged += async (_, _) =>
        {
            if (syncingAppearanceControls || theme.SelectedIndex < 0)
                return;
            settings.Theme = theme.SelectedIndex switch
            {
                0 => "light",
                2 => "system",
                _ => "dark",
            };
            ApplyTheme();
            await SaveAppearance();
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        theme,
                        ThemeEditor(() =>
                        {
                            foreach (var control in AppearanceElements(PageBody))
                            {
                                if (
                                    control is ToggleSwitch toggle
                                    && toggle.Tag?.ToString() == "show-scrollbars"
                                )
                                    toggle.IsOn = false;
                                else if (
                                    control is NumberBox number
                                    && number.Header?.ToString() == Ui("Control size / %")
                                )
                                    number.Value = 100;
                                else if (
                                    control is NumberBox buttonNumber
                                    && buttonNumber.Header?.ToString() == Ui("Button size / %")
                                )
                                    buttonNumber.Value = 100;
                                else if (
                                    control is NumberBox menuNumber
                                    && menuNumber.Header?.ToString() == Ui("Menu size / %")
                                )
                                    menuNumber.Value = 100;
                                else if (
                                    control is NumberBox size
                                    && size.Header?.ToString() == Ui("Font size")
                                )
                                    size.Value = 14;
                                else if (
                                    control is ComboBox fontChoice
                                    && fontChoice.Header?.ToString() == Ui("Font family")
                                )
                                    fontChoice.SelectedItem = LocalizedFontName(DefaultFontFamily);
                            }
                        }),
                    },
                }
            )
        );
        var showScrollBars = new ToggleSwitch
        {
            Header = Ui("Show scrollbars"),
            IsOn = settings.ShowScrollBars,
            Tag = "show-scrollbars",
        };
        showScrollBars.Toggled += async (_, _) =>
        {
            if (syncingAppearanceControls)
                return;
            settings.ShowScrollBars = showScrollBars.IsOn;
            await SaveAppearance();
        };
        var micaEffect = new ToggleSwitch
        {
            Header = Ui("Liquid glass effect"),
            IsOn = settings.MicaEffect,
            Tag = "mica-effect",
        };
        var glassStyle = new BelowComboBox
        {
            Header = Ui("Glass style"),
            ItemsSource = new[] { Ui("Transparent glass"), Ui("Aero glass (Win7)") },
            SelectedIndex = settings.GlassStyle == "aero" ? 1 : 0,
            Tag = "glass-style",
            Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var showGlassCardBorders = new ToggleSwitch
        {
            Header = Ui("Show card borders"),
            IsOn = settings.ShowGlassCardBorders,
            Tag = "show-glass-card-borders",
        };
        string OpacityLabel(int percent) => $"{Ui("Glass opacity / %")} · {percent}%";
        var glassOpacity = new Slider
        {
            Header = OpacityLabel(Math.Clamp(settings.GlassOpacityPercent, 0, 100)),
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Value = Math.Clamp(settings.GlassOpacityPercent, 0, 100),
        };
        var opacitySaveTimer = DispatcherQueue.CreateTimer();
        opacitySaveTimer.Interval = TimeSpan.FromMilliseconds(250);
        opacitySaveTimer.IsRepeating = false;
        opacitySaveTimer.Tick += async (_, _) => await SaveAppearance();
        glassOpacity.ValueChanged += (_, args) =>
        {
            if (syncingAppearanceControls || !double.IsFinite(args.NewValue))
                return;
            settings.GlassOpacityPercent = (int)Math.Round(Math.Clamp(args.NewValue, 0, 100));
            glassOpacity.Header = OpacityLabel(settings.GlassOpacityPercent);
            ApplyTheme();
            opacitySaveTimer.Stop();
            opacitySaveTimer.Start();
        };
        // 背景模糊独立于卡片不透明度：单独控制窗口背后的系统模糊材质。
        var blurBackground = new ToggleSwitch
        {
            Header = Ui("Blur background"),
            IsOn = settings.GlassBackgroundBlur,
            Tag = "glass-background-blur",
        };
        blurBackground.Toggled += async (_, _) =>
        {
            if (syncingAppearanceControls)
                return;
            settings.GlassBackgroundBlur = blurBackground.IsOn;
            try
            {
                ApplyBackdrop(micaEffect.IsOn);
            }
            catch (Exception ex)
            {
                // 系统材质切换不应中断设置保存或背景刷新，记录后回退。
                RuntimeLog.Write("ERROR", $"Glass blur backdrop: {ex}");
            }
            ApplyTheme();
            await SaveAppearance();
        };
        var glassOptions = new Expander
        {
            Header = Ui("Glass settings"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    AppearanceRow(glassStyle, showGlassCardBorders),
                    glassOpacity,
                    blurBackground,
                },
            },
        };
        void RefreshGlassOptions() =>
            glassOptions.Visibility = micaEffect.IsOn ? Visibility.Visible : Visibility.Collapsed;

        micaEffect.Toggled += async (_, _) =>
        {
            if (syncingAppearanceControls)
                return;
            settings.MicaEffect = micaEffect.IsOn;
            if (!micaEffect.IsOn)
                glassOptions.IsExpanded = false;
            RefreshGlassOptions();
            try
            {
                ApplyBackdrop(micaEffect.IsOn);
            }
            catch (Exception ex)
            {
                // 材质切换不应中断设置保存或背景刷新，记录后继续恢复不透明背景。
                RuntimeLog.Write("ERROR", $"Liquid glass backdrop: {ex}");
            }
            finally
            {
                // 无论材质是否切换成功，都用最新设置刷新背景与标题栏颜色。
                ApplyTheme();
                await SaveAppearance();
            }
        };
        glassStyle.SelectionChanged += async (_, _) =>
        {
            if (syncingAppearanceControls)
                return;
            settings.GlassStyle = glassStyle.SelectedIndex == 1 ? "aero" : "transparent";
            ApplyTheme();
            await SaveAppearance();
        };
        showGlassCardBorders.Toggled += async (_, _) =>
        {
            if (syncingAppearanceControls)
                return;
            settings.ShowGlassCardBorders = showGlassCardBorders.IsOn;
            foreach (var card in themeCards.Concat(taskCards))
                ApplyCardTheme(card);
            await SaveAppearance();
        };
        RefreshGlassOptions();
        // 自定义背景图：选择一张本地图片铺在内容区底层。选择后立即应用并保存，
        // 移除按钮恢复为默认主题底色。文件名在当前界面语言下线性显示。
        var backgroundFileName = new TextBlock
        {
            Text = BackgroundLabel(settings.CustomBackground),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        var chooseBackground = Action(
            Ui("Choose background"),
            async (_, _) =>
            {
                var picker = new DesktopFileOpenPicker();
                DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" })
                    picker.FileTypeFilter.Add(ext);
                foreach (var ext in WallpaperVideoExtensions)
                    picker.FileTypeFilter.Add(ext);
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;
                try
                {
                    settings.CustomBackground = file.Path;
                    // 强制重载图片源以便切换图片后立刻生效。
                    backgroundFileName.Text = BackgroundLabel(settings.CustomBackground);
                    ApplyTheme();
                    await SaveAppearance();
                }
                catch (Exception ex)
                {
                    await Error(ex);
                }
            }
        );
        var removeBackground = Action(
            Ui("Remove background"),
            async (_, _) =>
            {
                settings.CustomBackground = "";
                backgroundFileName.Text = BackgroundLabel("");
                ApplyTheme();
                await SaveAppearance();
            }
        );
        var backgroundControls = new StackPanel
        {
            Spacing = 6,
            Children = { Buttons(chooseBackground, removeBackground), backgroundFileName },
        };
        var scale = new NumberBox
        {
            Header = Ui("Control size / %"),
            Value = Math.Clamp(settings.UiScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var buttonScale = new NumberBox
        {
            Header = Ui("Button size / %"),
            Value = Math.Clamp(settings.UiButtonScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var menuScale = new NumberBox
        {
            Header = Ui("Menu size / %"),
            Value = Math.Clamp(settings.UiMenuScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var fonts = InstalledFontFamilies(settings.UiFontFamily).ToList();
        if (string.IsNullOrWhiteSpace(settings.UiFontFamily))
            settings.UiFontFamily = DefaultFontFamily;
        var font = new BelowComboBox
        {
            Header = Ui("Font family"),
            ItemsSource = fonts.Select(LocalizedFontName).ToArray(),
            SelectedItem = LocalizedFontName(settings.UiFontFamily),
            MaxDropDownHeight = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = new FontFamily(settings.UiFontFamily),
            ItemFontFactory = display => new FontFamily(FontFamilyForDisplay(display)),
        };
        var fontSize = new NumberBox
        {
            Header = Ui("Font size"),
            Value = Math.Clamp(settings.UiFontSize, 12, 22),
            Minimum = 12,
            Maximum = 22,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        scale.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(scale.Value) || scale.Value < 80 || scale.Value > 150)
                return;
            settings.UiScale = scale.Value / 100;
            await SaveAppearance();
        };
        buttonScale.ValueChanged += async (_, _) =>
        {
            if (
                !double.IsFinite(buttonScale.Value)
                || buttonScale.Value < 80
                || buttonScale.Value > 150
            )
                return;
            settings.UiButtonScale = buttonScale.Value / 100;
            await SaveAppearance();
        };
        menuScale.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(menuScale.Value) || menuScale.Value < 80 || menuScale.Value > 150)
                return;
            settings.UiMenuScale = menuScale.Value / 100;
            await SaveAppearance();
        };
        font.SelectionChanged += async (_, _) =>
        {
            if (font.SelectedItem is string family && family.Length > 0)
            {
                settings.UiFontFamily = FontFamilyForDisplay(family);
                font.FontFamily = new FontFamily(settings.UiFontFamily);
                await SaveAppearance();
            }
        };
        fontSize.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(fontSize.Value) || fontSize.Value < 12 || fontSize.Value > 22)
                return;
            settings.UiFontSize = fontSize.Value;
            await SaveAppearance();
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        AppearanceRow(showScrollBars, micaEffect),
                        glassOptions,
                        backgroundControls,
                        scale,
                        buttonScale,
                        menuScale,
                        font,
                        fontSize,
                    },
                }
            )
        );
        var presetNames = new BelowComboBox
        {
            Header = Ui("Saved presets"),
            ItemsSource = settings.CustomAppearancePresets.Select(p => p.Name).ToArray(),
            PlaceholderText = Ui("Select a saved preset"),
            MaxDropDownHeight = 260,
            IsEnabled = settings.CustomAppearancePresets.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var presetName = new TextBox
        {
            PlaceholderText = Ui("Preset name"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var updatingPresetList = false;
        Button deletePreset = null!;
        deletePreset = Action(
            Ui("Delete selected preset"),
            async (sender, _) =>
            {
                if (presetNames.SelectedItem is not string name)
                    return;
                var preset = settings.CustomAppearancePresets.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (preset == null)
                    return;
                if (!await ConfirmPop((FrameworkElement)sender, Ui("Delete preset?"), name))
                    return;
                settings.CustomAppearancePresets.Remove(preset);
                updatingPresetList = true;
                presetNames.ItemsSource = settings
                    .CustomAppearancePresets.Select(p => p.Name)
                    .ToArray();
                presetNames.SelectedIndex = -1;
                presetNames.IsEnabled = settings.CustomAppearancePresets.Count > 0;
                updatingPresetList = false;
                await store.Save();
                ShowToast(Ui("Preset deleted") + " · " + name);
                DispatcherQueue.TryEnqueue(() =>
                {
                    deletePreset.IsEnabled =
                        presetNames.SelectedItem is string selectedName
                        && settings.CustomAppearancePresets.Any(p =>
                            string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase)
                        );
                });
            }
        );
        deletePreset.IsEnabled = false;
        presetNames.SelectionChanged += (_, _) =>
            deletePreset.IsEnabled =
                presetNames.SelectedItem is string selectedName
                && settings.CustomAppearancePresets.Any(p =>
                    string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase)
                );
        var savePreset = Action(
            Ui("Save current settings as preset"),
            async (_, _) =>
            {
                var name = presetName.Text.Trim();
                if (name.Length == 0)
                    return;
                var preset = settings.CustomAppearancePresets.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                preset ??= new CustomAppearancePreset { Name = name };
                preset.Theme = settings.Theme;
                preset.ColorPreset = settings.ColorPreset;
                preset.ShowScrollBars = settings.ShowScrollBars;
                preset.UiScale = settings.UiScale;
                preset.UiFontFamily = settings.UiFontFamily;
                preset.UiFontSize = settings.UiFontSize;
                preset.UiButtonScale = settings.UiButtonScale;
                preset.UiMenuScale = settings.UiMenuScale;
                preset.CustomLightColors = new(settings.CustomLightColors);
                preset.CustomDarkColors = new(settings.CustomDarkColors);
                if (!settings.CustomAppearancePresets.Contains(preset))
                    settings.CustomAppearancePresets.Add(preset);
                updatingPresetList = true;
                presetNames.ItemsSource = settings
                    .CustomAppearancePresets.Select(p => p.Name)
                    .ToArray();
                presetNames.SelectedIndex = -1;
                presetNames.IsEnabled = true;
                updatingPresetList = false;
                await store.Save();
            }
        );
        var importPreset = Action(
            Ui("Import preset"),
            async (_, _) =>
            {
                var picker = new DesktopFileOpenPicker();
                DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;
                try
                {
                    var importName = presetName.Text.Trim();
                    if (importName.Length == 0)
                        importName = Path.GetFileNameWithoutExtension(file.Name);
                    if (importName.Length > 64)
                        importName = importName[..64];
                    var imported = ImportAppearancePreset(
                        await File.ReadAllTextAsync(file.Path),
                        importName
                    );
                    var baseName = imported.Name;
                    for (
                        var suffix = 2;
                        settings.CustomAppearancePresets.Any(p =>
                            string.Equals(p.Name, imported.Name, StringComparison.OrdinalIgnoreCase)
                        );
                        suffix++
                    )
                    {
                        var ending = $" ({suffix})";
                        imported.Name =
                            baseName[..Math.Min(baseName.Length, 64 - ending.Length)] + ending;
                    }
                    settings.CustomAppearancePresets.Add(imported);
                    updatingPresetList = true;
                    presetNames.ItemsSource = settings
                        .CustomAppearancePresets.Select(p => p.Name)
                        .ToArray();
                    presetNames.SelectedIndex = -1;
                    presetNames.IsEnabled = true;
                    updatingPresetList = false;
                    await store.Save();
                    Report(Ui("Preset imported."));
                }
                catch (Exception ex)
                {
                    await Error(ex);
                }
            }
        );
        presetNames.SelectionChanged += async (_, _) =>
        {
            if (updatingPresetList || presetNames.SelectedItem is not string name)
                return;
            var preset = settings.CustomAppearancePresets.FirstOrDefault(p => p.Name == name);
            if (preset == null)
                return;
            presetNames.IsDropDownOpen = false;
            settings.Theme = preset.Theme;
            settings.ColorPreset = preset.ColorPreset;
            settings.ShowScrollBars = preset.ShowScrollBars;
            settings.UiScale = preset.UiScale;
            settings.UiButtonScale = preset.UiButtonScale;
            settings.UiMenuScale = preset.UiMenuScale;
            settings.UiFontFamily = preset.UiFontFamily;
            settings.UiFontSize = preset.UiFontSize;
            settings.CustomLightColors = new(preset.CustomLightColors);
            settings.CustomDarkColors = new(preset.CustomDarkColors);
            ApplyTheme();
            ApplyPersonalization();
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                await Error(ex);
                return;
            }
            syncingAppearanceControls = true;
            try
            {
                showScrollBars.IsOn = settings.ShowScrollBars;
                micaEffect.IsOn = settings.MicaEffect;
                glassStyle.SelectedIndex = settings.GlassStyle == "aero" ? 1 : 0;
                showGlassCardBorders.IsOn = settings.ShowGlassCardBorders;
                blurBackground.IsOn = settings.GlassBackgroundBlur;
                RefreshGlassOptions();
                foreach (var control in AppearanceElements(PageBody))
                {
                    if (control is ComboBox combo && combo.Header?.ToString() == Ui("Appearance"))
                        combo.SelectedIndex = settings.Theme switch
                        {
                            "light" => 0,
                            "system" => 2,
                            _ => 1,
                        };
                    else if (
                        control is NumberBox number
                        && number.Header?.ToString() == Ui("Control size / %")
                    )
                        number.Value = settings.UiScale * 100;
                    else if (
                        control is NumberBox buttonNumber
                        && buttonNumber.Header?.ToString() == Ui("Button size / %")
                    )
                        buttonNumber.Value = settings.UiButtonScale * 100;
                    else if (
                        control is NumberBox menuNumber
                        && menuNumber.Header?.ToString() == Ui("Menu size / %")
                    )
                        menuNumber.Value = settings.UiMenuScale * 100;
                    else if (
                        control is NumberBox size
                        && size.Header?.ToString() == Ui("Font size")
                    )
                        size.Value = settings.UiFontSize;
                    else if (
                        control is ComboBox fontChoice
                        && fontChoice.Header?.ToString() == Ui("Font family")
                    )
                    {
                        fontChoice.SelectedItem = LocalizedFontName(settings.UiFontFamily);
                        fontChoice.FontFamily = new FontFamily(settings.UiFontFamily);
                    }
                    else if (
                        control is ComboBox colorPreset
                        && colorPreset.Header?.ToString() == Ui("Preset")
                    )
                        colorPreset.SelectedIndex = ColorPresetIndex(settings.ColorPreset);
                }
            }
            finally
            {
                syncingAppearanceControls = false;
            }
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        presetNames,
                        presetName,
                        Buttons(savePreset, deletePreset),
                        importPreset,
                    },
                }
            )
        );
        PageBody.Children.Add(
            Action(
                Ui("context.personalization.export_personalization"),
                async (_, _) =>
                {
                    var picker = new DesktopFileSavePicker
                    {
                        SuggestedFileName = "CMCLauncher-personalization",
                    };
                    DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(this));
                    picker.FileTypeChoices.Add("JSON", [".json"]);
                    var file = await picker.PickSaveFileAsync();
                    if (file == null)
                        return;
                    await File.WriteAllTextAsync(file.Path, AppearanceJson());
                    Report(Ui("context.personalization.personalization_exported"));
                }
            )
        );
    }

    // 合并系统和用户安装字体，同时保留当前名称，避免缺失字体让下拉框丢失选中项。
    static IReadOnlyList<string> InstalledFontFamilies(string current)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Segoe UI Variable Text",
            "Segoe UI",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Consolas",
        };
        if (!string.IsNullOrWhiteSpace(current))
            names.Add(current);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key == null)
                continue;
            foreach (var valueName in key.GetValueNames())
            {
                var family = valueName.Split('(')[0].Trim();
                if (family.Length > 0)
                    names.Add(family);
            }
        }
        return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // 背景文件名的显示文本：有选择时显示完整路径，"不启用"提示用于空状态。
    string BackgroundLabel(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? Ui("No background selected")
            : Path.GetFileName(path) + " · " + path;

    static Grid AppearanceRow(FrameworkElement left, FrameworkElement right)
    {
        var row = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new(), new() },
            Children = { left, right },
        };
        Grid.SetColumn(right, 1);
        return row;
    }

    async Task SaveAppearance()
    {
        if (syncingAppearanceControls)
            return;
        ApplyPersonalization();
        try
        {
            await store.Save();
        }
        catch (Exception ex)
        {
            await Error(ex);
        }
    }

    string AppearanceJson()
    {
        var s = store.Config.Settings;
        return JsonSerializer.Serialize(
            new
            {
                format = "CMCLauncher.Personalization",
                version = 1,
                s.Theme,
                s.ColorPreset,
                s.CustomLightColors,
                s.CustomDarkColors,
                s.ShowScrollBars,
                s.UiScale,
                s.UiButtonScale,
                s.UiMenuScale,
                s.UiFontFamily,
                s.UiFontSize,
            },
            Store.JsonOptions
        );
    }

    // 导入前检查格式版本和数值范围，不把任意 JSON 当作启动器配置应用。
    CustomAppearancePreset ImportAppearancePreset(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.GetString() != "CMCLauncher.Personalization"
            || !root.TryGetProperty("version", out var version)
            || version.GetInt32() != 1
        )
            throw new InvalidDataException("不支持的个性化预设文件格式。");
        var preset =
            JsonSerializer.Deserialize<CustomAppearancePreset>(json, Store.JsonOptions)
            ?? throw new InvalidDataException("预设文件为空。");
        preset.Name = string.IsNullOrWhiteSpace(name) ? "导入的预设" : name.Trim();
        if (preset.Name.Length > 64)
            throw new InvalidDataException("预设名称不能超过 64 个字符。");
        if (
            preset.Theme is not ("light" or "dark" or "system")
            || !ColorPresetIds.Contains(preset.ColorPreset)
        )
            throw new InvalidDataException("预设包含不支持的主题或配色类型。");
        if (
            !ValidScale(preset.UiScale)
            || !ValidScale(preset.UiButtonScale)
            || !ValidScale(preset.UiMenuScale)
            || !double.IsFinite(preset.UiFontSize)
            || preset.UiFontSize is < 12 or > 22
        )
            throw new InvalidDataException("预设中的缩放或字号超出支持范围。");
        if (string.IsNullOrWhiteSpace(preset.UiFontFamily) || preset.UiFontFamily.Length > 128)
            throw new InvalidDataException("预设中的字体名称无效。");
        ValidateColors(preset.CustomLightColors);
        ValidateColors(preset.CustomDarkColors);
        return preset;

        static bool ValidScale(double value) => double.IsFinite(value) && value is >= .8 and <= 1.5;
        static void ValidateColors(Dictionary<string, string>? colors)
        {
            if (
                colors == null
                || colors.Count > 7
                || colors.Any(pair =>
                    pair.Key
                        is not (
                            "background"
                            or "sidebar"
                            or "surface"
                            or "text"
                            or "muted"
                            or "border"
                            or "accent"
                        )
                    || !Regex.IsMatch(pair.Value ?? "", "^#[0-9A-Fa-f]{6}$")
                )
            )
                throw new InvalidDataException("预设中的颜色值无效。");
        }
    }
}