using System.IO.Compression;

namespace CMCLauncher;

// 存档导入、导出先写临时位置，取消或失败时由 finally 清理。
public static class WorldArchive
{
    // 单次导入的条目数量上限，防止海量极小条目的压缩包拖垮解压。
    const int MaxEntries = 29000;

    // 支持 ZIP 直接放存档文件，也支持外面多包一层同名文件夹。
    public static async Task Import(
        string zipFile,
        string destination,
        string archiveRootName,
        CancellationToken ct
    )
    {
        if (Directory.Exists(destination))
            throw new IOException("存档目标目录已存在。");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".world-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            long expanded = 0;
            // 已落地的规范化相对路径（含文件与目录），重复条目拒绝而非静默覆盖。
            var laid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var archive = ZipFile.OpenRead(zipFile);
            // 遍历前先快速拒绝海量条目，避免逐条枚举的开销。
            if (archive.Entries.Count > MaxEntries)
                throw new Exception($"存档 ZIP 条目数量超过限制（{MaxEntries}）。");
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (laid.Count + 1 > MaxEntries)
                    throw new Exception($"存档 ZIP 条目数量超过限制（{MaxEntries}）。");
                // 限制解压总大小，防止解压炸弹。
                expanded += entry.Length;
                if (expanded > 2L * 1024 * 1024 * 1024)
                    throw new Exception("存档解压内容超过限制。");
                var relative = entry.FullName.Replace('\\', '/').Trim('/');
                if (
                    relative.Length == 0
                    || relative.Split('/').Any(part => part is "" or "." or "..")
                )
                    throw new Exception("存档 ZIP 包含不安全路径。");
                var parts = relative.Split('/');
                // 若 ZIP 外层多包了一层同名文件夹，则剥掉该层。
                if (
                    parts.Length > 1
                    && parts[0].Equals(archiveRootName, StringComparison.OrdinalIgnoreCase)
                )
                    relative = string.Join('/', parts.Skip(1));
                if (relative.Length == 0)
                    continue;
                // 规范化后的同名条目（文件/目录冲突或大小写冲突）去重报错。
                if (!laid.Add(relative))
                    throw new Exception($"存档 ZIP 包含重复路径：{relative}。");
                var target = Store.SafePath(staging, relative);
                if (entry.FullName.EndsWith('/'))
                    Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = entry.Open();
                    await using var output = File.Create(target);
                    await input.CopyToAsync(output, ct);
                }
            }
            // 校验 level.dat 存在，确认是有效的 Minecraft 存档后再移动到目标位置。
            if (!File.Exists(Path.Combine(staging, "level.dat")))
                throw new Exception("ZIP 中未找到 level.dat，不是有效的 Minecraft 存档。");
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    // 只导出有 level.dat 的目录，保留空文件夹，遇到联接或符号链接则停止。
    public static async Task Export(string worldFolder, string zipFile, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(worldFolder, "level.dat")))
            throw new Exception("存档缺少 level.dat。");
        var target = Path.GetFullPath(zipFile);
        // 先写临时 ZIP，成功后再原子替换到目标文件。
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(
                () =>
                {
                    using var archive = ZipFile.Open(temp, ZipArchiveMode.Create);
                    foreach (
                        var entry in Directory.EnumerateFileSystemEntries(
                            worldFolder,
                            "*",
                            SearchOption.AllDirectories
                        )
                    )
                    {
                        ct.ThrowIfCancellationRequested();
                        if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                            throw new Exception("存档包含符号链接，已停止导出。");
                        var relative = Path.GetRelativePath(worldFolder, entry).Replace('\\', '/');
                        if (Directory.Exists(entry))
                            archive.CreateEntry(relative.TrimEnd('/') + "/");
                        else
                            archive.CreateEntryFromFile(entry, relative, CompressionLevel.Optimal);
                    }
                },
                ct
            );
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
