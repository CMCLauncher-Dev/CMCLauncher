using System.Security.Cryptography;
using System.Text;

namespace CMCLauncher;

// CMS caches contain parsed public data or images, never session HTML, cookies or tokens.
public sealed class CmsCache(string root, long capacity = 128L * 1024 * 1024)
{
    static readonly object Gate = new();

    string FileFor(string key) =>
        Store.SafePath(
            root,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".cache"
        );

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

    public void Clean()
    {
        lock (Gate)
        {
            if (Directory.Exists(root))
                CleanLocked(true);
        }
    }

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

[System.Text.Json.Serialization.JsonSerializable(typeof(CmsPage))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CmsDetail))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal partial class CmsJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
