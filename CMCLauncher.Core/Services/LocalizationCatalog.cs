using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 一个界面语言选项：语言代码（如 zh_cn）与显示名称。
public sealed record InterfaceLanguage(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

// 外部 JSON 覆盖随程序集附带的默认词条；损坏的语言包不会阻止启动。
public sealed class LocalizationCatalog
{
    // 前缀替换规则：把词条 key 的 Source 前缀替换为 Target 前缀。
    sealed class Prefix
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
    }

    // 正则翻译规则：对动态 key（如带 ID 的词条）做模式匹配与替换。
    sealed class Rule
    {
        public string Pattern { get; set; } = "";
        public string Replacement { get; set; } = "";
        public Dictionary<string, string>? Replacements { get; set; }
        public Dictionary<string, Dictionary<string, string>>? Captures { get; set; }
    }

    // 一个语言包：代码、显示名、词条表、别名表、前缀规则与正则规则。
    sealed class Pack
    {
        public string Code { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string>? Aliases { get; set; }
        public List<Prefix>? Prefixes { get; set; }
        public List<Rule>? Rules { get; set; }
    }

    Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    Dictionary<string, string> defaultStrings = new(StringComparer.Ordinal);
    readonly string directory;
    readonly Action<string>? diagnostic;
    Dictionary<string, Pack> packs = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static bool ValidCode(string code) => Regex.IsMatch(code, "^[a-z]{2}_[a-z]{2}$");

    // 初始化：清理目录中的过期副本并加载全部语言包。
    public LocalizationCatalog(string directory, Action<string>? diagnostic = null)
    {
        this.directory = directory;
        this.diagnostic = diagnostic;
        BundledResources.RemoveStaleCopies(directory, "Locales", diagnostic);
        Reload();
    }

    // 常用语言置顶，后续按语言代码排列，趣味语言收在列表末尾。
    public IReadOnlyList<InterfaceLanguage> Languages =>
        packs
            .Values.OrderBy(p =>
                p.Code == "en_us" ? 0
                : p.Code == "zh_cn" ? 1
                : p.Code is "zh_tw" or "zh_wy" ? 2
                : p.Code is "en_bs" or "lo_ls" ? 4
                : 3
            )
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new InterfaceLanguage(p.Code, p.DisplayName))
            .ToArray();

    // 重新加载语言包：先读程序集内嵌的默认词条，
    // 再合并 Default/ 自带语言包与外层用户语言包（后者覆盖前者）。
    public void Reload()
    {
        var next = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(LocalizationCatalog).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith("CMCLauncher.Locales.", StringComparison.Ordinal)
                    && n.EndsWith(".json")
                )
        )
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var pack = Parse(new StreamReader(stream).ReadToEnd());
            next[pack.Code] = pack;
        }
        // Keep pristine English templates for recovery from invalid external formatting.
        aliases = new(next["en_us"].Aliases ?? [], StringComparer.Ordinal);
        defaultStrings = new(next["en_us"].Strings, StringComparer.Ordinal);
        try
        {
            // Default/ 中的自带语言包先合并，外层的用户语言包再覆盖其中的词条。
            foreach (var (path, _) in BundledResources.JsonFiles(directory))
                {
                    try
                    {
                        var code = Path.GetFileNameWithoutExtension(path);
                        if (!ValidCode(code))
                            continue;
                        if (new FileInfo(path).Length > 4_000_000)
                            throw new IOException("Language pack exceeds 4 MB.");
                        var external = Parse(File.ReadAllText(path));
                        if (external.Code != code)
                            throw new IOException("Language code does not match the filename.");
                        if (next.TryGetValue(code, out var bundled))
                        {
                            foreach (var pair in external.Strings)
                                bundled.Strings[pair.Key] = pair.Value;
                            bundled.DisplayName = external.DisplayName;
                            bundled.Prefixes = external.Prefixes ?? bundled.Prefixes;
                            bundled.Rules = external.Rules ?? bundled.Rules;
                        }
                        else
                            next[code] = external;
                    }
                    catch (Exception ex)
                        when (ex
                                is IOException
                                    or UnauthorizedAccessException
                                    or JsonException
                                    or ArgumentException
                        )
                    {
                        diagnostic?.Invoke(
                            "Language pack ignored: "
                                + Path.GetRelativePath(directory, path)
                                + " · "
                                + ex.Message
                        );
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("Language directory unavailable: " + ex.Message);
        }
        packs = next;
    }

    // 解析并校验语言包 JSON（代码格式、显示名、词条、规则合法性）。
    static Pack Parse(string json)
    {
        var pack =
            JsonSerializer.Deserialize<Pack>(json, Options)
            ?? throw new JsonException("Empty language pack.");
        if (
            !ValidCode(pack.Code)
            || string.IsNullOrWhiteSpace(pack.DisplayName)
            || pack.Strings == null
            || pack.Strings.Any(p => p.Value == null)
        )
            throw new JsonException("Invalid language pack metadata or strings.");
        foreach (var rule in pack.Rules ?? [])
        {
            if (string.IsNullOrEmpty(rule.Pattern) || rule.Replacement == null)
                throw new JsonException("Invalid translation rule.");
            _ = new Regex(
                rule.Pattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)
            );
        }
        if (pack.Prefixes?.Any(p => string.IsNullOrEmpty(p.Source) || p.Target == null) == true)
            throw new JsonException("Invalid translation prefix.");
        return pack;
    }

    // 翻译词条：先查别名与词条表，再依次尝试正则规则与前缀规则，
    // 都没有时回退到英文词条，最后原样返回 key。
    public string Translate(string language, string key)
    {
        language ??= "en_us";
        key = aliases.GetValueOrDefault(key, key);
        if (!packs.ContainsKey(language))
            language = "en_us";
        if (packs.TryGetValue(language, out var pack))
        {
            if (pack.Strings.TryGetValue(key, out var value))
                return value;
            foreach (var rule in pack.Rules ?? [])
            {
                try
                {
                    var regex = new Regex(
                        rule.Pattern,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)
                    );
                    if (!regex.IsMatch(key))
                        continue;
                    var translated = regex.Replace(
                        key,
                        match =>
                            Regex.Replace(
                                rule.Replacement,
                                @"\$(\d+)",
                                capture =>
                                {
                                    var id = capture.Groups[1].Value;
                                    var content = match.Groups[int.Parse(id)].Value;
                                    return rule.Captures?.TryGetValue(id, out var mapping) == true
                                        ? mapping
                                            .FirstOrDefault(p =>
                                                p.Key.Equals(
                                                    content,
                                                    StringComparison.OrdinalIgnoreCase
                                                )
                                            )
                                            .Value ?? content
                                        : content;
                                }
                            )
                    );
                    foreach (var pair in rule.Replacements ?? [])
                        translated = translated.Replace(
                            pair.Key,
                            pair.Value,
                            StringComparison.Ordinal
                        );
                    return translated;
                }
                catch (RegexMatchTimeoutException)
                {
                    diagnostic?.Invoke("Translation rule timed out.");
                }
            }
            foreach (var prefix in pack.Prefixes ?? [])
                if (key.StartsWith(prefix.Source, StringComparison.Ordinal))
                    return prefix.Target + key[prefix.Source.Length..];
        }
        var english =
            packs.TryGetValue("en_us", out var fallback)
            && fallback.Strings.TryGetValue(key, out var original)
            ? original
            : key;
        return language.Equals("en_bs", StringComparison.OrdinalIgnoreCase)
            ? UpsideDownEnglish.Transform(english)
            : english;
    }

    // 翻译并按目标语言文化格式化占位符；格式错误时回退英文模板，仍失败则返回 key。
    public string Format(string language, string key, params object?[] values)
    {
        language ??= "en_us";
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language.Replace('_', '-'));
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }
        try
        {
            return string.Format(culture, Translate(language, key), values);
        }
        catch (FormatException)
        {
            diagnostic?.Invoke("Invalid translation placeholders: " + key);
            try
            {
                return string.Format(
                    culture,
                    defaultStrings.GetValueOrDefault(aliases.GetValueOrDefault(key, key), key),
                    values
                );
            }
            catch (FormatException)
            {
                return key;
            }
        }
    }
}
