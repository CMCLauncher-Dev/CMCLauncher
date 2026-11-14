using System.Text.RegularExpressions;

namespace CMCLauncher;

public enum LogIssueSeverity
{
    Error,
    Warning,
}

// 每条发现记录一个类别、可直接本地化的标题（英文短语即本地化 key），以及命中的那行原文。
public sealed record LogIssue(
    string Category,
    string TitleKey,
    string Line,
    LogIssueSeverity Severity
);

// 纯文本日志分析：扫描常见崩溃/错误模式，返回按类别去重的问题清单。
// 不依赖 Store / Instance，WinUI 与 Avalonia 可以共享同一份分析逻辑。
public static class LogAnalyzer
{
    readonly record struct Rule(
        string Category,
        string TitleKey,
        LogIssueSeverity Severity,
        Regex Pattern
    );

    static readonly Rule[] Rules =
    {
        new(
            "out-of-memory",
            "Out of memory (mods may need more RAM)",
            LogIssueSeverity.Error,
            new Regex(
                @"OutOfMemoryError|GC overhead limit exceeded|Java heap space",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
        new(
            "java-mismatch",
            "Java version mismatch (a mod/library was built for another Java)",
            LogIssueSeverity.Error,
            new Regex(
                @"UnsupportedClassVersionError|class file version \d+\.\d+ .*only recognizes class file versions up to",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
        new(
            "mixin",
            "Mixin injection failure (an incompatible mod)",
            LogIssueSeverity.Error,
            new Regex(
                @"MixinApply|UnexpectedMixinError|mixin apply failed|Failed to apply mixin",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
        new(
            "missing-dependency",
            "Missing mod or required dependency",
            LogIssueSeverity.Error,
            new Regex(
                @"ModResolutionException|Missing or unsupported mandatory dependencies|Not enough mods available|(dependency|mod)( file)? .*( is )?(missing|not found|not present)|NoClassDefFoundError|NoSuchMethodError|NoSuchFieldError",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
        new(
            "mod-load",
            "A mod failed to load (check the error above)",
            LogIssueSeverity.Error,
            new Regex(
                @"\[FATAL\]|ModLoadingException|Failed to load mod|LoadingModList.* (ERROR|FAILED)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
        new(
            "fatal",
            "JVM reported a fatal error (crash report saved)",
            LogIssueSeverity.Error,
            new Regex(
                @"A fatal error has been detected|SIGSEGV|hs_err_pid|SIGTERM|SIGFPE|SIGABRT",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
    };

    const int MaxFindingsPerCategory = 3;

    public static IReadOnlyList<LogIssue> Analyze(string log)
    {
        var findings = new List<LogIssue>();
        if (string.IsNullOrWhiteSpace(log))
            return findings;
        var counts = new Dictionary<string, int>();
        var seen = new HashSet<string>();
        foreach (var raw in log.Split('\n'))
        {
            for (var r = 0; r < Rules.Length; r++)
            {
                var rule = Rules[r];
                if (!rule.Pattern.IsMatch(raw))
                    continue;
                if (!counts.TryGetValue(rule.Category, out var n))
                    n = 0;
                if (n >= MaxFindingsPerCategory)
                    continue;
                counts[rule.Category] = n + 1;
                var cleaned = Regex.Replace(raw.Trim(), @"\s+", " ");
                if (cleaned.Length > 220)
                    cleaned = cleaned[..220] + "…";
                if (!seen.Add(rule.Category + "|" + cleaned))
                    continue;
                findings.Add(new LogIssue(rule.Category, rule.TitleKey, cleaned, rule.Severity));
            }
        }
        return findings;
    }
}