using System.Formats.Tar;
using System.IO.Compression;

namespace CMCLauncher;

// 运行时归档（JRE 等）的安全解压器。
// 支持 ZIP 与 gzip 压缩的 tar 两种格式，仅解压到全新的暂存目录，
// 并限制解压总大小与条目数，防止 zip-slip / 解压炸弹。
public static class RuntimeArchive
{
    // 仅解压到新的暂存目录。符号链接在普通文件全部写入之后再解析，
    // 因此归档无法把后续写入重定向到该目录之外。
    public static async Task Extract(
        string archive,
        string destination,
        bool gzip,
        long limit,
        CancellationToken ct
    )
    {
        if (
            Directory.Exists(destination)
            && (
                File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)
                || Directory.EnumerateFileSystemEntries(destination).Any()
            )
        )
            throw new IOException("Runtime extraction requires an empty staging directory.");
        Directory.CreateDirectory(destination);
        long expanded = 0;
        var entries = 0;
        // 累计解压大小，超出 limit 则中止。
        void Count(long length)
        {
            if (length < 0 || length > limit - expanded)
                throw new IOException("Runtime archive exceeds the extraction limit.");
            expanded += length;
        }
        if (!gzip)
        {
            // ZIP 格式：逐条解压，拒绝符号链接条目。
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                if (++entries > 100000)
                    throw new IOException("Too many runtime archive entries.");
                ct.ThrowIfCancellationRequested();
                var path = Store.SafePath(destination, entry.FullName.TrimEnd('/'));
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new IOException("Symbolic links in ZIP runtimes are unsupported.");
                Count(entry.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var output = new FileStream(path, FileMode.CreateNew);
                await using var input = entry.Open();
                await input.CopyToAsync(output, ct);
            }
            return;
        }
        // gzip tar 格式：先写入普通文件，收集符号链接待最后统一创建。
        var links = new List<(string Path, string Target)>();
        await using var file = File.OpenRead(archive);
        await using var stream = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(stream);
        while (await tar.GetNextEntryAsync(false, ct) is { } entry)
        {
            if (++entries > 100000)
                throw new IOException("Too many runtime archive entries.");
            ct.ThrowIfCancellationRequested();
            var relative = entry.Name.TrimEnd('/');
            while (relative.StartsWith("./", StringComparison.Ordinal))
                relative = relative[2..];
            if (relative is "" or ".")
                continue;
            var path = Store.SafePath(destination, relative);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(path);
                continue;
            }
            // 符号链接：校验目标仍在 destination 内，记录待最后创建。
            if (entry.EntryType == TarEntryType.SymbolicLink)
            {
                var target = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(path)!, entry.LinkName)
                );
                _ = Store.SafePath(destination, Path.GetRelativePath(destination, target));
                links.Add((path, entry.LinkName));
                continue;
            }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new IOException("Unsupported runtime archive entry.");
            Count(entry.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var output = new FileStream(path, FileMode.CreateNew))
                if (entry.DataStream != null)
                    await entry.DataStream.CopyToAsync(output, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
        }
        // 普通文件全部写入后，再统一创建符号链接。
        foreach (var link in links)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(link.Path)!);
            File.CreateSymbolicLink(link.Path, link.Target);
        }
    }
}
