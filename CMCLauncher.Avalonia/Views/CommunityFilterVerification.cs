using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyCommunityFilterParity(List<string> checks)
    {
        var catalogs = filterCatalogs.ToArray();
        var requests = verificationTransport.Requests;
        try
        {
            var catalog = new CommunityFilterCatalog(
                [],
                [
                    new("themed", "resourcepack", "categories"),
                    new("pbr", "resourcepack", "features"),
                    new("32x", "resourcepack", "resolutions"),
                ],
                [],
                []
            );
            filterCatalogs[("modrinth", "resourcepack")] = catalog;
            filterCatalogs[("curseforge", "resourcepack")] = new([], [], [], []);
            await Navigate("resourcepack");
            var source = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(c => Equals(c.Tag, "community-source"));
            source.SelectedItem = "curseforge";
            source.SelectedItem = "modrinth";
            var filters = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Where(c => c.Tag?.ToString()?.StartsWith("community-filter-category-") == true)
                .ToArray();
            if (
                filters.Length != 3
                || filters.Select(c => c.Parent?.Parent).Distinct().Count() != 1
            )
                throw new IOException(
                    "Resource pack category, feature and resolution filters must share one row."
                );
            filterCatalogs[("modrinth", "shader")] = new(
                [],
                [
                    new("realistic", "shader", "categories"),
                    new("shadows", "shader", "features"),
                    new("high", "shader", "performance impact"),
                ],
                [new("iris", ["shader"]), new("optifine", ["shader"])],
                []
            );
            filterCatalogs[("curseforge", "shader")] = new([], [], [], []);
            await Navigate("shader");
            source = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(c => Equals(c.Tag, "community-source"));
            source.SelectedItem = "curseforge";
            source.SelectedItem = "modrinth";
            var loaders = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(c => Equals(c.Tag, "community-filter-loader"));
            if (
                !loaders.ItemsSource!.Cast<string>().Contains("iris")
                || !loaders.ItemsSource!.Cast<string>().Contains("optifine")
            )
                throw new IOException("Shader loader choices do not use the provider catalog.");
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => { },
                global::Avalonia.Threading.DispatcherPriority.Background
            );
            filters = pageContent
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .Where(c => c.Tag?.ToString()?.StartsWith("community-filter-category-") == true)
                .ToArray();
            var row = (Grid)filters[0].Parent!.Parent!;
            row.UpdateLayout();
            var heights = filters
                .Append(loaders)
                .Select(c => c.TranslatePoint(default, row)!.Value.Y)
                .ToArray();
            if (heights.Max() - heights.Min() > 1)
                throw new IOException(
                    "Long filter headings displaced their controls onto different rows."
                );
            checks.Add("PASS shader loader metadata and aligned single-line filter headings");
            if (verificationTransport.Requests != requests)
                throw new IOException("Cached filter changes issued unexpected requests.");
            checks.Add(
                "PASS provider category groups preserve resource-pack features and resolutions in one row"
            );
        }
        finally
        {
            filterCatalogs.Clear();
            foreach (var entry in catalogs)
                filterCatalogs.Add(entry.Key, entry.Value);
        }
    }
}
