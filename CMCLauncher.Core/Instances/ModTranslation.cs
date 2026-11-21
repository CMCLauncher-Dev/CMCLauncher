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
    public const int NoticeSeconds = 8;
    public const string NoticeTitle = "Mod translation and copyright";
    public const string CopyrightNotice =
        "Mod text may be protected by copyright. The translation is saved to a local resource pack. Before sharing or publishing it, check the original mod license and obtain permission if needed.";
    public const string TranslationServiceNotice =
        "Untranslated entries are sent to MyMemory for translation. Choose Cancel if you do not want to send them.";

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

    public static Dictionary<string, Dictionary<string, string>> ReadLanguages(string file, string locale)
    {
        var languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(file);
        foreach (var entry in zip.Entries.OrderBy(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = entry.FullName.Split('/');
            if (parts.Length != 4 || parts[0] != "assets" || parts[2] != "lang"
                || !Regex.IsMatch(parts[1], @"\A[a-z0-9_.-]+\z") || parts[1] is "." or ".."
                || !parts[3].Equals(locale + ".json", StringComparison.OrdinalIgnoreCase)
                    && !parts[3].Equals(locale + ".lang", StringComparison.OrdinalIgnoreCase)
                || entry.Length > 2 * 1024 * 1024)
                continue;
            if (!languages.TryGetValue(parts[1], out var values))
                languages.Add(parts[1], values = new(StringComparer.Ordinal));
            using var reader = new StreamReader(entry.Open());
            if (parts[3].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (JsonNode.Parse(reader.ReadToEnd()) is JsonObject obj)
                    foreach (var pair in obj)
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                            values[pair.Key] = text;
            }
            else
            {
                while (reader.ReadLine() is { } line)
                {
                    if (line.TrimStart().StartsWith('#'))
                        continue;
                    var equals = line.IndexOf('=');
                    if (equals > 0)
                        values[line[..equals].Trim()] = line[(equals + 1)..];
                }
            }
        }
        return languages;
    }

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
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in entries)
                result[pair.Key] = pair.Value;
            progress?.Report((entries.Count, entries.Count));
            return new Dictionary<string, string>(result, StringComparer.Ordinal);
        }
        using var gate = new SemaphoreSlim(2);
        var completed = 0;
        var tasks = entries.Select(async pair =>
        {
            await gate.WaitAsync(ct);
            try
            {
                result[pair.Key] = await TranslateLangValue(http, pair.Value, source, target, ct);
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref completed), entries.Count));
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        return new Dictionary<string, string>(result, StringComparer.Ordinal);
    }

    static async Task<string> TranslateLangValue(
        HttpClient http, string text, string source, string target, CancellationToken ct
    )
    {
        var markers = new List<(string Token, string Value)>();
        var prefix = "CMCPLACEHOLDER" + Guid.NewGuid().ToString("N")[..8];
        var masked = Regex.Replace(text,
            @"%%|%(?:\d+\$)?[-#+ 0,(<]*\d*(?:\.\d+)?[A-Za-z]|\{[^{}\r\n]+\}|§.|<[^<>\r\n]+>|\r\n|\r|\n",
            match =>
            {
                var token = prefix + markers.Count.ToString(CultureInfo.InvariantCulture) + "Z";
                markers.Add((token, match.Value));
                return token;
            });
        var translated = await TranslateAsync(http, masked, source, target, ct);
        foreach (var (token, value) in markers)
        {
            var first = translated.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (first < 0 || translated.IndexOf(token, first + token.Length, StringComparison.OrdinalIgnoreCase) >= 0)
                return text;
            translated = translated.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }
        return translated;
    }

    public static async Task TranslateMod(
        HttpClient http, string game, string storage, string gameVersion, string modFile,
        Dictionary<string, Dictionary<string, string>> sourceEntries, string source, string target,
        CancellationToken ct
    )
    {
        var existing = ReadLanguages(modFile, target);
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (nameSpace, entries) in sourceEntries)
        {
            if (entries.Count == 0)
                continue;
            var known = existing.GetValueOrDefault(nameSpace);
            var pending = entries.Where(pair => known?.ContainsKey(pair.Key) != true)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var translated = await TranslateBatchAsync(http, pending, source, target, ct);
            if (known != null)
                foreach (var pair in known)
                    translated[pair.Key] = pair.Value;
            result.Add(nameSpace, translated);
        }
        ct.ThrowIfCancellationRequested();
        WriteResourcePack(game, storage, gameVersion, result, target);
    }

    public static void WriteResourcePack(
        string game, string storage, string gameVersion,
        Dictionary<string, Dictionary<string, string>> translations, string target
    )
    {
        if (translations.Count == 0)
            return;
        if (!Regex.IsMatch(target, @"\A[a-zA-Z]{2,3}_[a-zA-Z]{2,3}\z"))
            throw new ArgumentException("Invalid resource pack language.");
        var (major, minor) = ResourcePackFormat(storage, gameVersion);
        var legacy = major < 4;
        var locale = target.ToLowerInvariant();
        if (major <= 2)
            locale = locale[..(locale.IndexOf('_') + 1)] + locale[(locale.IndexOf('_') + 1)..].ToUpperInvariant();
        const string root = "resourcepacks/cmc-mod-translation";
        foreach (var (nameSpace, values) in translations)
        {
            if (!Regex.IsMatch(nameSpace, @"\A[a-z0-9_.-]+\z") || nameSpace is "." or "..")
                throw new ArgumentException("Invalid resource pack namespace.");
            var suffix = legacy ? ".lang" : ".json";
            var content = legacy
                ? string.Join("\n", values.Where(pair => !pair.Key.Any(char.IsControl) && !pair.Key.Contains('='))
                    .Select(pair => pair.Key + "=" + pair.Value.Replace("\r", "").Replace("\n", " "))) + "\n"
                : new JsonObject(values.Select(pair =>
                    KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))).ToJsonString();
            WriteAtomic(Store.SafePath(game, $"{root}/assets/{nameSpace}/lang/{locale}{suffix}"), content);
        }
        var pack = new JsonObject { ["description"] = "CMCLauncher mod translations" };
        if (major >= 65)
        {
            pack["min_format"] = new JsonArray(major, minor);
            pack["max_format"] = new JsonArray(major, minor);
        }
        else
            pack["pack_format"] = major;
        WriteAtomic(Store.SafePath(game, root + "/pack.mcmeta"), new JsonObject { ["pack"] = pack }.ToJsonString());
    }

    static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    static (int Major, int Minor) ResourcePackFormat(string storage, string gameVersion)
    {
        var jar = Store.SafePath(storage, $"versions/{gameVersion}/{gameVersion}.jar");
        if (File.Exists(jar))
        {
            using var zip = ZipFile.OpenRead(jar);
            if (zip.GetEntry("version.json") is { Length: <= 65536 } metadata)
            {
                using var stream = metadata.Open();
                var version = JsonNode.Parse(stream)?["pack_version"];
                var format = version?["resource"];
                if (format is JsonArray { Count: 2 } array)
                    return (array[0]!.GetValue<int>(), array[1]!.GetValue<int>());
                if (format is JsonValue number && number.TryGetValue<int>(out var major))
                    return (major, 0);
                if (format is JsonObject obj && obj["major"] is JsonValue majorValue
                    && majorValue.TryGetValue<int>(out major))
                    return (major, obj["minor"]?.GetValue<int>() ?? 0);
            }
        }
        if (!Version.TryParse(gameVersion, out var release) || release.Major != 1)
            throw new InvalidOperationException("Install the Minecraft version before generating its translation pack.");
        var patch = Math.Max(0, release.Build);
        return (release.Minor switch
        {
            < 9 => 1, 9 or 10 => 2, 11 or 12 => 3, 13 or 14 => 4, 15 => 5,
            16 => patch <= 1 ? 5 : 6, 17 => 7, 18 => 8,
            19 => patch <= 2 ? 9 : patch == 3 ? 12 : 13,
            20 => patch <= 1 ? 15 : patch == 2 ? 18 : patch <= 4 ? 22 : 32,
            21 => patch <= 1 ? 34 : patch <= 3 ? 42 : patch == 4 ? 46
                : patch == 5 ? 55 : patch == 6 ? 63 : patch <= 8 ? 64 : patch <= 10 ? 69 : 75,
            _ => throw new InvalidOperationException("Install the Minecraft version before generating its translation pack."),
        }, 0);
    }
}
