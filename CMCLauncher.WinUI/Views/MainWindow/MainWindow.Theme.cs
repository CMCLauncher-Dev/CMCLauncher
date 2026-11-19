using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal bool lightTheme;

    // 记住当前页面的卡片，换主题时同时改背景和边框；导航到别页时会清空。
    readonly List<Border> themeCards = new();
    readonly UISettings systemUi = new();

    // 自定义背景图画刷：作为根 Grid 的背景铺满全窗（含标题栏）。用 Background 而非 Grid
    // 子元素，背景不参与布局测量，避免切换大图时把侧边栏等子元素一步步压缩变小。
    readonly ImageBrush backgroundBrush = new() { Stretch = Stretch.UniformToFill };
    static readonly SolidColorBrush CommonBackground = new(Color.FromArgb(0, 0, 0, 0));

    // 系统通知不保证在 UI 线程触发，先排回窗口线程再操作控件。
    void SystemThemeChanged(UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (store.Config.Settings.Theme == "system")
                ApplyTheme();
        });

    // 设置变更和系统主题通知都会走这里，颜色预设与浅/深模式分别处理。
    void ApplyTheme()
    {
        LocalizeStaticChrome();
        var systemBackground = systemUi.GetColorValue(UIColorType.Background);
        lightTheme =
            store.Config.Settings.Theme == "light"
            || (
                store.Config.Settings.Theme == "system"
                && systemBackground.R + systemBackground.G + systemBackground.B > 382
            );
        // 先让 XAML 的系统资源换到目标主题，再读取 Native* 的颜色刷新画刷。
        if (Content is FrameworkElement themedRoot)
            themedRoot.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        RefreshPaletteBrushes();
        RefreshComboPalette();
        RefreshToastPalette();
        RefreshVersionDeletePalette();
        if (page == "frpc")
            RefreshFrpcPalette();
        var micaEnabled = store.Config.Settings.MicaEffect;
        // 开启液态玻璃时给上层内容/标题栏背景保留 20% 不透明度（alpha≈0x33），
        // 底部仍由 TransparentBackdrop + WS_EX_LAYERED 保持纯透明，透出桌面内容。
        Color Backdrop(Color c) =>
            micaEnabled ? Color.FromArgb(0x33, c.R, c.G, c.B) : c;
        // Win7 Aero 风格：在透明玻璃之上叠加顶部弧形高光，模拟 Aero 的窗框反光。
        // 仅当液态玻璃开启且 GlassStyle=="aero" 时显示；深色主题反光更明显。
        var aeroGlass = micaEnabled && store.Config.Settings.GlassStyle == "aero";
        if (AeroGlassHighlight != null)
        {
            AeroGlassHighlight.Visibility = aeroGlass
                ? Visibility.Visible
                : Visibility.Collapsed;
            AeroGlassHighlight.Opacity = lightTheme ? 0.55 : 0.8;
        }
        // 自定义背景图：设置中选择了图片路径就显示，并在底色与侧边栏上降低不透明度，
        // 让图片从内容区下方透出；同时保持文字/卡片前景不透明以保证可读性。
        var customBackground = store.Config.Settings.CustomBackground;
        var hasCustomBackground =
            !string.IsNullOrWhiteSpace(customBackground) && File.Exists(customBackground);
        if (hasCustomBackground)
        {
            try
            {
                // 直接换 ImageSource 即可，ImageBrush 作为 Grid 背景不参与布局测量。
                backgroundBrush.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(customBackground, UriKind.Absolute)
                );
                ModpackDropRoot.Background = backgroundBrush;
            }
            catch
            {
                ModpackDropRoot.Background = CommonBackground;
                hasCustomBackground = false;
            }
        }
        else
        {
            // 无自定义背景时恢复透明，保持 WinUI 原有的透明/液态玻璃表现。
            backgroundBrush.ImageSource = null;
            ModpackDropRoot.Background = CommonBackground;
        }
        var bg = lightTheme ? ThemeTokens.LightBg : ThemeTokens.DarkBg;
        var sidebar = lightTheme ? ThemeTokens.LightSidebar : ThemeTokens.DarkSidebar;
        var text = lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText;
        var muted = lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted;
        var accent = lightTheme ? ThemeTokens.LightAccent : ThemeTokens.LightAccentText;
        // 自定义背景启用时把底色压到半透明（0x33，约 20% 不透明），让下方图片清晰透出；
        // 关闭时沿用原有的液态玻璃 0x33 / 不透明底色逻辑。文字与卡片仍保持不透明保证可读。
        Color ContentBackdrop(Color c) =>
            hasCustomBackground
                ? Color.FromArgb(0x33, c.R, c.G, c.B)
                : Backdrop(c);
        var bgBrush = Brush(bg);
        WindowBackground.Color = hasCustomBackground
            ? Color.FromArgb(0x00, bgBrush.Color.R, bgBrush.Color.G, bgBrush.Color.B)
            : Backdrop(bgBrush.Color);
        if (Sidebar.Parent is Panel contentGrid)
            contentGrid.Background = new SolidColorBrush(ContentBackdrop(bgBrush.Color));
        Sidebar.Background = new SolidColorBrush(ContentBackdrop(Brush(sidebar).Color));
        // 液态玻璃下各层底色都是半透明，不透明的分隔线会显成一条黑线，直接隐藏。
        Sidebar.BorderBrush = micaEnabled
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(
                ContentBackdrop(
                    store.Config.Settings.ColorPreset == "native"
                        ? RawColor(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorderAlt)
                        : PaletteColor(
                            lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorderAlt
                        )
                )
            );
        SidebarNote.Foreground = Brush(muted);
        Eyebrow.Foreground = Brush(accent);
        PageTitle.Foreground = Brush(text);
        PageSubtitle.Foreground = Brush(muted);
        StatusText.Foreground = Brush(muted);
        TaskManagerButton.Foreground = Brush(text);
        StatusText.Visibility = Visibility.Collapsed;
        if (launchStatusText != null)
            launchStatusText.Foreground = Brush(muted);
        if (Content is FrameworkElement root)
            root.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        SyncNavigation();
        TaskDrawer.PaneBackground = Brush(sidebar);
        CommunityFilterDrawer.PaneBackground = Brush(bg);
        CommunityFilterDrawerTitle.Foreground = Brush(text);
        CommunityFilterDrawerHint.Foreground = Brush(muted);
        CommunityFilterDrawerFooter.BorderBrush = Brush(
            lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder
        );
        CommunityFilterApplyButton.Background = ComboSelectedBrush;
        CommunityFilterApplyButton.Foreground = ComboTextBrush;
        CommunityFilterApplyButton.BorderBrush = ComboAccentBrush;
        TaskCountBadge.Background = Brush(PrimaryColor);
        TaskCountText.Foreground = new SolidColorBrush(Colors.White);
        CustomTitleBar.Background = new SolidColorBrush(ContentBackdrop(Brush(bg).Color));
        TitleBrandBackground.Background = new SolidColorBrush(
            ContentBackdrop(Brush(sidebar).Color)
        );
        TitleLabel.Foreground = Brush(text);
        foreach (var card in themeCards.Concat(taskCards))
            ApplyCardTheme(card);
        // 标题栏按钮由系统绘制，不会跟随 XAML 画刷刷新，需要单独赋色。
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = Backdrop(Brush(bg).Color);
            bar.ForegroundColor = Brush(text).Color;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = Brush(text).Color;
            bar.ButtonHoverBackgroundColor = Brush(
                lightTheme ? ThemeTokens.LightTitlebarHover : ThemeTokens.DarkTitlebarHover
            ).Color;
            bar.ButtonHoverForegroundColor = Brush(text).Color;
            bar.ButtonPressedBackgroundColor = Brush(
                lightTheme ? ThemeTokens.LightTitlebarPressed : ThemeTokens.DarkTitlebarPressed
            ).Color;
            bar.ButtonPressedForegroundColor = Brush(text).Color;
            bar.InactiveBackgroundColor = Backdrop(Brush(bg).Color);
            bar.InactiveForegroundColor = Brush(muted).Color;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveForegroundColor = Brush(muted).Color;
        }
    }

    // 新建与已显示的卡片采用同一取色规则，避免首次进入页面时仍使用旧的表面色。
    void ApplyCardTheme(Border card)
    {
        var surface = SoftSurfaceColor();
        var hasCustomBackground =
            !string.IsNullOrWhiteSpace(store.Config.Settings.CustomBackground)
            && File.Exists(store.Config.Settings.CustomBackground);
        // 卡片是否透明的规则：
        // - 启用自定义背景时，由"卡片跟随背景透明化"开关决定半透明或不透明；
        // - 未启用背景图、仅开启液态玻璃时，按液态玻璃的 0x33 半透明；
        // - 其余情况保持不透明，保证文字与控件清晰可读。
        var followGlass =
            hasCustomBackground
                ? store.Config.Settings.CardFollowBackground
                : store.Config.Settings.MicaEffect;
        if (followGlass)
            surface = Color.FromArgb(0x33, surface.R, surface.G, surface.B);
        card.Background = new SolidColorBrush(surface);
        card.BorderBrush = Brush(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder);
    }

    readonly SolidColorBrush versionDeleteText = new();
    readonly SolidColorBrush versionDeleteBackground = new();
    readonly SolidColorBrush versionDeleteHover = new();
    readonly SolidColorBrush versionDeletePressed = new();

    // Destructive actions stay red independently of the selected theme accent.
    void RefreshVersionDeletePalette()
    {
        versionDeleteText.Color = RawColor(lightTheme ? "#B42318" : "#FF99A4");
        versionDeleteBackground.Color = RawColor(lightTheme ? "#FFF1F2" : "#35191D");
        versionDeleteHover.Color = RawColor(lightTheme ? "#FFE4E6" : "#4B2027");
        versionDeletePressed.Color = RawColor(lightTheme ? "#FECDD3" : "#602A34");
    }

    Button VersionDeleteButton(Button button)
    {
        button.CornerRadius = new CornerRadius(5);
        button.Foreground = versionDeleteText;
        button.BorderBrush = versionDeleteText;
        button.Background = versionDeleteBackground;
        button.Resources["ButtonBackgroundPointerOver"] = versionDeleteHover;
        button.Resources["ButtonBackgroundPressed"] = versionDeletePressed;
        foreach (var state in new[] { "PointerOver", "Pressed" })
        {
            button.Resources["ButtonForeground" + state] = versionDeleteText;
            button.Resources["ButtonBorderBrush" + state] = versionDeleteText;
        }
        return button;
    }

    void UpdateTitleBarInset()
    {
        var scale = CustomTitleBar.XamlRoot?.RasterizationScale ?? 1;
        // RightInset 是物理像素，XAML 的宽度要先除以显示缩放比例。
        CaptionButtonInset.Width = Math.Max(138, AppWindow.TitleBar.RightInset / scale);
    }

    // 创建并登记可刷新配色的 SolidColorBrush，hex 为 #RRGGBB 基础色值，优先使用 ThemeTokens。
    // native/custom 按已知基础色值映射角色；未识别的色值会按 border 处理。
    internal SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(PaletteColor(hex));
        paletteBrushes.Add((new WeakReference<SolidColorBrush>(brush), hex));
        return brush;
    }

    // 当前浅/深模式的基础强调色 hex，传入 Brush 后再按当前配色预设解析
    internal string PrimaryColor => lightTheme ? ThemeTokens.LightAccent : ThemeTokens.DarkAccent;
}