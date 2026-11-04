using System.Net;
using System.Text;
using CMCLauncher;

static class BlueprintTests
{
    sealed class Handler : HttpMessageHandler
    {
        public bool Html;
        public readonly List<Uri> Calls = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls.Add(request.RequestUri!);
            if (request.Headers.Contains("X-API-Key"))
                throw new Exception("Blueprint requests must not carry API keys");
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path == "/api/v1/blueprints")
                json =
                    "{\"items\":[{\"id\":\"01a1a60e-1414-78f2-90de-13b948d9e9fd\",\"title\":\"Castle\",\"author\":{\"displayName\":\"Creator\"},\"previewUrl\":\"/preview.png\",\"currentVersion\":{\"number\":3}}],\"totalCount\":40,\"totalPages\":3}";
            else if (path == "/search")
                json =
                    "{\"results\":[{\"id\":\"abc\",\"title\":\"House\",\"thumbnailUrl\":\"https://images.example/house.png\",\"owner\":{\"username\":\"Builder\"}}],\"hasMore\":true,\"total\":20}";
            else if (path == "/schematics/abc")
                json =
                    "{\"description\":\"Full Schematicraft description\",\"originalFilename\":\"house.litematic\",\"format\":\"Litematica\"}";
            else if (path.EndsWith("/versions/3"))
                json = "{\"originalFilename\":\"castle.schem\",\"sourceFormat\":\"world_edit\"}";
            else if (path.StartsWith("/api/v1/blueprints/") && !path.EndsWith("/download"))
                json =
                    "{\"description\":\"Full MCS description\",\"contentMarkdown\":\"# About the build\"}";
            else if (path == "/schematics")
                json = "[]";
            else if (path.EndsWith("/init"))
            {
                if (
                    request.Method != HttpMethod.Post
                    || !(await request.Content!.ReadAsStringAsync(ct)).Contains("abc")
                )
                    throw new Exception("Invalid download initialization");
                json = "{\"token\":\"fixture-token\"}";
            }
            else
            {
                var file = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([31, 139, 8, 0]),
                };
                file.Content.Headers.ContentDisposition = new("attachment")
                {
                    FileName = "house.nbt",
                };
                file.Content.Headers.ContentType = new(
                    Html ? "text/html" : "application/octet-stream"
                );
                return file;
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    public static async Task Run(string root)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var service = new BlueprintService(http);
        var page = await service.Search("house & farm", 2, default);
        if (
            page.Items.Single().Author != "Builder"
            || page.Items.Single().ImageUrl != "https://images.example/house.png"
            || !page.HasNext
            || page.Total != 20
            || !handler.Calls.Last().Query.Contains("page=2")
        )
            throw new Exception("Blueprint search lost pagination or attribution");
        if ((await service.Search("", 1, default)).Items.Count != 0)
            throw new Exception("Empty blueprint page failed");
        var path = await service.Download("abc", root, default);
        if (!File.Exists(path) || new FileInfo(path).Length != 4)
            throw new Exception("Blueprint download failed");
        handler.Html = true;
        try
        {
            await service.Download("abc", root, default);
            throw new Exception("HTML accepted as blueprint");
        }
        catch (IOException) { }
        if (new FileInfo(path).Length != 4)
            throw new Exception("Rejected response modified downloaded file");
        var details = await service.Details(page.Items.Single(), "schematicraft", default);
        if (
            details.Entry.Description != "Full Schematicraft description"
            || details.Files.Single().Name != "house.litematic"
        )
            throw new Exception("Schematicraft details lost description or original format");
        handler.Html = false;
        var mcs = await service.Search("castle", 2, default, "mcschematic");
        if (
            mcs.Items.Single().Version != 3
            || mcs.Items.Single().Author != "Creator"
            || mcs.Total != 40
            || !mcs.HasNext
            || mcs.Items.Single().ImageUrl != "https://mcschematic.top/preview.png"
        )
            throw new Exception("MCS pagination, version, attribution or preview mapping failed");
        var mcsDetails = await service.Details(mcs.Items.Single(), "mcschematic", default);
        if (
            mcsDetails.Files.Single().Name != "castle.schem"
            || mcsDetails.Note != "# About the build"
        )
            throw new Exception("MCS details lost version metadata or introduction");
        await service.Download(mcs.Items.Single().Id, root, default, "mcschematic", 3);
        if (!handler.Calls.Last().AbsolutePath.EndsWith("/versions/3/download"))
            throw new Exception("MCS download used wrong blueprint version");
        if (
            handler.Calls.Any(uri =>
                uri.Host != "api.schematicraft.com" && uri.Host != "mcschematic.top"
            )
        )
            throw new Exception("Unexpected blueprint provider request");
    }
}
