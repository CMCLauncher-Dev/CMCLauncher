using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    readonly VerificationHandler verificationTransport = new();

    // The verification window uses an isolated store and rejects every unplanned network request.
    async Task VerifyUi()
    {
        Directory.CreateDirectory(VerificationDirectory!);
        var checks = new List<string>();
        try
        {
            gameCatalog = [new("1.21.1", "release")];
            session.Store.Config.Settings.LauncherLanguage = "en_us";
            BuildNavigation();
            var instance = new Instance
            {
                Id = "ui-fixture",
                Name = "UI fixture",
                GameVersion = "1.21.1",
                Loader = "neoforge",
                Directory = Path.Combine(VerificationDirectory!, "fixture"),
            };
            Directory.CreateDirectory(instance.Directory);
            Directory.CreateDirectory(Path.Combine(instance.Directory, "schematics"));
            Directory.CreateDirectory(Path.Combine(instance.Directory, "mods"));
            using (
                var zip = System.IO.Compression.ZipFile.Open(
                    Path.Combine(instance.Directory, "mods", "fixture.jar"),
                    System.IO.Compression.ZipArchiveMode.Create
                )
            )
            {
                using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                    writer.Write(
                        "{\"schemaVersion\":1,\"id\":\"ui_fixture\",\"name\":\"Fixture mod\",\"version\":\"1.0\",\"icon\":\"icon.png\"}"
                    );
                var logo = Directory
                    .EnumerateFiles(
                        Path.Combine(AppContext.BaseDirectory, "assets", "about"),
                        "*.png"
                    )
                    .First();
                using var input = File.OpenRead(logo);
                using var output = zip.CreateEntry("icon.png").Open();
                input.CopyTo(output);
            }
            Directory.CreateDirectory(Path.Combine(instance.Directory, "saves", "Fixture"));
            await File.WriteAllBytesAsync(
                Path.Combine(instance.Directory, "saves", "Fixture", "level.dat"),
                [1, 2, 3]
            );
            session.Store.Config.Instances.Clear();
            session.Store.Config.Instances.Add(instance);
            session.Selected = instance;
            foreach (
                var page in new[]
                {
                    "home",
                    "instances",
                    "downloads",
                    "mod",
                    "modpack",
                    "resourcepack",
                    "shader",
                    "datapack",
                    "world",
                    "blueprints",
                    "settings",
                    "appearance",
                    "java",
                    "accounts",
                    "logs",
                    "toolbox",
                    "terracotta",
                    "frpc",
                    "pfp",
                    "stats",
                    "tasks",
                    "about",
                }
            )
            {
                var errors = session.Logs.Count(line => line.Contains("ERROR"));
                await Navigate(page);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (
                    pageContent.Content is not Control
                    || session.Logs.Count(line => line.Contains("ERROR")) != errors
                )
                    throw new IOException("Page failed: " + page);
                checks.Add("PASS " + page);
            }
            var savedLayout = session.Store.Config.Settings.InstanceView;
            session.Store.Config.Settings.InstanceView = "tile";
            await Navigate("instances");
            var instanceLayout = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(combo => combo.Name == "InstanceLayout");
            if (instanceLayout.SelectedIndex != 1)
                throw new IOException("WinUI tile layout was not restored by Avalonia.");
            instanceLayout.SelectedIndex = 0;
            await Task.Delay(50);
            instanceLayout.SelectedIndex = 1;
            await Task.Delay(100);
            if (new Store(session.Store.Root).Config.Settings.InstanceView != "tile")
                throw new IOException(
                    "Avalonia saved a different instance layout value from WinUI."
                );
            session.Store.Config.Settings.InstanceView = savedLayout;
            checks.Add("PASS instance layout reads and writes the shared WinUI tile setting");
            await VerifyGameDownloadSteps(checks);
            await VerifyHomeDashboard(checks);
            await VerifyAccountAvatar(checks);
            await VerifyAccountSelection(checks);
            await VerifyNavigationLocales(checks);
            VerifyLiveTheme(checks);
            await Navigate("mod");
            foreach (var input in pageContent.GetVisualDescendants().OfType<TextBox>())
                input.Text = "typing must not search";
            await Task.Delay(150);
            if (verificationTransport.Requests != 0)
                throw new IOException("Navigation or input unexpectedly issued a request.");
            checks.Add("PASS input and navigation do not search");
            if (session.Terracotta.Installed != null || session.Terracotta.Connected)
                throw new IOException(
                    "Toolbox navigation unexpectedly installed or started Terracotta."
                );
            checks.Add("PASS toolbox does not install or start Terracotta");
            var search = pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == "Search");
            search.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            search.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            await Task.Delay(250);
            if (verificationTransport.Requests != 1)
                throw new IOException("Repeated search clicks were not coalesced.");
            checks.Add("PASS explicit search and duplicate-click suppression");
            await CommunityDetails(
                new SearchResult("ui-project", "Detail fixture", "Fixture", "mod", ""),
                "mod"
            );
            var showDependencies = pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Content?.ToString() == Ui("Show dependencies"));
            showDependencies.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            for (
                var i = 0;
                i < 40
                    && !pageContent
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Any(text => text.Text == Ui("No dependencies declared."));
                i++
            )
                await Task.Delay(25);
            if (
                !pageContent
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Any(text => text.Text == Ui("No dependencies declared."))
            )
                throw new IOException(
                    "Dependencies were not expanded inline on the resource detail page."
                );
            checks.Add("PASS community details expand dependency declarations inline");
            await Navigate("mod");
            var operation = new ViewModels.OperationState("Fixture task");
            if (operation.HasReceived)
                throw new IOException("Local task displayed network bytes.");
            operation.AddReceived(4096);
            operation.Progress = 42;
            if (!operation.HasReceived || operation.Percent != 42)
                throw new IOException("Task progress did not update.");
            operation.Finish();
            if (operation.IsRunning || operation.IsIndeterminate)
                throw new IOException("Completed task remained active.");
            session.Operations.Insert(0, operation);
            checks.Add("PASS task progress and terminal state");
            await InstanceSettings();
            if (
                pageContent
                    .GetVisualDescendants()
                    .OfType<WrapPanel>()
                    .Single(p => p.Name == "InstanceTabs")
                    .Children.Count != 7
            )
                throw new IOException(
                    "Instance settings omit the shared tabs or compatible schematics."
                );
            checks.Add("PASS instance settings and conditional schematic tab");
            await ContentPage(instance, "mod");
            for (
                var i = 0;
                i < 40
                    && !pageContent
                        .GetVisualDescendants()
                        .OfType<Image>()
                        .Any(image => image.Source != null);
                i++
            )
                await Task.Delay(25);
            if (
                !pageContent
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Any(text => text.Text == "Fixture mod")
                || !pageContent
                    .GetVisualDescendants()
                    .OfType<Image>()
                    .Any(image => image.Source != null)
            )
                throw new IOException(
                    "Visible installed mods did not load shared metadata and icons."
                );
            var modSearch = pageContent
                .GetVisualDescendants()
                .OfType<TextBox>()
                .Single(input => input.Name == "InstalledModSearch");
            modSearch.Text = "does-not-match";
            var modFind = pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Name == "InstalledModSearchButton");
            modFind.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            await Task.Delay(150);
            if (pageContent.GetVisualDescendants().OfType<ListBox>().Single().ItemCount != 0)
                throw new IOException("Installed-mod search failed to filter by metadata.");
            modSearch.Text = "Fixture mod";
            modFind.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            await Task.Delay(150);
            if (pageContent.GetVisualDescendants().OfType<ListBox>().Single().ItemCount != 1)
                throw new IOException("Installed-mod search only matched filenames.");
            installedModQuery = "";
            checks.Add("PASS virtualized mod management, cached icons and metadata search");
            await WorldsPage(instance);
            checks.Add("PASS world management");
            await WorldBackupsPage(instance);
            checks.Add("PASS world backups");
            await SchematicsPage(instance);
            checks.Add("PASS schematic tree");
            await ExportPackPage(instance);
            checks.Add("PASS modpack export");
            foreach (var theme in new[] { "light", "dark" })
            {
                session.Store.Config.Settings.Theme = theme;
                ApplyTheme();
                await Navigate("settings");
                await Task.Delay(120);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
                bitmap.Render(this);
                bitmap.Save(Path.Combine(VerificationDirectory!, theme + ".png"));
                checks.Add("PASS " + theme + " rendering");
            }
            await File.WriteAllLinesAsync(
                Path.Combine(VerificationDirectory!, "passed.txt"),
                checks
            );
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(
                Path.Combine(VerificationDirectory!, "failure.txt"),
                ex.ToString()
            );
            Environment.ExitCode = 1;
        }
        Close();
    }

    void VerifyLiveTheme(List<string> checks)
    {
        var settings = session.Store.Config.Settings;
        var preset = settings.ColorPreset;
        var mode = settings.Theme;
        var light = settings.CustomLightColors;
        var dark = settings.CustomDarkColors;
        var card = Card(T("Theme fixture"));
        var background = card.Background;
        var icon = NavigationIcon(LauncherNavigation.BoxIcon);
        var stroke = icon.Stroke;
        try
        {
            settings.ColorPreset = "custom";
            settings.CustomLightColors = new()
            {
                ["surface"] = "#F0FFF1",
                ["border"] = "#557755",
                ["accent"] = "#228833",
                ["text"] = "#112211",
            };
            settings.CustomDarkColors = new()
            {
                ["surface"] = "#112211",
                ["border"] = "#668866",
                ["accent"] = "#77CC88",
                ["text"] = "#DDF6DD",
            };
            settings.Theme = "light";
            ApplyTheme();
            if (
                ((SolidColorBrush)card.Background!).Color != Color.Parse("#F0FFF1")
                || ((SolidColorBrush)Accent).Color != Color.Parse("#228833")
            )
                throw new IOException("Light custom theme did not reach existing controls.");
            settings.Theme = "dark";
            ApplyTheme();
            if (
                !ReferenceEquals(background, card.Background)
                || ((SolidColorBrush)card.Background!).Color != Color.Parse("#112211")
                || !ReferenceEquals(stroke, icon.Stroke)
                || ((SolidColorBrush)icon.Stroke!).Color != Color.Parse("#DDF6DD")
            )
                throw new IOException("Theme changes required rebuilding cards or icons.");
            checks.Add(
                "PASS existing cards, borders, accents and icons update without page reconstruction"
            );
        }
        finally
        {
            settings.ColorPreset = preset;
            settings.Theme = mode;
            settings.CustomLightColors = light;
            settings.CustomDarkColors = dark;
            ApplyTheme();
        }
    }

    async Task VerifyAccountSelection(List<string> checks)
    {
        var saved = session.Store.Config.Accounts.ToArray();
        var selected = session.Account;
        var unverified = new Account
        {
            Type = "microsoft",
            Name = "Unverified fixture",
            Uuid = Guid.NewGuid().ToString("N"),
        };
        try
        {
            session.Store.Config.Accounts.Clear();
            session.Store.Config.Accounts.Add(unverified);
            await Navigate("accounts");
            var use = pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "use-account:" + unverified.Key);
            if (use.IsEnabled)
                throw new IOException("An unverified Microsoft identity was selectable.");
            session.Account = unverified;
            await session.RemoveAccount(unverified);
            if (session.Account != null)
                throw new IOException("Removed identity remained selected.");
            checks.Add("PASS account selection and removal retain the shared ownership gate");
        }
        finally
        {
            session.Store.Config.Accounts.Clear();
            session.Store.Config.Accounts.AddRange(saved);
            session.Account = selected;
            await session.Save();
        }
        await Navigate("home");
    }

    async Task VerifyAccountAvatar(List<string> checks)
    {
        var id = Guid.NewGuid().ToString("N");
        var cache = Path.Combine(session.Store.Root, "skin-cache");
        Directory.CreateDirectory(cache);
        using (
            var skin = new WriteableBitmap(
                new PixelSize(64, 64),
                new Vector(96, 96),
                global::Avalonia.Platform.PixelFormats.Bgra8888,
                global::Avalonia.Platform.AlphaFormat.Unpremul
            )
        )
        {
            using (var frame = skin.Lock())
            {
                var pixels = Enumerable
                    .Repeat(new byte[] { 0, 0, 255, 255 }, 64 * 64)
                    .SelectMany(p => p)
                    .ToArray();
                for (var y = 0; y < 64; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        pixels,
                        y * 64 * 4,
                        IntPtr.Add(frame.Address, y * frame.RowBytes),
                        64 * 4
                    );
            }
            skin.Save(Path.Combine(cache, id + ".png"));
        }
        var avatar = AccountAvatar(
            new Account
            {
                Type = "microsoft",
                Uuid = id,
                Name = "Avatar fixture",
            },
            32
        );
        pageContent.Content = Stack(avatar);
        for (
            var i = 0;
            i < 40
                && !avatar
                    .GetVisualDescendants()
                    .OfType<Image>()
                    .Any(image => image.Source != null);
            i++
        )
            await Task.Delay(25);
        if (!avatar.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null))
            throw new IOException("Cached Minecraft account avatar was not rendered.");
        checks.Add(
            "PASS account avatar renders cached skin without creating a task or network request"
        );
        await Navigate("home");
    }

    async Task VerifyGameDownloadSteps(List<string> checks)
    {
        var saved = gameCatalog;
        var category = gameReleaseCategory;
        var query = gameVersionQuery;
        try
        {
            gameCatalog = Enumerable
                .Range(0, 151)
                .Select(i => new GameRelease("1.22." + i, "release"))
                .Concat(
                    new[]
                    {
                        new GameRelease("25w01a", "snapshot"),
                        new GameRelease("25w14craftmine", "snapshot"),
                    }
                )
                .ToArray();
            gameReleaseCategory = "正式版";
            gameVersionQuery = "";
            await Navigate("downloads");
            var list = pageContent
                .GetVisualDescendants()
                .OfType<ListBox>()
                .Single(l => l.Name == "GameVersionList");
            if (
                list.ItemCount != 151
                || pageContent.GetVisualDescendants().OfType<RadioButton>().Count() != 3
            )
                throw new IOException(
                    "Game downloads still truncate the catalog or lack release categories."
                );
            var snapshot = pageContent
                .GetVisualDescendants()
                .OfType<RadioButton>()
                .Single(r => r.Content?.ToString() == Ui("快照版"));
            snapshot.IsChecked = true;
            if (list.ItemCount != 1 || list.Items[0]?.ToString() != "25w01a")
                throw new IOException("April Fools releases leaked into the snapshot category.");
            list.SelectedIndex = 0;
            await Task.Delay(100);
            if (
                pageContent
                    .GetVisualDescendants()
                    .OfType<ListBox>()
                    .Any(l => l.Name == "GameVersionList")
                || !pageContent
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(b => b.Content?.ToString() == Ui("Install"))
            )
                throw new IOException("Selecting a version did not advance to install options.");
            checks.Add(
                "PASS uncapped virtual game catalog, separate release categories and two-step install flow"
            );
        }
        finally
        {
            gameCatalog = saved;
            gameReleaseCategory = category;
            gameVersionQuery = query;
        }
        await Navigate("home");
    }

    async Task VerifyHomeDashboard(List<string> checks)
    {
        var settings = session.Store.Config.Settings;
        var saved = settings
            .HomeCards.Select(p => new HomeCardPlacement
            {
                Id = p.Id,
                Width = p.Width,
                Height = p.Height,
            })
            .ToList();
        var menuScale = settings.UiMenuScale;
        var oldWidth = Width;
        try
        {
            homeCustomizeMode = true;
            settings.HomeCards =
            [
                new()
                {
                    Id = "account",
                    Width = 1,
                    Height = 2,
                },
                new() { Id = "game" },
            ];
            await Navigate("home");
            await Task.Delay(100);
            var actions = pageContent
                .GetVisualDescendants()
                .OfType<Grid>()
                .Single(g => g.Name == "HomeAccountActions");
            if (
                actions.Children.Count != 6
                || actions.ColumnDefinitions.Count != 6
                || actions.Children.OfType<Button>().Any(b => b.Height != 34)
            )
                throw new IOException(
                    "Home account shortcuts do not match the compact six-column layout."
                );
            await OpenHomeCardDrawer();
            await Task.Delay(100);
            var performance = ((Control)drawer.Pane!)
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "home-add:performance");
            performance.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            await Task.Delay(150);
            if (!settings.HomeCards.Any(p => p.Id == "performance") || drawer.IsPaneOpen)
                throw new IOException("The home card catalog did not add and persist a card.");
            settings.HomeCards.Add(new() { Id = "note", Width = 1 });
            settings.HomeCards.Add(new() { Id = "news" });
            await Navigate("home");
            await MoveHomeCard("news", "account", false);
            if (new Store(session.Store.Root).Config.Settings.HomeCards[0].Id != "news")
                throw new IOException("Home card reorder did not persist.");
            await Task.Delay(100);
            var dashboard = pageContent
                .GetVisualDescendants()
                .OfType<Grid>()
                .Single(g => g.Name == "HomeDashboard");
            if (
                pageContent
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(g => g.Name == "HomeCardTools")
                    .Any(g =>
                        g.Children.Count(c => Grid.GetColumn(c) == 3) != 1
                        || g.Children.Count(c => Grid.GetColumn(c) == 4) != 1
                    )
            )
                throw new IOException("Home card size fields overlap the drag handle.");
            if (
                dashboard.Children.Count != 5
                || !pageContent
                    .GetVisualDescendants()
                    .OfType<TextBox>()
                    .Any(t => t.Watermark?.ToString() == Ui("Write a note…"))
            )
                throw new IOException("Home optional cards were not migrated.");
            Width = 850;
            settings.UiMenuScale = 1.6;
            ApplyNavigationMetrics();
            await Task.Delay(150);
            if (dashboard.Children.Any(c => Grid.GetColumnSpan(c) != 2))
                throw new IOException("Home edit controls overflow at narrow widths.");
            checks.Add(
                "PASS home card catalog, six account actions, persisted order and responsive dashboard"
            );
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(VerificationDirectory!, "home-customize.png"));
        }
        finally
        {
            settings.HomeCards = saved;
            settings.UiMenuScale = menuScale;
            homeCustomizeMode = false;
            Width = oldWidth;
            ApplyNavigationMetrics();
            await session.Save();
        }
        await Navigate("home");
    }

    async Task VerifyNavigationLocales(List<string> checks)
    {
        BuildNavigation();
        if (
            navigationGroups.Count != 3
            || navigationParents.Count != 15
            || navigation.Children.OfType<Button>().Count() != 4
        )
            throw new IOException(
                "Sidebar must keep downloads, settings and more in nested groups."
            );
        await Navigate("mod");
        if (
            !navigationGroups["downloads"].IsExpanded
            || navigationGroups.Values.Count(g => g.IsExpanded) != 1
        )
            throw new IOException("Resource navigation did not expand its parent group.");
        navigationButtons["root:settings"]
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (IsLoaded && !navigationTweens.ContainsKey(navigationGroups["settings"].View))
            throw new IOException("Submenu expansion is missing its transition.");
        await Task.Delay(250);
        if (route != "mod" || navigationGroups["downloads"].IsExpanded)
            throw new IOException("Expanding a group navigated or left multiple groups open.");
        await Navigate("logs");
        if (navigationVisuals.Values.Any(v => v.Selected))
            throw new IOException("Runtime logs selected a sidebar page.");
        checks.Add("PASS nested menus, independent expansion and logs selection");
        await Navigate("mod");
        await Task.Delay(50);
        await Navigate("settings");
        await Task.Delay(50);
        await Navigate("about");
        await Task.Delay(260);
        if (
            !navigationGroups["more"].View.IsVisible
            || navigationGroups.Where(p => p.Key != "more").Any(p => p.Value.View.IsVisible)
            || navigationVisuals.Values.Count(v => v.Selected && v.Indicator.Opacity == 1) != 2
            || navigationVisuals.Values.Any(v => !v.Selected && v.Indicator.Opacity != 0)
        )
            throw new IOException(
                "Interrupted navigation left stale submenus or selection indicators."
            );
        checks.Add(
            "PASS rapid navigation settles root and child indicators without stale animations"
        );

        var pending = new ViewModels.OperationState("Badge fixture");
        session.Operations.Insert(0, pending);
        if (taskCountBadge?.IsVisible != true || taskCountText?.Text != "1")
            throw new IOException("Active task count is missing from the sidebar.");
        pending.Finish();
        if (taskCountBadge.IsVisible)
            throw new IOException("Completed task remained in the sidebar badge.");
        session.Operations.Remove(pending);
        checks.Add("PASS sidebar task badge follows active tasks and hides after completion");
        var originalMenuScale = session.Store.Config.Settings.UiMenuScale;
        session.Store.Config.Settings.UiMenuScale = 1.3;
        ApplyNavigationMetrics();
        if (
            scaling.Child is not Grid shell
            || Math.Abs(shell.ColumnDefinitions[0].Width.Value - 265.2) > .01
            || Math.Abs(navigationButtons["root:home"].Height - 41.6) > .01
        )
            throw new IOException("Sidebar width and menu row heights do not scale together.");
        session.Store.Config.Settings.UiMenuScale = originalMenuScale;
        ApplyNavigationMetrics();
        checks.Add("PASS menu scaling preserves WinUI sidebar and row proportions");
        await Navigate("settings");
        var headings = pageContent
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ToArray();
        if (
            new[]
            {
                "Language and interface",
                "Game launch",
                "Memory and Java",
                "Downloads and network",
                "Launcher updates",
                "Manage third-party applications",
            }.Any(title => !headings.Contains(Ui(title)))
            || pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Any(b => b.Content?.ToString() == Ui("Save"))
        )
            throw new IOException("Settings sections or immediate persistence differ from WinUI.");
        var automatic = pageContent
            .GetVisualDescendants()
            .OfType<global::Avalonia.Controls.Primitives.ToggleButton>()
            .Single(c => c.Content?.ToString() == Ui("Automatic memory allocation"));
        automatic.IsChecked = false;
        var allocation = pageContent.GetVisualDescendants().OfType<Slider>().Single();
        allocation.Value = Math.Min(4608, allocation.Maximum);
        var threadCount = pageContent.GetVisualDescendants().OfType<NumericUpDown>().Single();
        threadCount.Value = 24;
        await Task.Delay(150);
        var savedSettings = new Store(session.Store.Root).Config.Settings;
        if (
            savedSettings.AutoMemory
            || savedSettings.MemoryMb != (int)allocation.Value
            || savedSettings.DownloadThreads != 24
        )
            throw new IOException("Settings changes did not persist without a Save button.");
        automatic.IsChecked = true;
        await Task.Delay(100);
        checks.Add("PASS WinUI settings sections and immediate memory/download persistence");
        var language = pageContent
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(c => c.SelectedItem is InterfaceLanguage);
        language.SelectedItem = session.Locales.Languages.Single(l => l.Code == "zh_cn");
        for (var i = 0; i < 40 && pageTitle.Text != Ui("Settings"); i++)
            await Task.Delay(25);
        if (
            session.Store.Config.Settings.LauncherLanguage != "zh_cn"
            || navigationVisuals["root:downloads"].Label.Text != "下载"
            || navigationVisuals["root:home"].Label.Text != "主页"
            || !pageContent
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Any(t => t.Text == Ui("Launcher language"))
        )
            throw new IOException("Changing language did not rebuild the menus and settings.");
        checks.Add("PASS live Chinese localization of navigation and settings");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height)))
        {
            bitmap.Render(this);
            bitmap.Save(Path.Combine(VerificationDirectory!, "navigation-zh-cn.png"));
        }
        foreach (var pack in session.Locales.Languages)
        {
            session.Store.Config.Settings.LauncherLanguage = pack.Code;
            BuildNavigation();
            await Navigate("settings");
            if (
                navigationVisuals["root:more"].Label.Text != Ui("更多")
                || pageTitle.Text != Ui("Settings")
            )
                throw new IOException("Sidebar locale mismatch: " + pack.Code);
        }
        checks.Add("PASS shipped language packs share the same navigation and page titles");
        session.Store.Config.Settings.LauncherLanguage = "en_us";
        BuildNavigation();
    }

    sealed class VerificationHandler : HttpMessageHandler
    {
        public int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref Requests);
            await Task.Delay(100, cancellationToken);
            const string version =
                """{"id":"ui-version","project_id":"ui-project","name":"Fixture version","version_number":"1.0","version_type":"release","game_versions":["1.21.1"],"loaders":["neoforge"],"dependencies":[]}""";
            var payload = request.RequestUri?.AbsolutePath switch
            {
                "/v2/search" => """{"hits":[],"total_hits":0,"offset":0,"limit":16}""",
                "/v2/project/ui-project" =>
                    """{"title":"Detail fixture","description":"Fixture","body":"Fixture body","icon_url":"","categories":[]}""",
                "/v2/project/ui-project/version" => "[" + version + "]",
                "/v2/version/ui-version" => version,
                _ => null,
            };
            return payload == null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload),
                };
        }
    }
}
