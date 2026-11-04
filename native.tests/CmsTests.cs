using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CMCLauncher;

static class CmsTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static async Task Reject(Func<Task> operation, string message)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
            when (ex is IOException or HttpRequestException or OperationCanceledException)
        {
            return;
        }
        throw new Exception(message);
    }

    public static string Listing(int count = 20) =>
        "<div id='receive_msg'>40 个结果，2 页</div>"
        + string.Concat(
            Enumerable
                .Range(1, count)
                .Select(i =>
                    $"<a class='extra list_result' href='/detail/{i}/'><h2 class='title b'><span>Build {i}</span></h2><div class='author op5'>作者：Builder &amp; Co</div><div class='download'>下载量：{100 - i}</div><div class='version'><div class='t'>1.20.1</div><div class='t'>0.5.1</div></div><div class='size'>尺寸：5 × 6 × 7</div><img src='/upload/post/{i}.webp'><div class='desc'>Description</div></a>"
                )
        );

    static byte[] Nbt()
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, true))
        {
            using var writer = new BinaryWriter(gzip, Encoding.UTF8, true);
            void Name(string name)
            {
                var data = Encoding.UTF8.GetBytes(name);
                writer.Write((byte)(data.Length >> 8));
                writer.Write((byte)data.Length);
                writer.Write(data);
            }
            writer.Write((byte)10);
            Name("");
            writer.Write((byte)10);
            Name("size");
            foreach (var axis in new[] { "x", "y", "z" })
            {
                writer.Write((byte)3);
                Name(axis);
                writer.Write(new byte[] { 0, 0, 0, 1 });
            }
            writer.Write((byte)0);
            foreach (var name in new[] { "palette", "blocks" })
            {
                writer.Write((byte)9);
                Name(name);
                writer.Write((byte)10);
                writer.Write(new byte[4]);
            }
            writer.Write((byte)0);
        }
        return bytes.ToArray();
    }

    sealed class Handler : HttpMessageHandler
    {
        public readonly List<(string Path, long Start, string Body)> Calls = [];
        public bool OtherFormat;
        public bool Offline,
            CsrfOnce,
            AlwaysCsrf,
            Challenge,
            RateLimit,
            BadFile,
            ChallengeOk;
        public int Active,
            Maximum;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Active++;
            Maximum = Math.Max(Maximum, Active);
            try
            {
                var body =
                    request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                Calls.Add((request.RequestUri!.AbsolutePath, Stopwatch.GetTimestamp(), body));
                await Task.Delay(15, ct);
                if (Offline)
                    throw new HttpRequestException("fixture offline");
                if (RateLimit)
                {
                    var rate = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent(""),
                    };
                    rate.Headers.RetryAfter = new RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(5)
                    );
                    return rate;
                }
                var path = request.RequestUri.AbsolutePath;
                HttpResponseMessage Html(string text, HttpStatusCode status = HttpStatusCode.OK) =>
                    new(status) { Content = new StringContent(text, Encoding.UTF8, "text/html") };
                if (Challenge)
                    return Html(
                        "<form id='challenge-form'>Just a moment</form>",
                        ChallengeOk ? HttpStatusCode.OK : HttpStatusCode.Forbidden
                    );
                if (path == "/")
                    return Html("<input value='fixture-csrf' name='csrfmiddlewaretoken'>");
                if (path == "/search/")
                {
                    Check(
                        body.Contains("sort=download")
                            && body.Contains("order=down")
                            && body.Contains("csrfmiddlewaretoken=fixture-csrf"),
                        "CMS form fields changed"
                    );
                    Check(
                        request.Headers.Referrer?.AbsoluteUri == CmsBlueprints.Home,
                        "CMS referer missing"
                    );
                    if (CsrfOnce || AlwaysCsrf)
                    {
                        CsrfOnce = false;
                        return Html("CSRF verification failed", HttpStatusCode.Forbidden);
                    }
                    return Html(Listing());
                }
                if (path.StartsWith("/detail/"))
                    return Html(
                        "<h4>注释</h4><hr><div>Author note</div><div class='file'><div>Test.nbt</div><div>下载量 12</div><div>尺寸：5×6×7</div><a href='/download/9/'>下载</a></div>"
                    );
                if (path.StartsWith("/download/"))
                    return Html(
                        OtherFormat
                            ? "<a download='test.schem' href='/upload/blueprint/test.schem'>Download</a>"
                            : "<a download='test.nbt' href='/upload/blueprint/test.nbt'>Download</a>"
                    );
                if (path.StartsWith("/upload/post/"))
                {
                    var image = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent([
                            82,
                            73,
                            70,
                            70,
                            4,
                            0,
                            0,
                            0,
                            87,
                            69,
                            66,
                            80,
                        ]),
                    };
                    image.Content.Headers.ContentType = new("image/webp");
                    return image;
                }
                return new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        BadFile ? Encoding.UTF8.GetBytes("<!doctype html><html>Server error</html>")
                        : OtherFormat ? new byte[] { 0, 1, 2, 3 }
                        : Nbt()
                    ),
                };
            }
            finally
            {
                Active--;
            }
        }
    }

    public static async Task Run(string root)
    {
        Check(
            CmsHtml.List(Listing(), 1).Items[0].Author == "Builder & Co",
            "HTML DOM decoding failed"
        );
        Check(
            CmsHtml.List("<div>0 个结果，1 页</div>", 1).Items.Count == 0,
            "Empty CMS results failed"
        );
        await Reject(
            () => Task.Run(() => CmsHtml.List("<html>changed</html>", 1)),
            "Changed HTML accepted"
        );
        Check(
            !CmsBlueprints.Allowed(new Uri("https://127.0.0.1/upload/post/a.png"))
                && !CmsBlueprints.Allowed(
                    new Uri("https://www.creativemechanicserver.com.evil.test/a")
                ),
            "CMS URL allowlist failed"
        );
        var settings = new Settings();
        var handler = new Handler();
        var cache = Path.Combine(root, "cache");
        using var client = new CmsBlueprints(cache, settings, handler);
        var pending = Enumerable
            .Range(0, 8)
            .Select(_ => client.Search("", "download", 1, default))
            .ToArray();
        var pages = await Task.WhenAll(pending);
        Check(
            handler.Calls.Count == 2 && pages.All(p => p.Items.Count == 12),
            "Default list duplicated requests or failed the 12-item cap"
        );
        Check(
            handler.Calls.All(c => c.Path is "/" or "/search/"),
            "Default list prefetched details or files"
        );
        await client.Search("", "download", 1, default);
        Check(handler.Calls.Count == 2, "Fresh cache was not reused");
        var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Reject(
            () => client.Search("cancel", "download", 1, canceled.Token),
            "Canceled caller completed"
        );
        await client.Search("cancel", "download", 1, default);
        handler.CsrfOnce = true;
        await client.Search("csrf", "download", 1, default);
        Check(
            handler.Calls.Count(c => c.Path == "/search/" && c.Body.Contains("q=csrf")) == 2,
            "CSRF was not retried exactly once"
        );
        handler.AlwaysCsrf = true;
        await Reject(
            () => client.Search("failed", "download", 1, default),
            "Repeated 403 accepted"
        );
        handler.AlwaysCsrf = false;
        client.ResumeUserRequest();
        Check(
            handler.Calls.Count(c => c.Path == "/search/" && c.Body.Contains("q=failed")) == 2,
            "Repeated 403 retried indefinitely"
        );
        var detail = await Task.WhenAll(
            client.Detail(pages[0].Items[0], default),
            client.Detail(pages[0].Items[0], default)
        );
        Check(
            handler.Calls.Count(c => c.Path == "/detail/1/") == 1
                && detail[0].Detail.Files.Single().Id == "9",
            "CMS detail deduplication or parsing failed"
        );
        var image = await Task.WhenAll(
            client.Cover(pages[0].Items[0].ImageUrl, default),
            client.Cover(pages[0].Items[0].ImageUrl, default)
        );
        Check(
            handler.Calls.Count(c => c.Path.StartsWith("/upload/post/")) == 1,
            "Cover was downloaded twice"
        );
        await Reject(
            () => client.Cover("https://localhost/upload/post/x.webp", default),
            "Unsafe cover host accepted"
        );
        var stage = Path.Combine(root, "downloads");
        var file = await client.Download(detail[0].Detail.Files[0], stage, default);
        Check(
            (await File.ReadAllBytesAsync(file)).SequenceEqual(Nbt()),
            "CMS downloaded file invalid"
        );
        await Reject(
            () => client.Download(detail[0].Detail.Files[0], stage, default),
            "Existing file was overwritten"
        );
        var link = CmsHtml.FileLink(
            "<a href='/upload/blueprint/house.litematic' download='house.litematic'>Download</a>"
        );
        Check(link.Name == "house.litematic", "CMS must preserve non-NBT blueprint extensions");
        handler.OtherFormat = true;
        var other = await client.Download(
            detail[0].Detail.Files[0],
            Path.Combine(root, "other-format"),
            default
        );
        Check(
            Path.GetExtension(other) == ".schem"
                && (await File.ReadAllBytesAsync(other)).SequenceEqual(new byte[] { 0, 1, 2, 3 }),
            "CMS must save non-Create files without changing their bytes or extension"
        );
        handler.OtherFormat = false;
        handler.BadFile = true;
        await Reject(
            () => client.Download(detail[0].Detail.Files[0], Path.Combine(root, "bad"), default),
            "Invalid file was saved"
        );
        handler.BadFile = false;
        Check(!Directory.Exists(Path.Combine(root, "bad")), "Invalid download left files behind");
        foreach (var cached in Directory.GetFiles(cache, "*.cache"))
        {
            Check(
                !Encoding
                    .UTF8.GetString(await File.ReadAllBytesAsync(cached))
                    .Contains("fixture-csrf"),
                "Session token leaked into cache"
            );
            File.SetLastWriteTimeUtc(cached, DateTime.UtcNow.AddHours(-2));
        }
        handler.Offline = true;
        var stale = await client.Search("", "download", 1, default);
        Check(stale.Stale && stale.Items.Count == 12, "Offline stale cache fallback failed");
        handler.Offline = false;
        Check(handler.Maximum == 1, "CMS requests ran concurrently");
        for (var i = 1; i < handler.Calls.Count; i++)
            Check(
                Stopwatch
                    .GetElapsedTime(handler.Calls[i - 1].Start, handler.Calls[i].Start)
                    .TotalMilliseconds >= 1490,
                "CMS request interval was bypassed"
            );
        handler.Challenge = true;
        handler.ChallengeOk = true;
        var before = handler.Calls.Count;
        await Reject(
            () => client.Search("challenge", "download", 1, default),
            "Challenge was accepted"
        );
        Check(handler.Calls.Count == before + 1, "Challenge retried automatically");
        await Reject(
            () =>
                client.Cover(
                    "https://www.creativemechanicserver.com/upload/post/blocked.webp",
                    default
                ),
            "Queued cover bypassed the access stop"
        );
        Check(handler.Calls.Count == before + 1, "Access denial allowed queued image traffic");
        client.ResumeUserRequest();
        handler.ChallengeOk = false;
        await Reject(
            () => client.Search("challenge403", "download", 1, default),
            "403 verification page accepted"
        );
        handler.Challenge = false;
        client.ResumeUserRequest();
        handler.RateLimit = true;
        await Reject(() => client.Search("limited", "download", 1, default), "429 accepted");
        before = handler.Calls.Count;
        await Reject(
            () => client.Search("limited-again", "download", 1, default),
            "Cooldown was bypassed"
        );
        Check(handler.Calls.Count == before, "Cooldown issued another request");
        var small = new CmsCache(Path.Combine(root, "small"), 256);
        small.Write("one", new byte[200]);
        small.Write("two", new byte[200]);
        Check(
            Directory.GetFiles(Path.Combine(root, "small")).Sum(f => new FileInfo(f).Length) <= 256,
            "Cache capacity exceeded"
        );
        var expirationRoot = Path.Combine(root, "expiration");
        var expiring = new CmsCache(expirationRoot);
        expiring.Write("search", new byte[10], TimeSpan.FromMinutes(10));
        expiring.Write("cover", new byte[20], TimeSpan.FromHours(24));
        foreach (var entry in Directory.GetFiles(expirationRoot))
            File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddHours(-1));
        expiring.Clean();
        Check(
            expiring.Read("search", TimeSpan.FromMinutes(10)) == null
                && expiring.Read("cover", TimeSpan.FromHours(24)) is { Fresh: true },
            "Explicit expiry cleanup must respect separate search and cover lifetimes"
        );
    }
}
