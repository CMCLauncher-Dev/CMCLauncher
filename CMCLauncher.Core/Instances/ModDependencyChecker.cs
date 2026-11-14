using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 一次依赖检查发现的问题。Kind 用于区分缺失依赖 / 版本冲突。
public sealed record ModDependencyIssue(string Kind, string MessageKey, string[] Args);

// 启动前分析实例 mods 目录：缺失依赖与版本冲突。
// 纯 Core 逻辑，WinUI 与 Avalonia 可共享；缺失依赖直接复用 ContentManager 的成熟实现。
public static class ModDependencyChecker
{
    // 这些 id 由 Minecraft 或加载器提供，不作为普通 Mod 依赖检查对象。
    static readonly HashSet<string> SystemProviders = new(
        new[]
        {
            "minecraft",
            "java",
            "fabricloader",
            "quilt_loader",
            "forge",
            "neoforge",
            "fabric",
            "quilt",
        },
        StringComparer.OrdinalIgnoreCase
    );

    sealed record Dependency(string Id, string? Version);

    public static List<ModDependencyIssue> Check(string game)
    {
        var issues = new List<ModDependencyIssue>();
        List<ContentItem> enabled;
        try
        {
            enabled = ContentManager.List(game, "mod").Where(m => m.Enabled).ToList();
        }
        catch
        {
            return issues;
        }

        var installed = new Dictionary<string, ContentItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in enabled)
            foreach (var id in ContentManager.ModIds(
                ContentManager.Locate(game, "mod", null, item.File)
            ))
                installed.TryAdd(id, item);

        // 缺失依赖：depend 为缺失前置 id，dependent 为依赖它的 Mod 文件名。
        try
        {
            foreach (var (dep, dependent) in ContentManager.MissingDependencies(game))
                issues.Add(
                    new ModDependencyIssue(
                        "missing",
                        "Mod \"{0}\" requires the missing dependency \"{1}\"",
                        new[] { dependent, dep }
                    )
                );
        }
        catch
        {
            // 读取失败时跳过缺失依赖，继续版本检测。
        }

        // 版本冲突：已安装的前置版本不满足某 Mod 声明的版本区间。
        foreach (var item in enabled)
        {
            var file = ContentManager.Locate(game, "mod", null, item.File);
            foreach (var dep in DependenciesWithVersion(file))
            {
                if (dep.Version is not { Length: > 0 } range)
                    continue;
                if (SystemProviders.Contains(dep.Id))
                    continue;
                if (!installed.TryGetValue(dep.Id, out var provider))
                    continue;
                if (Satisfies(provider.Version, range))
                    continue;
                issues.Add(
                    new ModDependencyIssue(
                        "conflict",
                        "Mod \"{0}\" requires {1} version \"{2}\" but installed \"{3}\" has version \"{4}\"",
                        new[] { item.Name, dep.Id, range, provider.Name, provider.Version }
                    )
                );
            }
        }

