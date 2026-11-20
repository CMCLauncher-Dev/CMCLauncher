using System.Security.Cryptography;
using System.Text;

namespace CMCLauncher;

// CMS 缓存：仅存放解析后的公开数据或图片，绝不缓存会话 HTML、Cookie 或令牌。
// 文件以 key 的 SHA-256 命名，带 12 字节头（"CMS1" + 8 字节 TTL），
// 按容量上限（默认 128MB）与 30 天过期策略自动清理。
public sealed class CmsCache(string root, long capacity = 128L * 1024 * 1024)
{
    static readonly object Gate = new();

    // 由缓存 key 计算缓存文件路径（SHA-256 十六进制 + .cache）。
    string FileFor(string key) =>
        Store.SafePath(
            root,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".cache"
        );

    // 读取缓存：返回数据与是否仍在 TTL 内（Fresh）。
    // 文件不存在、超过 8MB 或读取失败时返回 null。
    public (byte[] Data, bool Fresh)? Read(string key, TimeSpan ttl)
    {
        lock (Gate)
        {
            try
            {
                var file = FileFor(key);
                if (!File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024)
                    return null;
                var bytes = File.ReadAllBytes(file);
                var header = bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("CMS1"u8);
                return (
                    header ? bytes[12..] : bytes,
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < ttl
                );
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    // 写入缓存：数据超过 8MB 则忽略；先写临时文件再原子移动，随后触发清理。
    public void Write(string key, byte[] data, TimeSpan? ttl = null)
    {
        if (data.Length > 8 * 1024 * 1024)
            return;
        lock (Gate)
        {
            Directory.CreateDirectory(root);
            var target = FileFor(key);
            var temporary = target + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    output.Write("CMS1"u8);
                    Span<byte> lifetime = stackalloc byte[8];
                    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                        lifetime,
                        (ttl ?? TimeSpan.FromMinutes(30)).Ticks
                    );
                    output.Write(lifetime);
                    output.Write(data);
                }
                File.Move(temporary, target, true);
                CleanLocked();
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    // 清理过期缓存（读取文件头中的 TTL 判断是否过期）。
    public void Clean()
    {
        lock (Gate)
        {
            if (Directory.Exists(root))
                CleanLocked(true);
        }
    }

    // 清理核心逻辑（需持有锁）：
    // 按最后写入时间排序，删除超出容量上限、超过 30 天或已过 TTL 的文件。
    void CleanLocked(bool expired = false)
    {
        var files = Directory
            .GetFiles(root, "*.cache")
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToArray();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            var age = DateTime.UtcNow - file.LastWriteTimeUtc;
            var lifetime = TimeSpan.FromMinutes(30);
            if (expired)
            {
                using var input = file.OpenRead();
                var header = new byte[12];
                if (input.Read(header) == 12 && header.AsSpan(0, 4).SequenceEqual("CMS1"u8))
                {
                    var ticks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                        header[4..]
                    );
                    if (ticks > 0)
                        lifetime = TimeSpan.FromTicks(ticks);
                }
            }
            if (total > capacity || age > TimeSpan.FromDays(30) || expired && age >= lifetime)
            {
                total -= file.Length;
                file.Delete();
            }
        }
    }
}

// CMS 缓存使用的源生成 JSON 序列化上下文。
[System.Text.Json.Serialization.JsonSerializable(typeof(CmsPage))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CmsDetail))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal partial class CmsJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
