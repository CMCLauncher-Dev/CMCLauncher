using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void AboutPage()
    {
        Header("About", "");
        var identity = new Grid { ColumnSpacing = 18 };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        identity.Children.Add(
            new Image
            {
                Source = new BitmapImage(
                    new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.png"))
                ),
                Width = 72,
                Height = 72,
                VerticalAlignment = VerticalAlignment.Top,
            }
        );
        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(Text("CMCLauncher", 24, true));
        details.Children.Add(Text("A fast, modern Minecraft launcher.", 13));
        details.Children.Add(Text(Labeled("Version", LauncherUpdates.CurrentVersion), 13));
        details.Children.Add(
            Text(
                Labeled(
                    "Build",
                    LauncherBuildInfo.IsRelease ? Ui("Release build") : Ui("Development build")
                ),
                13
            )
        );
        details.Children.Add(Text(Labeled("Build time", LauncherBuildInfo.BuildTime), 12));
        details.Children.Add(Text(Labeled("Commit", LauncherBuildInfo.GitCommit), 12));
        Grid.SetColumn(details, 1);
        identity.Children.Add(details);
        PageBody.Children.Add(Card(identity));
        PageBody.Children.Add(BuildLauncherUpdateSettings());
        var contributors = new StackPanel { Spacing = 12 };
        contributors.Children.Add(Text("Contributors", 18, true));
        contributors.Children.Add(Text("Ctoy · Fuzhu", 14));
        contributors.Children.Add(
            new HyperlinkButton
            {
                Content = Ui("View all contributors"),
                NavigateUri = new Uri("https://github.com/com-in/CMCLauncher/graphs/contributors"),
                HorizontalAlignment = HorizontalAlignment.Left,
            }
        );
        PageBody.Children.Add(Card(contributors));
    }

    string Labeled(string label, string value) => Ui(label) + "  " + value;
}
