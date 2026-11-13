using Avalonia.Controls;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control InstanceArtwork(Instance instance, double size = 48)
    {
        if (!string.IsNullOrWhiteSpace(instance.IconPath) && File.Exists(instance.IconPath))
        {
            try
            {
                using var file = File.OpenRead(instance.IconPath);
                var bitmap = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(file, 96);
                var image = new Image
                {
                    Source = bitmap,
                    Width = size,
                    Height = size,
                    Stretch = global::Avalonia.Media.Stretch.Uniform,
                };
                image.DetachedFromVisualTree += (_, _) => bitmap.Dispose();
                return image;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                session.Log("DEBUG", ex.Message);
            }
        }
        var icon = NavigationIcon(LauncherNavigation.BoxIcon);
        icon.Width = icon.Height = size;
        return icon;
    }

    long instanceViewRevision;

    async Task ShowInstancePage(Instance instance, string section, Control content)
    {
        var revision = ++instanceViewRevision;
        var navigationGeneration = generation;
        var tabs = Row();
        tabs.Name = "InstanceTabs";
        var entries = new (string Kind, string Title, Func<Task> Open)[]
        {
            ("overview", "常规", InstanceSettings),
            ("worlds", "存档", () => WorldsPage(instance)),
            ("mod", "Mods", () => ContentPage(instance, "mod")),
            ("resourcepack", "资源包", () => ContentPage(instance, "resourcepack")),
            ("datapack", "数据包", () => ContentPage(instance, "datapack")),
            ("shader", "光影包", () => ContentPage(instance, "shader")),
        };
        foreach (var entry in entries)
        {
            var button = Btn(entry.Title, entry.Open, entry.Kind == section);
            button.Tag = "instance-tab:" + entry.Kind;
            tabs.Children.Add(button);
        }
        // JAR inspection stays off the UI thread. Stale scans cannot replace a newer page.
        var supported = await Task.Run(() =>
            SchematicManager.EnsureDirectory(session.Store, instance) != null
        );
        if (revision != instanceViewRevision || navigationGeneration != generation)
            return;
        if (supported)
        {
            var schematics = Btn(
                "投影管理",
                () => SchematicsPage(instance),
                section == "schematics"
            );
            schematics.Tag = "instance-tab:schematics";
            tabs.Children.Add(schematics);
        }
        if (content is StackPanel stack && stack.Children.FirstOrDefault() is WrapPanel toolbar)
            foreach (
                var back in toolbar
                    .Children.OfType<Button>()
                    .Where(b => b.Content?.ToString() == Ui("Back"))
                    .ToArray()
            )
                toolbar.Children.Remove(back);
        pageTitle.Text = Ui("版本设置") + " · " + instance.Name;
        pageContent.Content = Stack(tabs, content);
        route = "instance-settings";
        expandedNavigation = "home";
        SyncNavigation();
    }
}
