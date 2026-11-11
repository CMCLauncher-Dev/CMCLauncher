using System.Text;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace CMCLauncher.WinUI;

// 启动游戏时单独弹出的调试窗口：实时显示启动阶段与运行日志，配色与主窗口保持一致。
public sealed class GameDebugWindow : Window
{
    readonly bool lightTheme;
    readonly TextBlock stageText;
    readonly ProgressBar progressBar;
    readonly TextBox logView;
    bool closed;

    public GameDebugWindow(bool lightTheme, string accentHex, string gameName)
    {
        this.lightTheme = lightTheme;
        Title = "CMCLauncher · " + gameName + " 调试";

        var bg = FromHex(lightTheme ? ThemeTokens.LightBg : ThemeTokens.DarkBg);
        var surface = FromHex(lightTheme ? ThemeTokens.LightSurface : ThemeTokens.DarkSurface);
        var text = FromHex(lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText);
        var muted = FromHex(lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted);
        var border = FromHex(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder);
        var accent = FromHex(accentHex);

        var root = new Grid
        {
            Background = new SolidColorBrush(bg),
            Padding = new Thickness(16, 14, 16, 12),
            RowSpacing = 8,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        root.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;

        var title = new TextBlock
        {
            Text = "游戏启动调试",
            Foreground = new SolidColorBrush(text),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };

        stageText = new TextBlock
        {
            Text = "正在准备启动…",
            Foreground = new SolidColorBrush(muted),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 2),
            TextWrapping = TextWrapping.Wrap,
        };

        progressBar = new ProgressBar
        {
            Height = 5,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = new SolidColorBrush(accent),
        };

        logView = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Background = new SolidColorBrush(surface),
            Foreground = new SolidColorBrush(text),
            BorderBrush = new SolidColorBrush(border),
            FontSize = 12,
        };

        Grid.SetRow(title, 0);
        Grid.SetRow(stageText, 1);
        Grid.SetRow(progressBar, 2);
        Grid.SetRow(logView, 3);
        stageText.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(title);
        root.Children.Add(stageText);
        root.Children.Add(progressBar);
        root.Children.Add(logView);
        Content = root;

        AppWindow.Resize(new SizeInt32(640, 420));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.ico"));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;

        RuntimeLog.Written += OnRuntimeLog;
        Closed += (_, _) =>
        {
            closed = true;
            RuntimeLog.Written -= OnRuntimeLog;
        };
    }

    static Windows.UI.Color FromHex(string hex) =>
        Color.FromArgb(
            255,
            Convert.ToByte(hex[1..3], 16),
            Convert.ToByte(hex[3..5], 16),
            Convert.ToByte(hex[5..7], 16)
        );

    void OnRuntimeLog(string line)
    {
        if (closed)
            return;
        if (DispatcherQueue.HasThreadAccess)
            Append(line);
        else
            DispatcherQueue.TryEnqueue(() => Append(line));
    }

    void Append(string line)
    {
        if (logView == null)
            return;
        var next = logView.Text + Environment.NewLine + line;
        if (next.Length > 200_000)
            next = next[^200_000..];
        logView.Text = next;
        logView.SelectionStart = logView.Text.Length;
        logView.SelectionLength = 0;
    }

    public void SetProgress(double value, string stage)
    {
        if (closed)
            return;
        void Apply()
        {
            progressBar.Value = Math.Clamp(value, 0, 100);
            if (!string.IsNullOrEmpty(stage))
                stageText.Text = stage;
        }
        if (DispatcherQueue.HasThreadAccess)
            Apply();
        else
            DispatcherQueue.TryEnqueue(Apply);
    }

    // 从任意线程安全地关闭窗口并释放日志订阅。
    public void CloseSafe()
    {
        if (closed)
            return;
        closed = true;
        DispatcherQueue.TryEnqueue(Close);
    }
}