using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CMCLauncher;

// 元数据按内容哈希落盘，相同文件换名或在不同实例中出现时可以复用识别结果。
public static class ContentInspectionCache
{
    // 缓存的识别结果条目，按内容哈希落盘。落盘落盘！！——fuzhu
    internal sealed record Entry(
        string Name,
        string Version,
        string Loader,
        string Description,
        bool Recognized
    );

    // 识别结果：内容项 + 可选图标字节。
    public sealed record Result(ContentItem Item, byte[]? Icon);

    // 待识别任务：同一内容的并发请求共用一次识别。
    static readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> Pending = new(
        StringComparer.Ordinal
    );
    // 图标字节缓存，按内容哈希索引。
    static readonly ConcurrentDictionary<string, byte[]> Icons = new(StringComparer.Ordinal);
    // 文件指纹（长度 + 修改时间 + 哈希），用于跳过重复哈希计算。
    static readonly ConcurrentDictionary<
        string,
        (long Length, long Modified, string Hash)
    > Fingerprints = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    // 运行期常驻缓存无界增长会导致内存持续攀升，达到阈值后整体收敛以限定内存上界。
    const int MaxEntries = 4096;
    // 累计识别次数（用于统计）。
    public static int InspectionCount;

    // 各缓存超过阈值时整体清空，限定内存上界。
    public static void Tighten()
    {
        if (Pending.Count > MaxEntries)
            Pending.Clear();
        if (Icons.Count > MaxEntries)
            Icons.Clear();
        if (Fingerprints.Count > MaxEntries)
            Fingerprints.Clear();
    }

    // 清空所有内存缓存（不删除磁盘文件）。
    public static void ClearMemory()
    {
        Pending.Clear();
        Icons.Clear();
        Fingerprints.Clear();
    }

    // 异步获取内容识别结果（目录直接识别，文件走哈希缓存）。
    public static Task<Result> GetAsync(string file, string kind, bool enabled, string cacheRoot) =>
        Task.Run(() => GetCore(file, kind, enabled, cacheRoot));

    static async Task<Result> GetCore(string file, string kind, bool enabled, string cacheRoot)
    {
        // 已解包的整合包可能在不改变目录时间戳的情况下修改其中的单个文件。
        if (Directory.Exists(file))
            return new Result(
                await Task.Run(() => ContentManager.Inspect(file, kind, enabled)),
                null
            );
        // 以 kind + 内容哈希作为缓存键，元数据与图标分别落盘。
        var key = kind + ":" + await Hash(file);
        var directory = Path.Combine(cacheRoot, "content-metadata");
        var metadataPath = Path.Combine(directory, key.Replace(':', '-') + ".json");
        var iconPath = Path.Combine(directory, key.Replace(':', '-') + ".icon");
        // 同一内容的并发请求共用一次识别任务，失败后会移除，以便下次重试。
        var lazy = Pending.GetOrAdd(
            key,
            _ => new Lazy<Task<Entry>>(() => LoadOrInspect(file, kind, metadataPath))
        );
        try
        {
            var entry = await lazy.Value;
            var cachedIcon = Icons.GetOrAdd(key, _ => ReadIcon(iconPath) ?? []);
            var icon = cachedIcon.Length == 0 ? null : cachedIcon;
            var item = new ContentItem(
                Path.GetFileName(file),
                entry.Name,
                entry.Version,
                entry.Loader,
                entry.Description,
                enabled,
                entry.Recognized
            );
            return new Result(item, icon);
        }
        catch
        {
            Pending.TryRemove(key, out _);
            throw;
        }

        // 优先读取磁盘缓存，命中则直接返回；否则执行识别并写回缓存。
        async Task<Entry> LoadOrInspect(string source, string contentKind, string savedMetadata)
        {
            Directory.CreateDirectory(directory);
            // 尝试从磁盘读取已缓存的识别结果。
            try
            {
                if (File.Exists(savedMetadata))
                {
                    await using var saved = File.OpenRead(savedMetadata);
                    if (await JsonSerializer.DeserializeAsync<Entry>(saved) is { } value)
                        return value;
                }
            }
            catch (Exception ex)
                when (ex is IOException or JsonException or UnauthorizedAccessException) { }

            var inspected = await Task.Run(() =>
            {
                Interlocked.Increment(ref InspectionCount);
                return ContentManager.Inspect(source, contentKind, enabled);
            });
            var result = new Entry(
                inspected.Name,
                inspected.Version,
                inspected.Loader,
                inspected.Description,
                inspected.Recognized
            );
            // 识别结果先写临时文件再原子替换，避免写入中断导致缓存损坏。
            var temp = savedMetadata + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = File.Create(temp))
                    await JsonSerializer.SerializeAsync(
                        output,
                        result,
                        ContentInspectionJsonContext.Default.Entry
                    );
                File.Move(temp, savedMetadata, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            // 仅 mod 类型提取图标，限制大小 8MB 以内。
            var bytes =
                contentKind == "mod" ? await Task.Run(() => ContentManager.Icon(source)) : null;
            if (bytes is { Length: > 0 and <= 8_388_608 })
            {
                try
                {
                    await File.WriteAllBytesAsync(iconPath, bytes);
                }
                catch (IOException) { }
                Icons.TryAdd(key, bytes);
            }
            return result;
        }
    }

    // 路径、长度和修改时间未变时复用哈希，减少反复进入页面时的大文件读取。
    static async Task<string> Hash(string file)
    {
        var info = new FileInfo(file);
        var length = info.Length;
        var modified = info.LastWriteTimeUtc.Ticks;
        if (
            Fingerprints.TryGetValue(file, out var saved)
            && saved.Length == length
            && saved.Modified == modified
        )
            return saved.Hash;
        await using var stream = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        Fingerprints[file] = (length, modified, hash);
        Tighten();
        return hash;
    }

    // 从磁盘读取图标字节，文件不存在或过大时返回 null。
    static byte[]? ReadIcon(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length is > 0 and <= 8_388_608
                ? File.ReadAllBytes(path)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(ContentInspectionCache.Entry))]
internal partial class ContentInspectionJsonContext : JsonSerializerContext { }
//必须吐槽一下我真像知道咋能写出这么雷霆的玩意的——fuzhu
