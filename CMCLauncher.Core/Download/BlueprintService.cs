using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// 蓝图条目：Id、标题、描述、作者、来源网站、封面图、版本、下载量、适用版本与尺寸。
public sealed record Blueprint(
    string Id,
    string Title,
    string Description,
    string Author,
    string Website,
    string ImageUrl = "",
    int Version = 1,
    string Downloads = "",
    string Versions = "",
    string Size = ""
);

// 蓝图搜索结果页：条目列表、是否有下一页与总结果数。
public sealed record BlueprintPage(IReadOnlyList<Blueprint> Items, bool HasNext, int? Total);

// 蓝图（Schematic）下载服务：对接 Schematicraft 与 MCSchematic 两个来源，
// 提供搜索、详情查询与文件下载（含重定向跟随与文件名安全校验）。
public sealed class BlueprintService(HttpClient http)
{
    const string Api = "https://api.schematicraft.com";

    // 请求 JSON 接口并解析；响应超过 8MB 或为空时抛出异常。
    async Task<JsonNode> Json(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length > 8 * 1024 * 1024)
            throw new IOException("Blueprint response is too large.");
        return JsonNode.Parse(text) ?? throw new IOException("Empty blueprint response.");
    }

    // 按来源搜索蓝图：mcschematic 走其 REST API，schematicraft 走官方 API
    // （无关键词时列出公开蓝图，有关键词时走搜索接口）。
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
                        item.S("previewUrl").Length > 0
                        && Uri.TryCreate(
                            new Uri("https://mcschematic.top"),
                            item.S("previewUrl"),
                            out var preview
                        )
                            ? preview.AbsoluteUri
                            : "",
                        item["currentVersion"].Int("number"),
                        Downloads: item["engagement"]?["downloadCount"]?.ToString() ?? "",
                        Versions: item["sourceGameVersion"].S("release")
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
                    item.S("thumbnailUrl"),
                    Downloads: item["downloads"]?.ToString() ?? ""
                ))
                .ToArray(),
            data is JsonArray ? items.Length == 16 : data.S("hasMore") == "true",
            data is JsonArray ? null : data.Int("total")
        );
    }

    // 查询蓝图详情：mcschematic 额外请求指定版本信息以获取原始文件名与格式，
    // schematicraft 则从详情接口提取文件名与格式。
    public async Task<CmsDetail> Details(Blueprint entry, string source, CancellationToken ct)
    {
        if (source == "mcschematic")
        {
            var data = await Json(
                "https://mcschematic.top/api/v1/blueprints/" + Uri.EscapeDataString(entry.Id),
                ct
            );
            var version = await Json(
                $"https://mcschematic.top/api/v1/blueprints/{Uri.EscapeDataString(entry.Id)}/versions/{entry.Version}",
                ct
            );
            var updated = entry with { Description = data.S("description", entry.Description) };
            return new(
                updated,
                new[]
                {
                    new CmsFile(
                        entry.Id,
                        version.S("originalFilename", "Original file"),
                        version.S("sourceFormat")
                    ),
                },
                data.S("contentMarkdown")
            );
        }
        if (source != "schematicraft")
            throw new ArgumentException("Unknown blueprint source.");
        var detail = await Json(Api + "/schematics/" + Uri.EscapeDataString(entry.Id), ct);
        var name = detail.S(
            "originalFilename",
            detail.S("filename", detail.S("fileName", "Original file"))
        );
        return new(
            entry with
            {
                Description = detail.S("description", entry.Description),
            },
            new[] { new CmsFile(entry.Id, name, detail.S("format")) },
            ""
        );
    }

    // 下载蓝图文件到暂存目录并返回本地路径：
    // schematicraft 需先 POST 初始化获取下载 token；
    // 下载过程最多跟随 6 次重定向，并校验 URL 协议、文件名与扩展名安全性。
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
