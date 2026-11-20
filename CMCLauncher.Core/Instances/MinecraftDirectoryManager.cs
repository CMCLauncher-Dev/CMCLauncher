using System.Security.Cryptography;

namespace CMCLauncher;

// 已注册的 Minecraft 目录记录：
// Path 为目录路径，OwnershipId 为所有权标识（写入目录内的标记文件）。
public sealed class MinecraftDirectory
{
    public string Path { get; set; } = "";
    public string? OwnershipId { get; set; }
}

// Minecraft 目录管理器：
// 负责注册、选择与迁移 Minecraft 数据目录。
// 所有权需要本地配置记录与目录内标记文件双重匹配；
// 已存在的自定义文件夹不会仅因包含 Minecraft 或启动器文件而被接管。
public sealed class MinecraftDirectoryManager(Store store)
{
    const string Marker = ".cmclauncher-directory";
    readonly SemaphoreSlim gate = new(1, 1);

    // 规范化路径（完整路径 + 去除末尾分隔符）。
    static string Full(string path) =>
        System
            .IO.Path.GetFullPath(path)
            .TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar
            );

    // 判断两个路径是否相同（不区分大小写）。
    static bool Same(string a, string b) =>
        string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    // 判断 path 是否位于 root 内部（或与之相同）。
    static bool Inside(string path, string root) =>
        Same(path, root)
        || Full(path)
            .StartsWith(
                Full(root) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

    // 列出全部已注册目录（配置记录 + 内置的 Library/MinecraftRoot/实例存储目录）。
    public IReadOnlyList<MinecraftDirectory> List()
    {
        var result = store.Config.MinecraftDirectories.ToList();
        foreach (
            var path in new[] { store.Library, store.MinecraftRoot }.Concat(
                store.Config.Instances.Select(store.StorageDirectory)
            )
        )
            if (!result.Any(r => Same(r.Path, path)))
                result.Add(new() { Path = Full(path) });
        return result;
    }

    // 注册历史默认 Library 目录（位于配置目录内，早于所有权标记机制创建）。
    // 自定义根目录保持外部状态，不在此处注册。
    internal bool RegisterDefaultLibrary()
    {
        if (store.Config.MinecraftDirectories.Any(r => Same(r.Path, store.Library)))
            return false;
        try
        {
            NoLinks(store.Library);
        }
        catch (IOException)
        {
            return false;
        }
        MarkCreated(store.Library);
        return true;
    }

    // 标记目录为 CMCLauncher 创建：生成所有权 ID、写入标记文件并记录到配置。
    internal MinecraftDirectory MarkCreated(string path)
    {
        var record = new MinecraftDirectory
        {
            Path = Full(path),
            OwnershipId = Guid.NewGuid().ToString("N"),
        };
        File.WriteAllText(System.IO.Path.Combine(record.Path, Marker), record.OwnershipId);
        store.Config.MinecraftDirectories.Add(record);
        return record;
    }

    // 判断目录是否可迁移：
    // 必须拥有所有权（配置记录 + 标记文件匹配）、目录存在、
    // 不在配置根目录内、且不被任何导入实例占用。
    public bool CanMigrate(MinecraftDirectory record)
    {
        try
        {
            return record.OwnershipId != null
                && store.Config.MinecraftDirectories.Contains(record)
                && Directory.Exists(record.Path)
                && File.ReadAllText(System.IO.Path.Combine(record.Path, Marker))
                    == record.OwnershipId
                && !Inside(store.Root, record.Path)
                && !store.Config.Instances.Any(i =>
                    i.ImportRoot != null
                    && (
                        Inside(i.ImportRoot, record.Path)
                        || i.Directory != null && Inside(i.Directory, record.Path)
                    )
                );
        }
        catch
        {
            return false;
        }
    }

    // 校验路径及其所有父级均非符号链接/重解析点（链接目录不可管理或迁移）。
    static void NoLinks(string path)
    {
        for (
            var cursor = Full(path);
            cursor != null;
            cursor = System.IO.Path.GetDirectoryName(cursor)
        )
            if (
                (Directory.Exists(cursor) || File.Exists(cursor))
                && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0
            )
                throw new IOException("Linked directories cannot be managed or migrated.");
    }

    // 创建新目录：
    // 校验无链接、不存在、不与已注册目录嵌套 → 创建 → 标记所有权 → 保存配置。
    // 保存失败时回滚配置记录。
    public async Task Create(string path)
    {
        path = Full(path);
        NoLinks(path);
        if (
            Directory.Exists(path)
            || File.Exists(path)
            || List().Any(r => Inside(path, r.Path) || Inside(r.Path, path))
        )
            throw new IOException("Choose a new, separate directory that does not already exist.");
        Directory.CreateDirectory(path);
        var record = MarkCreated(path);
        try
        {
            await store.Save();
        }
        catch
        {
            store.Config.MinecraftDirectories.Remove(record);
            throw;
        }
    }

    // 选择默认 Minecraft 根目录（保存失败时回滚）。
    public async Task Select(string path)
    {
        path = Full(path);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var previous = store.Config.Settings.MinecraftRoot;
        store.Config.Settings.MinecraftRoot = path;
        try
        {
            await store.Save();
        }
        catch
        {
            store.Config.Settings.MinecraftRoot = previous;
            throw;
        }
    }

    // 迁移目录到目标位置：
    // 先复制到私有暂存目录（支持跨卷），逐文件 SHA-256 校验，
    // 全部验证通过后才发布新路径。取消操作绝不删除源目录。
    // 迁移后重映射所有实例的路径引用，保存失败时回滚全部配置。
    // 返回 true 表示源目录已成功删除，false 表示迁移完成但源目录清理失败。
    public async Task<bool> Migrate(
        MinecraftDirectory record,
        string destination,
        CancellationToken ct
    )
    {
        await gate.WaitAsync(ct);
        string? stage = null;
        bool committed = false;
        try
        {
            if (!CanMigrate(record))
                throw new InvalidOperationException(
                    "Only directories created by CMCLauncher can be migrated. Imported directories stay in place."
                );
            var source = Full(record.Path);
            destination = Full(destination);
            NoLinks(source);
            NoLinks(destination);
            if (
                Directory.Exists(destination)
                || File.Exists(destination)
                || Inside(destination, source)
                || Inside(source, destination)
                || List()
                    .Any(r =>
                        !Same(r.Path, source)
                        && (Inside(destination, r.Path) || Inside(r.Path, destination))
                    )
            )
                throw new IOException("Choose a new, separate destination directory.");
            // 嵌套的受管根目录与导入的实例目录不归本次操作所有。
            if (List().Any(r => !Same(r.Path, source) && Inside(r.Path, source)))
                throw new IOException(
                    "The source contains another registered Minecraft directory."
                );
            var directories = new List<string>();
            var entries = await Task.Run(
                () =>
                {
                    var files = new List<string>();
                    var pending = new Stack<string>();
                    pending.Push(source);
                    while (pending.TryPop(out var directory))
                    {
                        ct.ThrowIfCancellationRequested();
                        NoLinks(directory);
                        directories.Add(directory);
                        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                        {
                            NoLinks(entry);
                            if (Directory.Exists(entry))
                                pending.Push(entry);
                            else
                                files.Add(entry);
                        }
                    }
                    return files;
                },
                ct
            );
            stage = destination + ".cmcl-migrating-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            foreach (var directory in directories)
                if (!Same(directory, source))
                    Directory.CreateDirectory(
                        Store.SafePath(stage, System.IO.Path.GetRelativePath(source, directory))
                    );
            var copiedHashes = new Dictionary<string, byte[]>();
            foreach (var file in entries)
            {
                ct.ThrowIfCancellationRequested();
                NoLinks(file);
                var target = Store.SafePath(stage, System.IO.Path.GetRelativePath(source, file));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                await using var input = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    true
                );
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    true
                );
                await input.CopyToAsync(output, ct);
                input.Position = 0;
                output.Position = 0;
                var hash = await SHA256.HashDataAsync(input, ct);
                if (!hash.SequenceEqual(await SHA256.HashDataAsync(output, ct)))
                    throw new IOException("A file failed migration verification.");
                copiedHashes[file] = hash;
            }
            // 拒绝删除在复制期间被其它启动器修改过的源目录。
            foreach (var file in entries)
            {
                NoLinks(file);
                await using var input = File.OpenRead(file);
                if (!copiedHashes[file].SequenceEqual(await SHA256.HashDataAsync(input, ct)))
                    throw new IOException(
                        "The source changed during migration. Close other launchers and retry."
                    );
            }
            var fileCount = await Task.Run(
                () =>
                {
                    var pending = new Stack<string>();
                    pending.Push(source);
                    var count = 0;
                    while (pending.TryPop(out var directory))
                        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                        {
                            ct.ThrowIfCancellationRequested();
                            NoLinks(entry);
                            if (Directory.Exists(entry))
                                pending.Push(entry);
                            else
                                count++;
                        }
                    return count;
                },
                ct
            );
            if (fileCount != entries.Count)
                throw new IOException(
                    "The source changed during migration. Close other launchers and retry."
                );
            ct.ThrowIfCancellationRequested();
            var oldDefault = store.Config.Settings.MinecraftRoot;
            var paths = store
                .Config.Instances.Select(i =>
                    (Instance: i, i.Directory, i.StorageRoot, i.ImportRoot, i.IconPath)
                )
                .ToArray();
            string? Remap(string? path) =>
                path != null && Inside(path, source)
                    ? Same(path, source)
                        ? destination
                        : System.IO.Path.Combine(
                            destination,
                            System.IO.Path.GetRelativePath(source, path)
                        )
                    : path;
            Directory.Move(stage, destination);
            stage = destination;
            record.Path = destination;
            foreach (var item in paths)
            {
                var i = item.Instance;
                // 旧版实例隐式使用 Library。在修改其根目录前先固定下来。
                if (i.StorageRoot == null && i.ImportRoot == null && Same(source, store.Library))
                    i.StorageRoot = destination;
                else
                    i.StorageRoot = Remap(i.StorageRoot);
                i.Directory = Remap(i.Directory);
                i.IconPath = Remap(i.IconPath);
            }
            if (Same(store.MinecraftRoot, source))
                store.Config.Settings.MinecraftRoot = destination;
            try
            {
                await store.Save();
                committed = true;
            }
            catch
            {
                record.Path = source;
                store.Config.Settings.MinecraftRoot = oldDefault;
                foreach (var item in paths)
                {
                    item.Instance.Directory = item.Directory;
                    item.Instance.StorageRoot = item.StorageRoot;
                    item.Instance.ImportRoot = item.ImportRoot;
                    item.Instance.IconPath = item.IconPath;
                }
                throw;
            }
            // 清理失败时仍保留一份有效的迁移副本，旧源目录保持原样。
            try
            {
                await Task.Run(() => Directory.Delete(source, true));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        finally
        {
            if (!committed && stage != null && Directory.Exists(stage))
                try
                {
                    Directory.Delete(stage, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            gate.Release();
        }
    }
}
