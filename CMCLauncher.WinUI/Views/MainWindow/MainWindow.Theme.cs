using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
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

    // 动态壁纸视频：仅当选中的自定义背景是视频文件时创建并循环播放。
    // 视频层是 Grid 子元素但被锁定为根 Grid 的真实尺寸，且 MediaPlayerElement 不把
    // 媒体原始尺寸上报为 DesiredSize，因此不会像大图那样压缩侧边栏等 Star 列。
    MediaPlayer? wallpaperPlayer;

    // 支持的动态壁纸视频扩展名（含点）。与静态图一样，仅本地文件路径生效。
    static readonly HashSet<string> WallpaperVideoExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".mp4",
        ".mkv",
        ".webm",
        ".mov",
        ".m4v",
        ".avi",
        ".wmv",
        ".mpg",
        ".mpeg",
    };

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
        var micaEnabled = GlassActive;
        // 底部仍由 TransparentBackdrop + WS_EX_LAYERED 保持纯透明，透出桌面内容。
        Color Backdrop(Color c)
        {
            if (!micaEnabled)
                return c;
            // Keep the foreground readable over a bright or dark desktop.
            c.A = Math.Max(GlassSurface(c).A, lightTheme ? (byte)224 : (byte)232);
            return c;
        }
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
        var videoWallpaper =
            hasCustomBackground
            && WallpaperVideoExtensions.Contains(Path.GetExtension(customBackground));
        if (hasCustomBackground && videoWallpaper)
        {
            // 视频墙纸：交给 MediaPlayer 循环播放；旧画刷清空以免两层叠加。
            backgroundBrush.ImageSource = null;
            ModpackDropRoot.Background = CommonBackground;
            PlayWallpaperVideo(customBackground);
        }
        else if (hasCustomBackground)
        {
            StopWallpaperVideo();
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
            StopWallpaperVideo();
            backgroundBrush.ImageSource = null;
            ModpackDropRoot.Background = CommonBackground;
        }
        var bg = lightTheme ? ThemeTokens.LightBg : ThemeTokens.DarkBg;
        var sidebar = lightTheme ? ThemeTokens.LightSidebar : ThemeTokens.DarkSidebar;
        var text = lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText;
        var muted = lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted;
        var accent = lightTheme ? ThemeTokens.LightAccent : ThemeTokens.LightAccentText;
        Color ContentBackdrop(Color c) =>
            hasCustomBackground && !micaEnabled
                ? Color.FromArgb(0x33, c.R, c.G, c.B)
                : Backdrop(c);
        var bgBrush = Brush(bg);
        WindowBackground.Color = ContentBackdrop(bgBrush.Color);
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
        var titleSurface = ContentBackdrop(Brush(bg).Color);
        titleSurface.A = Math.Max(titleSurface.A, (byte)12);
        CustomTitleBar.Background = new SolidColorBrush(titleSurface);
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

    // 启动/切换到动态壁纸视频：循环播放、静音、隐藏全部控件。
    // MediaPlayerElement.MediaPlayer 在 WinUI 3 中是只读的，因此通过 Source 装载媒体，
    // 再读取其内部播放器设置循环与静音。
    void PlayWallpaperVideo(string path)
    {
        // 本地路径转成 file:/// 形式的绝对 Uri，保证 Media Foundation 能正确打开带空格/中文路径。
        var uri = new Uri("file:///" + Path.GetFullPath(path).Replace('\\', '/'));
        if (wallpaperPlayer == null)
        {
            VideoWallpaper.Source = MediaSource.CreateFromUri(uri);
            wallpaperPlayer = VideoWallpaper.MediaPlayer;
            if (wallpaperPlayer != null)
                wallpaperPlayer.MediaFailed += WallpaperMediaFailed;
            // 把视频层明确锁到根 Grid 的真实尺寸：媒体自然尺寸不再作为 DesiredSize
            // 参与根 Grid 的量测，避免像大图那样把侧边栏等 Star 列一步步压缩。
            SyncWallpaperSize();
            ModpackDropRoot.SizeChanged += (_, _) => SyncWallpaperSize();
        }
        else
        {
            wallpaperPlayer.Source = MediaSource.CreateFromUri(uri);
        }
        if (wallpaperPlayer == null)
            return;
        wallpaperPlayer.IsMuted = true;
        wallpaperPlayer.IsLoopingEnabled = true;
        VideoWallpaper.Opacity = 1;
        VideoWallpaper.Visibility = Visibility.Visible;
        wallpaperPlayer.Play();
    }

    // 视频层尺寸与根 Grid 保持一致；显式尺寸使布局系统直接接受而不做额外列/行分配。
    void SyncWallpaperSize()
    {
        VideoWallpaper.Width = ModpackDropRoot.ActualWidth;
        VideoWallpaper.Height = ModpackDropRoot.ActualHeight;
    }

    // 视频解码/加载失败时（如缺失编解码器）隐藏视频层，让窗口回到默认底色。
    void WallpaperMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
        VideoWallpaper.DispatcherQueue.TryEnqueue(() =>
        {
            StopWallpaperVideo();
            backgroundBrush.ImageSource = null;
            ModpackDropRoot.Background = CommonBackground;
        });

    // 停止动态壁纸视频并释放文件引用，让外部进程可删除/替换该文件。
    void StopWallpaperVideo()
    {
        if (wallpaperPlayer != null)
        {
            wallpaperPlayer.MediaFailed -= WallpaperMediaFailed;
            wallpaperPlayer.Pause();
            wallpaperPlayer.Source = null;
        }
        VideoWallpaper.Opacity = 0;
        VideoWallpaper.Visibility = Visibility.Collapsed;
    }

    // 新建与已显示的卡片采用同一取色规则，避免首次进入页面时仍使用旧的表面色。
    void ApplyCardTheme(Border card)
    {
        var settings = store.Config.Settings;
        var surface = SoftSurfaceColor();
        if (GlassActive)
            surface = GlassSurface(surface);
        card.Background = new SolidColorBrush(surface);
        card.BorderBrush = GlassActive && !settings.ShowGlassCardBorders
            ? new SolidColorBrush(Colors.Transparent)
            : Brush(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder);
    }

    Color GlassSurface(Color color) =>
        Color.FromArgb(
            (byte)Math.Round(Math.Clamp(store.Config.Settings.GlassOpacityPercent, 0, 100) * 255 / 100.0),
            color.R,
            color.G,
            color.B
        );

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
