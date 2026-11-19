using System.Security.Cryptography;
using System.Text;

namespace CMCLauncher;

// 自带的语言包和主题放在 locales/Default、themes/Default，随每次发布整体替换；
// 外层目录只放用户新增或修改的文件，同名时优先于自带文件。
public static class BundledResources
{
    public const string DefaultFolder = "Default";

    static readonly Lazy<HashSet<string>> History = new(LoadHistory);

    public static string DefaultDirectory(string directory) =>
        Path.Combine(directory, DefaultFolder);

    // 自带文件在前、用户文件在后，调用方按顺序合并即可让用户文件覆盖自带文件。
    public static IEnumerable<(string Path, bool Bundled)> JsonFiles(string directory)
    {
        foreach (var (folder, bundled) in new[] { (DefaultDirectory(directory), true), (directory, false) })
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (
                var path in Directory
                    .EnumerateFiles(folder, "*.json")
                    .OrderBy(p => p, StringComparer.Ordinal)
            )
                yield return (path, bundled);
        }
    }

    // 旧版本把自带文件直接复制到外层目录，且之后从不覆盖，会一直遮住新版的 Default。
    // 内容与历史自带版本或当前 Default 完全一致的文件说明用户没改过，删除后即可回落到 Default。
    public static int RemoveStaleCopies(string directory, Action<string>? diagnostic = null)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(directory))
                return 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    if (new FileInfo(path).Length > 4_000_000)
                        continue;
                    var hash = ContentHash(File.ReadAllBytes(path));
                    var current = Path.Combine(DefaultDirectory(directory), Path.GetFileName(path));
                    if (
                        !History.Value.Contains(hash)
                        && !(File.Exists(current) && ContentHash(File.ReadAllBytes(current)) == hash)
                    )
                        continue;
                    File.Delete(path);
                    removed++;
                    diagnostic?.Invoke("Removed unmodified bundled copy: " + Path.GetFileName(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostic?.Invoke(
                        "Bundled copy cleanup skipped: " + Path.GetFileName(path) + " · " + ex.Message
                    );
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("Bundled copy cleanup unavailable: " + ex.Message);
        }
        return removed;
    }

    // 换行符、BOM 和末尾空白随签出与打包方式变化，比较前统一去掉。
    public static string ContentHash(byte[] data)
    {
        var text = new UTF8Encoding(false).GetString(data);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        text = text.Replace("\r\n", "\n").TrimEnd();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    static HashSet<string> LoadHistory()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        using var stream = typeof(BundledResources).Assembly.GetManifestResourceStream(
            "CMCLauncher.BundledHistory"
        );
        if (stream == null)
            return hashes;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 64 && !line.StartsWith('#'))
                hashes.Add(line);
        }
        return hashes;
    }
}
