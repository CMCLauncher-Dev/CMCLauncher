using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void AboutPage()
    {
        Header(Ui("About"), "");
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
        details.Children.Add(Text(Labeled("License", Ui("GNU AGPL v3")), 12));
        Grid.SetColumn(details, 1);
        identity.Children.Add(details);
        PageBody.Children.Add(Card(identity));
        PageBody.Children.Add(BuildLauncherUpdateSettings());
        PageBody.Children.Add(
            AcknowledgementCards(
                Ui("Contributors"),
                AboutAcknowledgements.Contributors,
                true
            )
        );
        PageBody.Children.Add(
            AcknowledgementCards(
                Ui("Open-source projects and services"),
                AboutAcknowledgements.Projects,
                false
            )
        );
    }

    Border AcknowledgementCards(string title, IReadOnlyList<Acknowledgement> entries, bool compact)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Text(title, 18, true));
        var cards = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        var columns = compact ? 4 : 3;
        for (var i = 0; i < columns; i++)
            cards.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
        for (var i = 0; i < entries.Count; i++)
        {
            if (i % columns == 0)
                cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var entry = entries[i];
            var content = new Grid { ColumnSpacing = 8, MinHeight = compact ? 24 : 30 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            content.Children.Add(
                new Image
                {
                    Source = new BitmapImage(
                        new Uri(
                            Path.Combine(AppContext.BaseDirectory, "assets", "about", entry.Image)
                        )
                    ),
                    Width = compact ? 24 : 28,
                    Height = compact ? 24 : 28,
                }
            );
            var label = Text("", 14, true);
            label.Text = entry.Name;
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var link = new HyperlinkButton
            {
                Content = content,
                NavigateUri = new Uri(entry.Url),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
            };
            ToolTipService.SetToolTip(link, entry.Name);
            var card = CreateCard(link, new Thickness(8, 6, 8, 6), 5);
            Grid.SetRow(card, i / columns);
            Grid.SetColumn(card, i % columns);
            cards.Children.Add(card);
        }
        body.Children.Add(cards);
        return Card(body);
    }

    string Labeled(string label, string value) => Ui(label) + "  " + value;
}