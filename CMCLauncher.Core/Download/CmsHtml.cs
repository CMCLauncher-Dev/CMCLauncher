using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace CMCLauncher;

// CMS 蓝图文件条目：Id、名称与尺寸。
public sealed record CmsFile(string Id, string Name, string Size);

// CMS 蓝图详情页解析结果：入口条目、文件列表与注释。
public sealed record CmsDetail(Blueprint Entry, IReadOnlyList<CmsFile> Files, string Note);

// CMS 列表页解析结果：条目列表、当前页、总页数、总结果数与是否过期。
public sealed record CmsPage(
    IReadOnlyList<Blueprint> Items,
    int Page,
    int Pages,
    int? Total,
    bool Stale = false
);

// CMS（Creative Mechanic Server）HTML 解析器：
// 从 CMS 网站的列表页、详情页与下载页中提取蓝图信息，
// 并检测 Cloudflare 等浏览器验证挑战。
public static class CmsHtml
{
    // 解析 HTML 为文档；若检测到浏览器验证挑战则抛出异常。
    static IDocument Document(string html)
    {
        if (IsChallenge(html))
            throw new IOException("CMS requires browser verification. Open the CMS website.");
        return new HtmlParser().ParseDocument(html);
    }

    // 检测页面是否为 Cloudflare / reCAPTCHA / hCaptcha 等浏览器验证挑战页。
    public static bool IsChallenge(string text) =>
        text.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
        || text.Contains("challenge-form", StringComparison.OrdinalIgnoreCase)
        || text.Contains("g-recaptcha", StringComparison.OrdinalIgnoreCase)
        || text.Contains("h-captcha", StringComparison.OrdinalIgnoreCase)
        || text.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase);

    // 提取元素的纯文本（折叠空白并去除首尾空格）。
    static string Plain(IElement? element) =>
        Regex
            .Replace(
                element?.TextContent ?? "",
                @"\s+",
                " ",
                RegexOptions.None,
                TimeSpan.FromMilliseconds(100)
            )
            .Trim();

    // 从卡片中按选择器提取字段文本，并去除指定前缀（如"作者："）。
    static string Field(IElement card, string selector, string prefix) =>
        Plain(card.QuerySelector(selector)).Replace(prefix, "").Trim();

    // 从 HTML 中提取 CSRF token；未找到时抛出异常。
    public static string Csrf(string html) =>
        Document(html).QuerySelector("input[name=csrfmiddlewaretoken]")?.GetAttribute("value")
        ?? throw new IOException("CMS session could not be initialized. Open the CMS website.");

    // 解析 CMS 列表页：提取蓝图卡片（标题、封面、作者、下载量、版本、尺寸）与分页信息。
    public static CmsPage List(string html, int page)
    {
        var document = Document(html);
        var cards = document
            .QuerySelectorAll("a[href]")
            .Where(a =>
                a.ClassList.Contains("list_result")
                && Regex.IsMatch(
                    a.GetAttribute("href") ?? "",
                    @"^/detail/\d+/$",
                    RegexOptions.None,
                    TimeSpan.FromMilliseconds(100)
                )
            )
            .ToArray();
        var text = Plain(document.Body);
        var meta = Regex.Match(
            text,
            @"(\d+)\s*个结果(?:[，,]\s*(\d+)\s*页)?",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(100)
        );
        if (cards.Length == 0 && !meta.Success)
            throw new IOException(
                "CMS page structure changed or could not be parsed. Open the CMS website."
            );
        int? total =
            meta.Success && int.TryParse(meta.Groups[1].Value, out var count) ? count : null;
        var pages =
            meta.Groups[2].Success && int.TryParse(meta.Groups[2].Value, out var parsed)
                ? parsed
                : Math.Max(1, ((total ?? 0) + 19) / 20);
        var entries = cards
            .Select(card =>
            {
                var href = card.GetAttribute("href")!;
                var title = Plain(card.QuerySelector("h2.title, h2"));
                if (title.Length == 0)
                    throw new IOException("CMS blueprint title could not be parsed.");
                var cover =
                    card.QuerySelector("img[src^='/upload/post/']")?.GetAttribute("src") ?? "";
                return new Blueprint(
                    href.Split('/')[2],
                    title,
                    Plain(card.QuerySelector(".desc")),
                    Field(card, ".author", "作者："),
                    "https://www.creativemechanicserver.com" + href,
                    cover.Length == 0 ? "" : "https://www.creativemechanicserver.com" + cover,
                    Downloads: Field(card, ".download", "下载量："),
                    Versions: string.Join(
                        " · ",
                        card.QuerySelectorAll(".version .t").Select(Plain)
                    ),
                    Size: Field(card, ".size", "尺寸：")
                );
            })
            .ToArray();
        return new(entries, page, Math.Max(page, pages), total);
    }

    // 解析 CMS 详情页：提取文件列表（Id、名称、尺寸）与"注释"段落。
    public static CmsDetail Detail(Blueprint entry, string html)
    {
        var document = Document(html);
        var files = document
            .QuerySelectorAll("a[href]")
            .Where(a =>
                Regex.IsMatch(
                    a.GetAttribute("href") ?? "",
                    @"^/download/\d+/$",
                    RegexOptions.None,
                    TimeSpan.FromMilliseconds(100)
                )
            )
            .Select(a =>
            {
                var parent = a.ParentElement;
                var parts =
                    parent
                        ?.Children.Where(c => c.LocalName == "div")
                        .Select(Plain)
                        .Where(s => s.Length > 0)
                        .ToArray() ?? [];
                return new CmsFile(
                    a.GetAttribute("href")!.Split('/')[2],
                    parts.FirstOrDefault() ?? Plain(a),
                    parts.FirstOrDefault(t => t.StartsWith("尺寸")) ?? ""
                );
            })
            .DistinctBy(f => f.Id)
            .ToArray();
        if (files.Length == 0)
            throw new IOException("CMS blueprint files could not be parsed. Open the CMS website.");
        var heading = document.QuerySelectorAll("h4").FirstOrDefault(h => Plain(h) == "注释");
        var note = heading?.NextElementSibling;
        if (note?.LocalName == "hr")
            note = note.NextElementSibling;
        return new(entry, files, Plain(note));
    }

    // 解析 CMS 下载页：提取蓝图文件的下载 URL 与文件名，并校验文件名安全性。
    public static (Uri Url, string Name) FileLink(string html)
    {
        var document = Document(html);
        var link =
            document
                .QuerySelectorAll("a[href]")
                .FirstOrDefault(a =>
                    a.GetAttribute("href")!
                        .StartsWith("/upload/blueprint/", StringComparison.Ordinal)
                )
            ?? throw new IOException(
                "CMS download link could not be parsed. Open the CMS website."
            );
        var uri = new Uri(
            new Uri("https://www.creativemechanicserver.com"),
            link.GetAttribute("href")
        );
        var name = Uri.UnescapeDataString(
            link.GetAttribute("download") ?? Path.GetFileName(uri.LocalPath)
        );
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(uri.LocalPath);
        if (
            Path.GetFileName(name) != name
            || name.Contains('\\')
            || string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        )
            throw new IOException("CMS returned an unsafe filename.");
        return (uri, name);
    }
}
