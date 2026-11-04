using CMCLauncher;

static class WorldBackupTests
{
    public static async Task Run(string root)
    {
        var world = Path.Combine(root, "game", "saves", "Original");
        Directory.CreateDirectory(Path.Combine(world, "region"));
        await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "level-fixture");
        await File.WriteAllTextAsync(Path.Combine(world, "region", "r.0.0.mca"), "region-fixture");
        var backups = Path.Combine(root, "launcher", "world-backups");
        Directory.CreateDirectory(backups);
        var zip = Path.Combine(backups, "Original-backup.zip");
        await WorldArchive.Export(world, zip, default);
        var restored = Path.Combine(root, "game", "saves", "Restored");
        await WorldArchive.Import(zip, restored, "Original-backup", default);
        if (
            await File.ReadAllTextAsync(Path.Combine(restored, "region", "r.0.0.mca"))
            != "region-fixture"
        )
            throw new Exception("World backup roundtrip lost region data");
        if (!File.Exists(Path.Combine(world, "level.dat")) || !File.Exists(zip))
            throw new Exception("Restoring altered source world or backup");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var pending = Path.Combine(backups, "canceled.zip");
        try
        {
            await WorldArchive.Export(world, pending, canceled.Token);
            throw new Exception("Canceled backup accepted");
        }
        catch (OperationCanceledException) { }
        if (File.Exists(pending))
            throw new Exception("Canceled backup published a partial ZIP");
    }
}
