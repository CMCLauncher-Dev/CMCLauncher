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
