using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// Mod 语言文件翻译 — 读取 mod 内置 lang，调用免费翻译 API，生成可被游戏加载的
// 资源包覆盖文件（assets/<modid>/lang/<locale>.json），不修改原始 mod JAR。
public static class ModTranslation
{
    // 本项目 LauncherLanguage 代码（en_us/zh_cn/...）到翻译服务代码的映射。
    public static string ServiceLanguage(string code) =>
        code.ToLowerInvariant() switch
        {
            "en_us" => "en",
            "zh_cn" => "zh-CN",
            "zh_wy" => "zh-CN",
            "zh_tw" => "zh-TW",
            "ja_jp" => "ja",
            "ko_kr" => "ko",
            "de_de" => "de",
            "fr_fr" => "fr",
            "ru_ru" => "ru",
            "es_es" => "es",
            "pt_br" => "pt-BR",
            _ => "en",
        };

    // 本地规则语言（趣味语言包）：不调用翻译服务，直接在本地按规则生成。
    static bool IsLocalRuleTarget(string code) =>
        code.ToLowerInvariant() is "en_bs" or "lo_ls";

    // LOLSpeak 常用词替换表（与 lo_ls 语言包规则一致）。
    static readonly (string From, string To)[] LolSpeakRules =
    [
        ("the", "teh"),
        ("you", "u"),
        ("your", "ur"),
        ("please", "plz"),
        ("are", "ar"),
        ("is", "iz"),
        ("and", "an"),
        ("what", "wat"),
        ("know", "noe"),
        ("love", "luv"),
        ("because", "cuz"),
        ("going to", "gonna"),
        ("maybe", "mai"),
        ("have", "haz"),
        ("this", "dis"),
        ("that", "dat"),
        ("****", "****"),
    ];

