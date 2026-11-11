using System.Formats.Tar;
using System.IO.Compression;
using CMCLauncher;

static class RuntimeArchiveTests
{
    public static async Task Run(string root)
    {
        var archive = Path.Combine(root, "runtime.tar.gz");
        await Write(archive, "jdk/bin/java", false);
        var extracted = Path.Combine(root, "runtime-ok");
        await RuntimeArchive.Extract(archive, extracted, true, 100, default);
        if (await File.ReadAllTextAsync(Path.Combine(extracted, "jdk/bin/java")) != "runtime")
            throw new Exception("Runtime contents");
        if (
            !OperatingSystem.IsWindows()
            && (
                File.GetUnixFileMode(Path.Combine(extracted, "jdk/bin/java"))
                & UnixFileMode.UserExecute
            ) == 0
        )
            throw new Exception("Runtime execute permission");
        await Rejected(() =>
            RuntimeArchive.Extract(archive, Path.Combine(root, "runtime-limit"), true, 2, default)
        );
        await Write(archive, "../outside", false);
        await Rejected(() =>
            RuntimeArchive.Extract(
                archive,
                Path.Combine(root, "runtime-traversal"),
                true,
                100,
                default
            )
        );
        await Write(archive, "jdk/link", true);
        await Rejected(() =>
            RuntimeArchive.Extract(archive, Path.Combine(root, "runtime-link"), true, 100, default)
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await RuntimeArchive.Extract(
                archive,
                Path.Combine(root, "runtime-cancel"),
                true,
                100,
                cancelled.Token
            );
            throw new Exception("Runtime cancellation ignored");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine(
            "PASS runtime TAR extraction, permissions, bounds, traversal, links and cancellation"
        );
    }

    static async Task Write(string path, string name, bool link)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        await using var writer = new TarWriter(gzip);
        var entry = new PaxTarEntry(
            link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile,
            name
        )
        {
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
        };
        if (link)
            entry.LinkName = "../../outside";
        else
            entry.DataStream = new MemoryStream("runtime"u8.ToArray());
        await writer.WriteEntryAsync(entry);
    }

    static async Task Rejected(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (IOException)
        {
            return;
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (Exception ex) when (ex.Message == "文件路径不安全")
        {
            return;
        }
        throw new Exception("Unsafe runtime archive accepted");
    }
}
