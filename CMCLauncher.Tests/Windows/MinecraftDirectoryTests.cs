using CMCLauncher;

static class MinecraftDirectoryTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static async Task Reject(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new Exception("Unsafe directory operation was accepted");
    }

    public static async Task Run(string root)
    {
        var legacyRoot = Path.Combine(root, "legacy-config");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "library"));
        var legacy = new Store(legacyRoot);
        var legacyManager = new MinecraftDirectoryManager(legacy);
        Check(
            legacyManager.CanMigrate(legacyManager.List().Single()),
            "historical default library is recognized as launcher-created"
        );
        var reopenedLegacy = new Store(legacyRoot);
        var reopenedManager = new MinecraftDirectoryManager(reopenedLegacy);
        Check(
            reopenedManager.CanMigrate(reopenedManager.List().Single()),
            "default library ownership persists across restarts"
        );
        var store = new Store(Path.Combine(root, "config"));
        Check(
            store.Config.Settings.MinecraftDirectorySetupPending,
            "new configurations request Minecraft directory setup"
        );
        store.Config.Settings.MinecraftDirectorySetupPending = false;
        await store.Save();
        Check(
            !new Store(store.Root).Config.Settings.MinecraftDirectorySetupPending,
            "completed first-run directory setup stays dismissed"
        );
        var manager = new MinecraftDirectoryManager(store);
        var first = Path.Combine(root, "first", ".minecraft");
        var second = Path.Combine(root, "second", ".minecraft");
        await manager.Create(first);
        await manager.Create(second);
        await manager.Select(first);
        var instance = new Instance
        {
            Name = "Fixture",
            StorageRoot = first,
            Directory = Path.Combine(first, "versions", "fixture"),
        };
        store.Config.Instances.Add(instance);
        Directory.CreateDirectory(instance.Directory);
        await File.WriteAllTextAsync(
            Path.Combine(instance.Directory, "fixture.json"),
            "{\"id\":\"fixture\"}"
        );
        await manager.Select(second);
        Check(
            store.StorageDirectory(instance) == first
                && store.GameDirectory(instance) == instance.Directory,
            "Selecting a root moved an existing instance"
        );
        await manager.Select(first);
        var record = store.Config.MinecraftDirectories.Single(d => d.Path == first);
        Check(manager.CanMigrate(record), "New directory ownership was not recorded");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(external);
        var foreign = new MinecraftDirectory { Path = external };
        store.Config.MinecraftDirectories.Add(foreign);
        Check(!manager.CanMigrate(foreign), "External directory was treated as owned");
        await Reject(() =>
            manager.Migrate(foreign, Path.Combine(root, "forbidden"), CancellationToken.None)
        );
        await Reject(() => manager.Create(external));
        await Reject(() => manager.Migrate(record, second, CancellationToken.None));
        await Reject(() =>
            manager.Migrate(record, Path.Combine(first, "nested"), CancellationToken.None)
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Reject(() =>
            manager.Migrate(record, Path.Combine(root, "cancelled"), cancelled.Token)
        );
        Check(Directory.Exists(first), "Cancellation deleted the source");
        var destination = Path.Combine(root, "migrated", ".minecraft");
        Check(
            await manager.Migrate(record, destination, CancellationToken.None),
            "Migration cleanup failed"
        );
        Check(
            !Directory.Exists(first)
                && File.Exists(Path.Combine(destination, "versions", "fixture", "fixture.json")),
            "Migration lost files"
        );
        Check(
            store.MinecraftRoot == destination
                && instance.StorageRoot == destination
                && instance.Directory == Path.Combine(destination, "versions", "fixture"),
            "Instance paths were not remapped"
        );
        var reopened = new Store(store.Root);
        Check(
            reopened.MinecraftRoot == destination
                && new MinecraftDirectoryManager(reopened).CanMigrate(
                    reopened.Config.MinecraftDirectories.Single(d => d.Path == destination)
                ),
            "Directory ownership or default did not persist"
        );
        // Imported versions must prevent migration even inside an otherwise owned root.
        store.Config.Instances.Add(new Instance { ImportRoot = destination });
        Check(!manager.CanMigrate(record), "Imported root became migratable");
        store.Config.Instances.RemoveAt(store.Config.Instances.Count - 1);
        var externalRoot = Path.Combine(root, "recognized-external");
        var externalVersion = Path.Combine(externalRoot, "versions", "recognized");
        Directory.CreateDirectory(externalVersion);
        await File.WriteAllTextAsync(Path.Combine(externalVersion, "recognized.json"), """{"id":"recognized"}""");
        var externalRecord = new MinecraftDirectory { Path = externalRoot };
        store.Config.MinecraftDirectories.Add(externalRecord);
        var imported = new Instance { Name = "Recognized", ImportRoot = externalRoot, VersionId = "recognized" };
        store.Config.Instances.Add(imported);
        Check(manager.CanCopyForMigration(externalRecord) && !manager.CanRemove(externalRecord), "Recognized external installations must allow copy migration and protect in-use paths");
        var externalCopy = Path.Combine(root, "recognized-copy");
        Check(await manager.Migrate(externalRecord, externalCopy, CancellationToken.None, keepSource: true), "External directory copy did not complete");
        Check(
            imported.ImportRoot == externalCopy && Directory.Exists(externalVersion)
                && File.Exists(Path.Combine(externalCopy, "versions", "recognized", "recognized.json")),
            "Copy migration did not preserve the source and redirect the recognized instance"
        );

        var removable = Path.Combine(root, "removable");
        await manager.Create(removable);
        var unused = manager.List().Single(item => item.Path == removable);
        Check(manager.CanTrash(unused), "An unused owned directory should allow recycling");
        await manager.Remove(unused, moveToTrash: false);
        Check(Directory.Exists(removable) && !manager.List().Any(item => item.Path == removable), "Removing a directory record deleted files or left the entry visible");

        // Force configuration replacement to fail after copying; paths and source must roll back.
        var settingsFile = Path.Combine(store.Root, "settings.json");
        File.Delete(settingsFile);
        Directory.CreateDirectory(settingsFile);
        var rejected = Path.Combine(root, "save-failure");
        await Reject(() => manager.Migrate(record, rejected, CancellationToken.None));
        Check(
            Directory.Exists(destination)
                && !Directory.Exists(rejected)
                && record.Path == destination
                && store.MinecraftRoot == destination,
            "Failed configuration save did not preserve the source"
        );
    }
}