    // 本地规则转换一段文本到趣味语言。
    static string LocalizeToRule(string text, string target)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (target.Equals("en_bs", StringComparison.OrdinalIgnoreCase))
        {
            // 逐词反转字母顺序（Back slang）：仅反转每个词内的字母，保留数字、
            // 占位符（{0}）首尾标点原样，最后再整体逆序单词顺序。
            var words = text.Split(' ').Where(w => w.Length > 0);
            var mapped = words.Select(word =>
            {
                if (word.Contains('{') || word.Contains('}'))
                    return word;
                var leading = 0;
                while (leading < word.Length && !char.IsLetterOrDigit(word[leading]))
                    leading++;
                var trailing = 0;
                while (
                    trailing < word.Length - leading
                    && !char.IsLetterOrDigit(word[word.Length - 1 - trailing])
                )
                    trailing++;
                var core =
                    trailing > 0 ? word[leading..(word.Length - trailing)] : word[leading..];
                var reversedCore = new string(core.ToCharArray().Reverse().ToArray());
                return word[..leading]
                    + reversedCore
                    + (trailing > 0 ? word[^trailing..] : "");
            }).ToArray();
            Array.Reverse(mapped);
            return string.Join(" ", mapped);
        }
        if (target.Equals("lo_ls", StringComparison.OrdinalIgnoreCase))
        {
            var s = text;
            foreach (var (from, to) in LolSpeakRules.Where(r => r.From != "****"))
                s = Regex.Replace(
                    s,
                    "\\b" + Regex.Escape(from) + "\\b",
                    to,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50)
                );
            return s;
        }
        return text;
    }

    // 从 JAR 包读取一个 mod 的 lang 文件（JSON 或旧式 .lang）。返回 (翻译键→原文)。
    public static Dictionary<string, string>? ReadLang(string file, string locale)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var expected = "assets/" + locale.ToLowerInvariant() + "/lang/";
            var entry = zip
                .Entries.FirstOrDefault(e =>
                    e.FullName.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
                    && (e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || e.FullName.EndsWith(".lang", StringComparison.OrdinalIgnoreCase))
                );
            if (entry == null || entry.Length > 2 * 1024 * 1024)
                return null;
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (JsonNode.Parse(text) is not JsonObject obj)
                    return result;
                foreach (var pair in obj)
                    if (pair.Value is JsonValue v)
                        result[pair.Key] = v.ToString();
            }
            else
            {
                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                        result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // 把 mod 内已有的 <target> 语言语言文件合并进来（优先保留 mod 自带翻译）。
    public static Dictionary<string, string> MergeExisting(
        Dictionary<string, string> source,
        string file,
        string target
    )
    {
        if (ReadLang(file, target) is { } existing)
            foreach (var pair in existing)
                source[pair.Key] = pair.Value;
        return source;
    }

    // 用 MyMemory 免费 API 翻译一段文本。
    public static async Task<string> TranslateAsync(
        HttpClient http,
        string text,
        string source,
        string target,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return text;
        // 剥离 HTML 标记（如 Modrinth 摘要中的 <g id="…">★</g> 高亮标签），
        // 避免翻译服务把标签文本当普通文本翻译并遗留原始标签字符。
        text = Regex.Replace(
            text,
            "<[^>]+>",
            string.Empty,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)
        );
        // 趣味语言（Back slang / LOLSpeak）直接在本地按规则生成，不调用翻译服务。
        if (IsLocalRuleTarget(target))
            return LocalizeToRule(text, target);
        var from = ServiceLanguage(source);
        var to = ServiceLanguage(target);
        var url =
            "https://api.mymemory.translated.net/get?q="
            + Uri.EscapeDataString(text)
            + "&langpair="
            + from
            + "%7C"
            + to;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://cmclauncher.github.io/");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var translated = data?["responseData"]?["translatedText"]?.ToString() ?? text;
        return translated;
    }

    // 翻译整个 lang 字典（保持键不变）。批量并发，但控制并发以免触发限流。
    public static async Task<Dictionary<string, string>> TranslateBatchAsync(
        HttpClient http,
        Dictionary<string, string> entries,
        string source,
        string target,
        CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in entries)
                result[pair.Key] = pair.Value;
            progress?.Report((entries.Count, entries.Count));
            return result;
        }
        using var gate = new SemaphoreSlim(2);
        var tasks = entries.Select(async pair =>
        {
            await gate.WaitAsync(ct);
            try
            {
                result[pair.Key] = await TranslateAsync(http, pair.Value, source, target, ct);
            }
            finally
            {
                gate.Release();
                progress?.Report((0, entries.Count));
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        return result;
    }

    // 在实例 resourcepacks 目录生成（或更新）语言覆盖资源包，返回打包后的目标语言文件路径。
    public static string? WriteResourcePack(
        string game,
        string modFile,
        string modId,
        Dictionary<string, string> translated,
        string source,
        string target
    )
    {
        if (translated.Count == 0)
            return null;
        var bundle = Store.SafePath(
            game,
            "resourcepacks/cmc-mod-translation/" + modId + "/" + target + ".json"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        var json = new JsonObject();
        foreach (var pair in translated)
            json[pair.Key] = pair.Value;
        var content = json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var tmp = bundle + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
        File.Move(tmp, bundle, true);
        WritePackMeta(game);
        return bundle;
    }

    // 生成资源包包说明（pack.mcmeta），使游戏能把它当作普通资源包加载。
    static void WritePackMeta(string game)
    {
        var root = Store.SafePath(game, "resourcepacks/cmc-mod-translation");
        var meta = Path.Combine(root, "pack.mcmeta");
        if (File.Exists(meta))
            return;
        Directory.CreateDirectory(root);
        var json = """{"pack":{"pack_format":15,"description":"CMCLauncher mod translations"}}""";
        var tmp = meta + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
        File.Move(tmp, meta, true);
    }

    // 清空该 mod 的缓存翻译（关闭翻译时避免残留覆盖）。
    public static void Clear(string game, string modId, string target)
    {
        var bundle = Store.SafePath(
            game,
            "resourcepacks/cmc-mod-translation/" + modId + "/" + target + ".json"
        );
        try
        {
            if (File.Exists(bundle))
                File.Delete(bundle);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}