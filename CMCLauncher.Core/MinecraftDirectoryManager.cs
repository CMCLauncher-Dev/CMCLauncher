using System.Security.Cryptography;

namespace CMCLauncher;

public sealed class MinecraftDirectory
{
    public string Path { get; set; } = "";
    public string? OwnershipId { get; set; }
}

// Ownership needs both a local configuration record and a matching file. Existing
// custom folders are never adopted simply because they contain Minecraft or launcher files.
public sealed class MinecraftDirectoryManager(Store store)
{
    const string Marker = ".cmclauncher-directory";
    readonly SemaphoreSlim gate = new(1, 1);

    static string Full(string path) =>
        System
            .IO.Path.GetFullPath(path)
            .TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar
            );

    static bool Same(string a, string b) =>
        string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    static bool Inside(string path, string root) =>
        Same(path, root)
        || Full(path)
            .StartsWith(
                Full(root) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

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

    // The historical default library is inside our configuration directory and was
    // created by Store before ownership markers existed. Custom roots stay external.
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

    // Copy to a private sibling first, including across volumes. Publish the new path
    // only after every file has been verified. Cancellation never deletes the source.
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
            // Nested managed roots and imported instance directories are not owned by this operation.
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
            // Refuse to remove a source changed by another launcher during the copy.
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
                // Legacy instances used Library implicitly. Pin their root before changing it.
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
            // A failed cleanup leaves a valid migrated copy and the old source intact.
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
