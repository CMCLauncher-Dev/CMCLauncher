using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    bool HomeButtonAvailable(Button button)
    {
        if (button.Tag is not string tag)
            return true;
        if (tag.StartsWith("home-add:", StringComparison.Ordinal))
            return !store.Config.Settings.HomeCards.Any(c => c.Id == tag[9..]);
        if (
            !tag.StartsWith("home-account:", StringComparison.Ordinal)
            || !int.TryParse(tag[13..], out var index)
        )
            return true;
        var account = store.Config.Accounts.FirstOrDefault(a =>
            a.Key == store.Config.SelectedAccount
            && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a)
        );
        return index switch
        {
            0 or 1 or 2 => account?.Type == "microsoft",
            3 => account?.Type is "microsoft" or "yggdrasil",
            _ => true,
        };
    }

    Grid HomeAccountActions(Account? account)
    {
        var grid = new Grid
        {
            ColumnSpacing = 6,
            RowSpacing = 6,
            Tag = "home-account-actions",
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var i = 0; i < 6; i++)
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Open(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        var operations = new (string Name, string IconPath, Func<Task> Action, bool Enabled)[]
        {
            (
                "Change skin",
                "M7,2 L4,3 L1,7 L4,10 L6,8 L6,22 L18,22 L18,8 L20,10 L23,7 L20,3 L17,2 C17,8 7,8 7,2 Z",
                () =>
                {
                    Open("https://www.minecraft.net/en-us/msaprofile/mygames/editskin");
                    return Task.CompletedTask;
                },
                account?.Type == "microsoft"
            ),
            (
                "Change cape",
                "M12,2 L21,6 L21,13 C21,18 16,21 12,23 C8,21 3,18 3,13 L3,6 Z",
                () =>
                {
                    Open("https://www.minecraft.net/en-us/msaprofile/mygames/editskin");
                    return Task.CompletedTask;
                },
                account?.Type == "microsoft"
            ),
            (
                "Change player name",
                "M4,17 L17,4 L20,4 L23,7 L23,10 L10,23 L3,24 Z M16,5 L22,11 M4,17 L10,23",
                () =>
                {
                    Open("https://www.minecraft.net/en-us/msaprofile/mygames/editprofile");
                    return Task.CompletedTask;
                },
                account?.Type == "microsoft"
            ),
            (
                "Refresh account",
                "M20,8 A9,9 0 0 0 4,6 M4,6 L4,2 M4,6 L8,6 M4,16 A9,9 0 0 0 20,18 M20,18 L20,22 M20,18 L16,18",
                async () =>
                {
                    if (account == null)
                        return;
                    await Run(
                        async ct =>
                        {
                            Account refreshed;
                            if (account.Type == "microsoft")
                                refreshed = await auth.Refresh(
                                    account,
                                    MicrosoftAuth.ConfiguredClientId,
                                    ct,
                                    force: true
                                );
                            else
                            {
                                using var external = new ExternalAuth(store.Config.Settings);
                                refreshed = await external.Refresh(
                                    store.Config.Accounts,
                                    account,
                                    ct
                                );
                            }
                            await SaveAccount(refreshed);
                        },
                        "Refresh account"
                    );
                    Render();
                },
                account?.Type is "microsoft" or "yggdrasil"
            ),
            (
                "Add account",
                "M10,2 A4,4 0 1 1 9.99,2 M2,22 L2,20 C2,14 16,14 16,20 M21,11 L21,19 M17,15 L25,15",
                () =>
                {
                    page = "accounts";
                    expandedNavigation = "settings";
                    Render();
                    return Task.CompletedTask;
                },
                true
            ),
            (
                "Redeem code",
                "M3,10 L21,10 L21,23 L3,23 Z M1,6 L23,6 L23,10 L1,10 Z M12,6 L12,23 M12,6 C3,6 4,0 8,1 C11,1 12,6 12,6 C12,0 18,0 19,3 C20,6 15,6 12,6",
                () =>
                {
                    Open("https://www.minecraft.net/en-us/redeem");
                    return Task.CompletedTask;
                },
                true
            ),
        };
        for (var i = 0; i < operations.Length; i++)
        {
            var entry = operations[i];
            var button = Action(entry.Name, async (_, _) => await entry.Action());
            button.Tag = "home-account:" + i;
            var icon = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = (Microsoft.UI.Xaml.Media.Geometry)
                    Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                        typeof(Microsoft.UI.Xaml.Media.Geometry),
                        entry.IconPath
                    ),
                StrokeThickness = 1.6,
                Width = 18,
                Height = 18,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            };
            icon.SetBinding(
                Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty,
                new Microsoft.UI.Xaml.Data.Binding
                {
                    Source = button,
                    Path = new PropertyPath("Foreground"),
                }
            );
            button.Content = icon;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.MinWidth = 0;
            button.Height = 34;
            button.Padding = new Thickness(4, 5, 4, 5);
            button.CornerRadius = new CornerRadius(8);
            button.IsEnabled = entry.Enabled && task == null;
            ToolTipService.SetToolTip(button, Ui(entry.Name));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Ui(entry.Name));
            Grid.SetColumn(button, i);
            Grid.SetRow(button, 0);
            grid.Children.Add(button);
        }
        return grid;
    }
}
