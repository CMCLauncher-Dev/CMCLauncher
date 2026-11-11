using System.Formats.Tar;
using System.IO.Compression;

namespace CMCLauncher;

public static class RuntimeArchive
{
    // Extract only into a new staging directory. Links are resolved after ordinary files,
    // so an archive cannot redirect a later write outside that directory.
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
        void Count(long length)
        {
            if (length < 0 || length > limit - expanded)
                throw new IOException("Runtime archive exceeds the extraction limit.");
            expanded += length;
        }
        if (!gzip)
        {
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
        foreach (var link in links)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(link.Path)!);
            File.CreateSymbolicLink(link.Path, link.Target);
        }
    }
}
