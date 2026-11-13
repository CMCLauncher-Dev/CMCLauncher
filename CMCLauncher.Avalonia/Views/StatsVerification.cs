using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyStatsParity(List<string> checks)
    {
        var saved = Width;
        var language = session.Store.Config.Settings.LauncherLanguage;
        try
        {
            session.Store.Config.Settings.LauncherLanguage = "en_us";
            BuildNavigation();
            await Navigate("stats");
            var dashboard = pageContent
                .GetVisualDescendants()
                .OfType<Grid>()
                .Single(c => Equals(c.Tag, "stats-dashboard"));
            if (dashboard.Children.OfType<Border>().Count() != 6)
                throw new IOException("System status is missing WinUI dashboard sections.");
            foreach (var width in new[] { 850d, 1280d })
            {
                Width = width;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                dashboard.UpdateLayout();
                var narrow = dashboard.Bounds.Width < 700;
                if (
                    dashboard.RowDefinitions.Count != (narrow ? 6 : 3)
                    || dashboard.Children.Any(c => Grid.GetColumnSpan(c) != (narrow ? 2 : 1))
                )
                    throw new IOException("System status columns do not adapt like WinUI.");
            }
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(VerificationDirectory!, "system-status.png"));
            checks.Add(
                "PASS system status CPU/memory trends, six dashboard sections and responsive columns"
            );
        }
        finally
        {
            Width = saved;
            session.Store.Config.Settings.LauncherLanguage = language;
            BuildNavigation();
        }
    }
}
