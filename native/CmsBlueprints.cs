using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// One instance is retained by the window; the static gate also serializes separate consumers.
public sealed class CmsBlueprints : IDisposable
{
    public const string Home = "https://www.creativemechanicserver.com/";
    static readonly SemaphoreSlim PageGate = new(1, 1);
    static long lastStart;
    static DateTimeOffset blockedUntil;
    static volatile bool accessDenied;

    // Only explicit user actions resume access after a denial; 429 cooldown is never cleared here.
    public void ResumeUserRequest() => accessDenied = false;

    readonly HttpClient http;
    readonly CmsCache cache;
    readonly Func<int> interval;
    readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> pending = new();
    string? csrf;

    public CmsBlueprints(string cacheRoot, Settings settings, HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            Proxy = NetworkProxy.Create(settings),
            UseProxy = NetworkProxy.Mode(settings) != "direct",
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CMCLauncher/1.0 (+https://github.com/com-in/CMCLauncher)"
        );
        cache = new CmsCache(cacheRoot);
        interval = () => Math.Clamp(settings.CmsRequestIntervalMs, 1500, 60000);
    }

    public static bool Allowed(Uri uri) =>
        uri.Scheme == "https"
        && uri.Port == 443
        && uri.UserInfo.Length == 0
        && (
            uri.Host == "www.creativemechanicserver.com" || uri.Host == "creativemechanicserver.com"
        );

