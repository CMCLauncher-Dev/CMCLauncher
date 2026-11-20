using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 主题预设：ID、显示名称、是否内置。
public sealed record ThemePreset(string Id, string DisplayName, bool IsBuiltIn);

// 主题目录：独立加载外部调色板，损坏的文件不会影响可用的内置主题。
public sealed class ThemeCatalog
{
    // 单个主题定义：版本、ID、显示名称，以及基础/浅色/深色三组颜色令牌。
    sealed class Definition
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Light { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Dark { get; set; } = new(StringComparer.Ordinal);
    }

    const string ResourcePrefix = "CMCLauncher.Themes.";
    readonly string directory;
    readonly Action<string>? diagnostic;
    // 全部主题定义（内置 + 外部），按 ID 索引。
    Dictionary<string, Definition> definitions = new(StringComparer.Ordinal);
    // 程序集内嵌的主题 ID 集合。
    HashSet<string> embeddedIds = new(StringComparer.Ordinal);
    // 自带（Default/）主题 ID 集合。
    HashSet<string> builtIns = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // 初始化主题目录：清理过期副本并加载全部主题。
    public ThemeCatalog(string directory, Action<string>? diagnostic = null)
    {
        this.directory = directory;
        this.diagnostic = diagnostic;
        BundledResources.RemoveStaleCopies(directory, "Themes", diagnostic);
        Reload();
    }

    // 程序集内置的主题在前，其次是 Default/ 中的自带主题，最后是用户新增的主题。
    public IReadOnlyList<ThemePreset> Presets =>
        definitions
            .Values.OrderBy(p =>
                embeddedIds.Contains(p.Id) ? 0
                : builtIns.Contains(p.Id) ? 1
                : 2
            )
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new ThemePreset(p.Id, p.DisplayName, builtIns.Contains(p.Id)))
            .ToArray();

    public bool IsBuiltIn(string id) => builtIns.Contains(id);

    // 重新加载全部主题：先读程序集内嵌主题，再叠加 Default/ 与用户主题文件。
    public void Reload()
    {
        var next = new Dictionary<string, Definition>(StringComparer.Ordinal);
        var embedded = new HashSet<string>(StringComparer.Ordinal);
        var assembly = typeof(ThemeCatalog).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    && n.EndsWith(".json", StringComparison.Ordinal)
                )
        )
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var definition = Parse(new StreamReader(stream).ReadToEnd());
            next.Add(definition.Id, definition);
            embedded.Add(definition.Id);
        }
        var shipped = new HashSet<string>(embedded, StringComparer.Ordinal);
        try
        {
            // 叠加外部主题文件：校验大小、ID 与文件名一致，同 ID 时合并覆盖颜色。
            foreach (var (path, isDefault) in BundledResources.JsonFiles(directory))
            {
                try
                {
                    if (new FileInfo(path).Length > 256_000)
                        throw new IOException("Theme file exceeds 256 KB.");
                    var definition = Parse(File.ReadAllText(path));
                    if (definition.Id != Path.GetFileNameWithoutExtension(path))
                        throw new JsonException("Theme ID does not match its filename.");
                    if (next.TryGetValue(definition.Id, out var bundled))
                    {
                        // 后加载的文件可覆盖先前调色板的部分颜色，而无需重复整份定义。
                        foreach (var pair in definition.Colors)
                            bundled.Colors[pair.Key] = pair.Value;
                        foreach (var pair in definition.Light)
                            bundled.Light[pair.Key] = pair.Value;
                        foreach (var pair in definition.Dark)
                            bundled.Dark[pair.Key] = pair.Value;
                        bundled.DisplayName = definition.DisplayName;
                    }
                    else
                        next.Add(definition.Id, definition);
                    if (isDefault)
                        shipped.Add(definition.Id);
                }
                catch (Exception ex)
                    when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    diagnostic?.Invoke(
                        "Theme file ignored: "
                            + Path.GetRelativePath(directory, path)
                            + " · "
                            + ex.Message
                    );
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("Theme directory unavailable: " + ex.Message);
        }
        definitions = next;
        embeddedIds = embedded;
        builtIns = shipped;
    }

    // 解析并校验主题 JSON：版本、ID 格式、显示名称、颜色令牌与取值均须合法。
    static Definition Parse(string json)
    {
        var definition =
            JsonSerializer.Deserialize<Definition>(json, Options)
            ?? throw new JsonException("Empty theme file.");
        if (
            definition.Version != 1
            || !Regex.IsMatch(definition.Id ?? "", "^[a-z][a-z0-9_-]{0,63}$")
            || definition.Id is "native" or "custom"
            || string.IsNullOrWhiteSpace(definition.DisplayName)
            || definition.DisplayName.Length > 80
            || definition.DisplayName.Any(char.IsControl)
        )
            throw new JsonException("Invalid theme metadata or reserved theme ID.");
        foreach (var colors in new[] { definition.Colors, definition.Light, definition.Dark })
        {
            if (
                colors == null
                || colors.Count > 256
                || colors.Any(p =>
                    !Regex.IsMatch(p.Key, "^[A-Za-z][A-Za-z0-9]*$")
                    || !Regex.IsMatch(p.Value ?? "", "^#[0-9A-Fa-f]{6}$")
                )
            )
                throw new JsonException("Theme colors must use token names and #RRGGBB values.");
        }
        if (definition.Colors.Count + definition.Light.Count + definition.Dark.Count == 0)
            throw new JsonException("Theme has no colors.");
        return definition;
    }

    // 解析颜色令牌：优先主题自身（浅色/深色 → 基础），缺失时回退到 craft 主题，
    // 仍缺失则返回原始值。native/custom 主题直接返回原始值。
    public string Resolve(string id, string token, bool light, string original)
    {
        if (id is "native" or "custom")
            return original;
        if (definitions.TryGetValue(id, out var definition))
        {
            var modes = light ? definition.Light : definition.Dark;
            if (modes.TryGetValue(token, out var value))
                return value;
            if (definition.Colors.TryGetValue(token, out value))
                return value;
        }
        // 外部调色板可能省略颜色；缺失的文件与未知 ID 回退到 Fresh 绿色。
        if (definitions.TryGetValue("craft", out var fallback))
        {
            var modes = light ? fallback.Light : fallback.Dark;
            if (modes.TryGetValue(token, out var value))
                return value;
            if (fallback.Colors.TryGetValue(token, out value))
                return value;
        }
        return original;
    }
}
