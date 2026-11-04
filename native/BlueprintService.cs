using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public sealed record Blueprint(
    string Id,
    string Title,
    string Description,
    string Author,
    string Website,
    string ImageUrl = "",
    int Version = 1
);

public sealed record BlueprintPage(IReadOnlyList<Blueprint> Items, bool HasNext, int? Total);

public sealed class BlueprintService(HttpClient http)
{
    const string Api = "https://api.schematicraft.com";

    async Task<JsonNode> Json(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length > 8 * 1024 * 1024)
            throw new IOException("Blueprint response is too large.");
        return JsonNode.Parse(text) ?? throw new IOException("Empty blueprint response.");
    }

    public async Task<BlueprintPage> Search(
        string query,
        int page,
        CancellationToken ct,
        string source = "schematicraft"
    )
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (source == "mcschematic")
        {
            if (query.Length > 120)
                throw new ArgumentException("Search text must not exceed 120 characters.");
            var result = await Json(
                $"https://mcschematic.top/api/v1/blueprints?page={page}&limit=16&extra=none&q={Uri.EscapeDataString(query)}",
                ct
            );
            return new(
                result["items"]
                    .Items()
                    .Select(item => new Blueprint(
                        item.S("id"),
                        item.S("title"),
                        item.S("description"),
                        item["author"].S("displayName"),
                        "https://mcschematic.top/blueprints/" + Uri.EscapeDataString(item.S("id")),
                        Uri.TryCreate(
                            new Uri("https://mcschematic.top"),
                            item.S("previewUrl"),
                            out var preview
                        )
                            ? preview.AbsoluteUri
                            : "",
                        item["currentVersion"].Int("number")
                    ))
                    .ToArray(),
                page < result.Int("totalPages"),
                result.Int("totalCount")
            );
        }
        if (source != "schematicraft")
            throw new ArgumentException("Unknown blueprint source.");
        var url =
            query.Length == 0
                ? $"{Api}/schematics?publicOnly=true&limit=16&page={page}"
                : $"{Api}/search?query={Uri.EscapeDataString(query)}&limit=16&page={page}";
        var data = await Json(url, ct);
        var items = (data is JsonArray ? data : data["results"]).Items().ToArray();
        return new(
            items
                .Select(item => new Blueprint(
                    item.S("id"),
                    item.S("title"),
                    item.S("description"),
                    item["owner"].S("username"),
                    "https://schematicraft.com/schematic/" + Uri.EscapeDataString(item.S("id")),
                    item.S("thumbnailUrl")
                ))
                .ToArray(),
            data is JsonArray ? items.Length == 16 : data.S("hasMore") == "true",
            data is JsonArray ? null : data.Int("total")
        );
    }

    public async Task<string> Download(
        string id,
        string stage,
        CancellationToken ct,
        string source = "schematicraft",
        int version = 1
    )
    {
        string url;
        if (source == "mcschematic")
        {
            if (!Guid.TryParse(id, out _) || version < 1)
                throw new ArgumentException("Invalid blueprint version.");
            url =
                $"https://mcschematic.top/api/v1/blueprints/{Uri.EscapeDataString(id)}/versions/{version}/download";
        }
        else if (source == "schematicraft")
        {
            using var init = new HttpRequestMessage(
                HttpMethod.Post,
                Api + "/download/original/init"
            )
            {
                Content = new StringContent(
                    new JsonObject { ["schematicId"] = id }.ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            using var response = await http.SendAsync(init, ct);
            response.EnsureSuccessStatusCode();
            var token = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)).S("token");
            if (token.Length is < 1 or > 256)
                throw new IOException("Invalid download response.");
            url = Api + "/download/original/" + Uri.EscapeDataString(token);
        }
        else
            throw new ArgumentException("Unknown blueprint source.");
        HttpResponseMessage? file = null;
        try
        {
            for (var redirect = 0; redirect < 6; redirect++)
            {
                var uri = new Uri(url);
                if (uri.Scheme != "https" || uri.UserInfo.Length > 0)
                    throw new IOException("Unsafe blueprint download URL.");
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                file = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (
                    (int)file.StatusCode is >= 300 and < 400
                    && file.Headers.Location is { } location
                )
                {
                    url = new Uri(uri, location).AbsoluteUri;
                    file.Dispose();
                    file = null;
                    continue;
                }
                break;
            }
            if (file == null)
                throw new IOException("Too many blueprint redirects.");
            file.EnsureSuccessStatusCode();
            var name = (
                file.Content.Headers.ContentDisposition?.FileNameStar ?? file.Content
                    .Headers
                    .ContentDisposition
                    ?.FileName
            )?.Trim('"');
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileName(new Uri(url).LocalPath);
            if (
                Path.GetFileName(name) != name
                || !SchematicManager.Extensions.Contains(
                    Path.GetExtension(name),
                    StringComparer.OrdinalIgnoreCase
                )
            )
                throw new IOException("The source did not return a supported blueprint file.");
            if (
                file.Content.Headers.ContentType?.MediaType?.Contains(
                    "html",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            )
                throw new IOException("The source returned a web page instead of a blueprint.");
            Directory.CreateDirectory(stage);
            var target = Store.SafePath(stage, name);
            await using var input = await file.Content.ReadAsStreamAsync(ct);
            await using var output = System.IO.File.Create(target);
            var buffer = new byte[65536];
            int read;
            long total = 0;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > 100 * 1024 * 1024)
                    throw new IOException("Blueprint exceeds 100 MB.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (total == 0)
                throw new IOException("The blueprint file is empty.");
            return target;
        }
        finally
        {
            file?.Dispose();
        }
    }
}