        return issues;
    }

    // 从 JAR 包提取依赖 id 及声明的版本区间（无区间则为 null）。
    static List<Dependency> DependenciesWithVersion(string file)
    {
        var deps = new List<Dependency>();
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(file);
            string? Read(string path)
            {
                var entry = zip.GetEntry(path);
                if (entry == null || entry.Length > 2 * 1024 * 1024)
                    return null;
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }
            foreach (var path in new[] { "fabric.mod.json", "quilt.mod.json" })
                if (Read(path) is string json)
                {
                    var data = JsonNode.Parse(json);
                    var depends =
                        path.StartsWith("quilt")
                            ? data?["quilt_loader"]?["depends"] as JsonObject
                            : data?["depends"] as JsonObject;
                    if (depends == null)
                        continue;
                    foreach (var dep in depends)
                    {
                        var range =
                            dep.Value is JsonValue v ? v.ToString()
                            : dep.Value?["version"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(range))
                            continue;
                        deps.Add(new Dependency(dep.Key, Clean(range)));
                    }
                }
            foreach (
                var path in new[]
                {
                    "META-INF/mods.toml",
                    "META-INF/neoforge.mods.toml",
                }
            )
                if (Read(path) is string toml)
                    foreach (var block in TomlBlocks(toml))
                    {
                        var body = block.Body;
                        bool required =
                            Regex.IsMatch(
                                body,
                                @"(?m)^\s*type\s*=\s*[""']required[""']"
                            )
                            || Regex.IsMatch(body, @"(?m)^\s*mandatory\s*=\s*true");
                        if (!required)
                            continue;
                        var id = Regex
                            .Match(body, "(?m)^\\s*modId\\s*=\\s*[\"']([^\"']+)")
                            .Groups[1]
                            .Value;
                        var range = Regex
                            .Match(body, "(?m)^\\s*versionRange\\s*=\\s*[\"']([^\"']+)")
                            .Groups[1]
                            .Value;
                        if (id.Length == 0)
                            continue;
                        deps.Add(
                            new Dependency(id, string.IsNullOrWhiteSpace(range) ? null : Clean(range))
                        );
                    }
        }
        catch (Exception ex)
            when (
                ex is IOException
                    or InvalidDataException
                    or System.Text.Json.JsonException
                    or UnauthorizedAccessException
            ) { }
        return deps;
    }

    static IEnumerable<(string Header, string Body)> TomlBlocks(string toml)
    {
        foreach (
            Match m in Regex.Matches(
                toml,
                @"(?ms)^[ \t]*\[{1,2}\s*([^\]#]*?)\s*\]{1,2}[ \t]*$(.*?)(?=^[ \t]*\[{1,2}|\z)"
            )
        )
            yield return (m.Groups[1].Value.Trim(), m.Groups[2].Value);
    }

    static string Clean(string range) => range.Trim('"', '\'', ' ', '\t');

    // ---------- 轻量语义版本与 Maven 范围判定 ----------

    static bool Satisfies(string version, string range)
    {
        foreach (var part in range.Split(','))
        {
            var req = part.Trim();
            if (req.Length == 0)
                continue;
            if (req.Contains(" - ", StringComparison.Ordinal))
            {
                var ends = req.Split(new[] { " - " }, 2, StringSplitOptions.None);
                var lower = ends[0].Trim();
                var upper = ends[1].Trim();
                if (
                    CompareVersion(version, TrimOps(lower)) >= 0
                    && CompareVersion(TrimOps(upper), version) >= 0
                )
                    return true;
                continue;
            }
            if (req[0] is '[' or '(')
            {
                if (MavenSatisfies(version, req))
                    return true;
                continue;
            }
            var op = req[0];
            if (op is '>' or '<' or '=' or '~' or '^')
            {
                var target = TrimOps(req[1..]);
                var c = CompareVersion(version, target);
                if (op == '=' && c == 0)
                    return true;
                if (op == '>')
                    if (req.StartsWith(">=") ? c >= 0 : c > 0)
                        return true;
                if (op == '<')
                    if (req.StartsWith("<=") ? c <= 0 : c < 0)
                        return true;
                if (op == '~' && c >= 0)
                    return true;
                if (op == '^' && c >= 0)
                    return true;
                continue;
            }
            if (CompareVersion(version, TrimOps(req)) == 0)
                return true;
        }
        return false;
    }

    static bool MavenSatisfies(string version, string range)
    {
        var lowerIncl = range[0] == '[';
        var comma = range.IndexOf(',');
        var upperIncl = range[^1] == ']';
        var lowerText = trimBound(comma < 0 ? range[1..^1] : range[1..comma]);
        var upperText = comma < 0 ? "" : range[(comma + 1)..^1];
        var okLower = lowerText.Length == 0 || BoundOk(version, TrimOps(lowerText), lowerIncl, true);
        var okUpper = upperText.Length == 0 || BoundOk(version, TrimOps(upperText), upperIncl, false);
        return okLower && okUpper;

        static bool BoundOk(string v, string target, bool inclusive, bool lower)
        {
            var c = CompareVersion(v, target);
            return lower ? (inclusive ? c >= 0 : c > 0) : (inclusive ? c <= 0 : c < 0);
        }
    }

    static string TrimOps(string s)
    {
        var t = s.Trim();
        while (t.Length > 0 && (t[0] is '=' or '>' or '<' or '~' or '^'))
            t = t[1..].TrimStart();
        return t.Replace("+", "");
    }

    static string trimBound(string s) => s.Trim();

    // 只比较主/次/修订三段的数字大小，忽略预发布与构建后缀，足够覆盖常见依赖区间。
    static int CompareVersion(string a, string b)
    {
        var ax = Numbers(a);
        var bx = Numbers(b);
        for (var i = 0; i < Math.Max(ax.Count, bx.Count); i++)
        {
            var av = i < ax.Count ? ax[i] : 0;
            var bv = i < bx.Count ? bx[i] : 0;
            if (av != bv)
                return av.CompareTo(bv);
        }
        return 0;
    }

    static List<int> Numbers(string v)
    {
        var result = new List<int>();
        foreach (Match m in Regex.Matches(v, @"\d+"))
            result.Add(int.TryParse(m.Value, out var n) ? n : 0);
        return result;
    }
}