    async Task<(byte[] Data, string Type)> Send(
        Func<HttpRequestMessage> create,
        int maximum,
        CancellationToken ct
    )
    {
        await PageGate.WaitAsync(ct);
        try
        {
            if (accessDenied)
                throw new IOException(
                    "CMS access was stopped. Try a manual search later or open the website."
                );
            if (DateTimeOffset.UtcNow < blockedUntil)
                throw new IOException(
                    "CMS is cooling down after an access limit. Try again later."
                );
            var wait = interval() - Stopwatch.GetElapsedTime(lastStart).TotalMilliseconds;
            if (wait > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(wait), ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var current = create();
            for (var redirect = 0; redirect < 4; redirect++)
            {
                using var request = current;
                if (request.RequestUri == null || !Allowed(request.RequestUri))
                    throw new IOException("CMS returned an unsafe URL.");
                if (redirect > 0)
                    await Task.Delay(interval(), deadline.Token);
                lastStart = Stopwatch.GetTimestamp();
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    deadline.Token
                );
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter;
                    blockedUntil =
                        retry?.Date
                        ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.FromMinutes(2));
                    if (blockedUntil <= DateTimeOffset.UtcNow)
                        blockedUntil = DateTimeOffset.UtcNow.AddSeconds(1);
                    throw new IOException(
                        "CMS requests are too frequent (HTTP 429). Try again later."
                    );
                }
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location =
                        response.Headers.Location
                        ?? throw new IOException("CMS returned an invalid redirect.");
                    var uri = new Uri(request.RequestUri, location);
                    if (!Allowed(uri))
                        throw new IOException("CMS returned an unsafe redirect.");
                    current = new HttpRequestMessage(HttpMethod.Get, uri);
                    current.Headers.Referrer = new Uri(Home);
                    continue;
                }
                var data = await Read(response.Content, maximum, deadline.Token);
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var body = Encoding.UTF8.GetString(data);
                    if (
                        !CmsHtml.IsChallenge(body)
                        && body.Contains("CSRF", StringComparison.OrdinalIgnoreCase)
                        && (
                            body.Contains("验证失败")
                            || body.Contains(
                                "verification failed",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                        throw new CmsCsrfException();
                    accessDenied = true;
                    throw new IOException(
                        "CMS temporarily refused access (HTTP 403). Try again later or open the website."
                    );
                }
                if ((int)response.StatusCode >= 500)
                    throw new IOException(
                        "CMS is temporarily unavailable (HTTP 5xx). Try again later."
                    );
                response.EnsureSuccessStatusCode();
                if (
                    type.Contains("html", StringComparison.OrdinalIgnoreCase)
                    && CmsHtml.IsChallenge(Encoding.UTF8.GetString(data))
                )
                {
                    accessDenied = true;
                    throw new IOException(
                        "CMS requires browser verification. Open the CMS website."
                    );
                }
                return (data, type);
            }
            throw new IOException("CMS returned too many redirects.");
        }
        finally
        {
            PageGate.Release();
        }
    }

    static async Task<byte[]> Read(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum)
            throw new IOException("CMS response exceeds the size limit.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + n > maximum)
                throw new IOException("CMS response exceeds the size limit.");
            await bytes.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        return bytes.ToArray();
    }

    async Task<string> Page(string path, CancellationToken ct)
    {
        var response = await Send(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(Home), path));
                request.Headers.Referrer = new Uri(Home);
                return request;
            },
            4 * 1024 * 1024,
            ct
        );
        if (!response.Type.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new IOException(
                "CMS page structure changed or could not be parsed. Open the CMS website."
            );
        return Encoding.UTF8.GetString(response.Data);
    }

    async Task Prime(CancellationToken ct) => csrf = CmsHtml.Csrf(await Page("/", ct));

    async Task<string> SearchHtml(string query, string sort, int page, CancellationToken ct)
    {
        if (csrf == null)
            await Prime(ct);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await Send(
                    () =>
                    {
                        var form = new Dictionary<string, string>
                        {
                            ["search_type"] = "t",
                            ["q"] = query,
                            ["consume"] = "",
                            ["product"] = "",
                            ["loader_type"] = "any",
                            ["mc_type"] = "any",
                            ["create_type"] = "any",
                            ["z_size"] = "",
                            ["x_size"] = "",
                            ["y_size"] = "",
                            ["function"] = "",
                            ["grid_col"] = "1",
                            ["sort"] = sort,
                            ["order"] = "down",
                            ["page"] = page.ToString(
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            ["csrfmiddlewaretoken"] = csrf!,
                        };
                        var request = new HttpRequestMessage(HttpMethod.Post, Home + "search/")
                        {
                            Content = new FormUrlEncodedContent(form),
                        };
                        request.Headers.Referrer = new Uri(Home);
                        return request;
                    },
                    4 * 1024 * 1024,
                    ct
                );
                return Encoding.UTF8.GetString(response.Data);
            }
            catch (CmsCsrfException) when (attempt == 0)
            {
                csrf = null;
                await Prime(ct);
            }
            catch (CmsCsrfException)
            {
                accessDenied = true;
                throw;
            }
        }
        throw new IOException(
            "CMS temporarily refused access (HTTP 403). Try again later or open the website."
        );
    }

    sealed class CmsCsrfException : IOException
    {
        public CmsCsrfException()
            : base(
                "CMS temporarily refused access (HTTP 403). Try again later or open the website."
            ) { }
    }

    async Task<(T Data, bool Stale)> Cached<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken ct,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type
    )
    {
        var stored = cache.Read(key, ttl);
        if (stored is { Fresh: true })
            try
            {
                return (JsonSerializer.Deserialize<T>(stored.Value.Data, type)!, false);
            }
            catch (JsonException)
            {
                stored = null;
            }
        var lazy = pending.GetOrAdd(
            key,
            _ => new Lazy<Task<byte[]>>(async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await fetch(deadline.Token);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(result, type);
                try
                {
                    cache.Write(key, bytes, ttl);
                }
                catch (IOException) { }
                return bytes;
            })
        );
        var task = lazy.Value;
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                pending.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(key, lazy));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        try
        {
            return (JsonSerializer.Deserialize<T>(await task.WaitAsync(ct), type)!, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (stored != null
                && ex is IOException or HttpRequestException or OperationCanceledException
            )
        {
            return (JsonSerializer.Deserialize<T>(stored.Value.Data, type)!, true);
        }
    }

    public async Task<CmsPage> Search(string query, string sort, int page, CancellationToken ct)
    {
        if (query.Length > 200 || page < 1 || page > 10000 || sort is not ("download" or "time"))
            throw new ArgumentException("Invalid CMS search parameters.");
        var result = await Cached(
            "list:v1:"
                + JsonSerializer.Serialize(
                    new[] { query, sort, page.ToString() },
                    CmsJsonContext.Default.StringArray
                ),
            query.Length == 0 ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10),
            async token =>
            {
                var html = await SearchHtml(query, sort, page, token);
                return await Task.Run(() => CmsHtml.List(html, page), token);
            },
            ct,
            CmsJsonContext.Default.CmsPage
        );
        var data = result.Data;
        return data with
        {
            Items = query.Length == 0 && page == 1 ? data.Items.Take(12).ToArray() : data.Items,
            Stale = result.Stale,
        };
    }

    public async Task<(CmsDetail Detail, bool Stale)> Detail(Blueprint entry, CancellationToken ct)
    {
        if (!int.TryParse(entry.Id, out var id) || id <= 0)
            throw new ArgumentException("Invalid CMS blueprint.");
        var result = await Cached(
            "detail:v1:" + entry.Id,
            TimeSpan.FromMinutes(30),
            async token =>
            {
                var html = await Page("/detail/" + entry.Id + "/", token);
                return await Task.Run(() => CmsHtml.Detail(entry, html), token);
            },
            ct,
            CmsJsonContext.Default.CmsDetail
        );
        return (result.Data, result.Stale);
    }

    public async Task<byte[]> Cover(string url, CancellationToken ct)
    {
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !Allowed(uri)
            || !uri.AbsolutePath.StartsWith("/upload/post/", StringComparison.Ordinal)
            || !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(
                Path.GetExtension(uri.AbsolutePath),
                StringComparer.OrdinalIgnoreCase
            )
        )
            throw new IOException("CMS returned an unsafe image URL.");
        var stored = cache.Read("cover:" + url, TimeSpan.FromHours(24));
        if (stored is { Fresh: true })
            return stored.Value.Data;
        var lazy = pending.GetOrAdd(
            "cover:" + url,
            _ => new Lazy<Task<byte[]>>(async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var image = await Send(
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.Referrer = new Uri(Home);
                        return request;
                    },
                    4 * 1024 * 1024,
                    deadline.Token
                );
                if (
                    !new[] { "image/png", "image/jpeg", "image/webp" }.Contains(
                        image.Type,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                    throw new IOException("CMS returned an invalid image.");
                var bytes = image.Data;
                var valid =
                    bytes.Length >= 12
                    && (
                        image.Type == "image/webp"
                            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
                        || image.Type == "image/png"
                            && bytes
                                .AsSpan(0, 8)
                                .SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                        || image.Type == "image/jpeg"
                            && bytes[0] == 255
                            && bytes[1] == 216
                            && bytes[2] == 255
                    );
                if (!valid)
                    throw new IOException("CMS returned an invalid image.");
                try
                {
                    cache.Write("cover:" + url, image.Data, TimeSpan.FromHours(24));
                }
                catch (IOException) { }
                return image.Data;
            })
        );
        var task = lazy.Value;
        _ = task.ContinueWith(
            _ =>
                pending.TryRemove(
                    new KeyValuePair<string, Lazy<Task<byte[]>>>("cover:" + url, lazy)
                ),
            TaskScheduler.Default
        );
        try
        {
            return await task.WaitAsync(ct);
        }
        catch (Exception ex) when (stored != null && ex is IOException or HttpRequestException)
        {
            return stored.Value.Data;
        }
    }

    public async Task<string> Download(CmsFile file, string stage, CancellationToken ct)
    {
        if (!int.TryParse(file.Id, out var id) || id <= 0)
            throw new ArgumentException("Invalid CMS file.");
        var relay = await Page("/download/" + id + "/", ct);
        var link = CmsHtml.FileLink(relay);
        if (
            !Allowed(link.Url)
            || !link.Url.AbsolutePath.StartsWith("/upload/blueprint/", StringComparison.Ordinal)
        )
            throw new IOException("CMS returned an unsafe URL.");
        var download = await Send(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, link.Url);
                request.Headers.Referrer = new Uri(Home + "download/" + id + "/");
                return request;
            },
            8 * 1024 * 1024,
            ct
        );
        var prefix = Encoding
            .UTF8.GetString(download.Data.AsSpan(0, Math.Min(download.Data.Length, 512)))
            .TrimStart();
        if (
            download.Data.Length == 0
            || download.Type.Contains("html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
        )
            throw new IOException(
                "CMS returned an empty file or a web page instead of a blueprint."
            );
        Directory.CreateDirectory(stage);
        var path = Store.SafePath(stage, link.Name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, download.Data, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void CleanCache() => cache.Clean();

    public void Dispose() => http.Dispose();
}
