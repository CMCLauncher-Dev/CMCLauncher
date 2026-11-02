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
