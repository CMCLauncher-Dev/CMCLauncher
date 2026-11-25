using CMCLauncher;

static class InstanceTransferTests
{
    public static async Task Run(string temporary)
    {
        var sender = new Store(Path.Combine(temporary, "sender"));
        var receiver = new Store(Path.Combine(temporary, "receiver"));
        receiver.Config.Settings.GameLanguage = "en_us";
        using var core = new LauncherCore(receiver);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stale = new Instance
        {
            Name = "Better MTR 1.20.1 Fabric",
            GameVersion = "Better MTR 1.20.1 Fabric",
            Loader = "fabric",
            ImportRoot = sender.Library,
            VersionId = "Better MTR 1.20.1 Fabric",
        };
        if (
            InstanceTransfer.SelectionError(stale)
            != "The imported version file is missing. Repair or reimport this instance."
        )
            throw new Exception("Stale import was not diagnosed before pairing.");

        // Install from a cached vanilla profile, without external downloads.
        const string version = "1.21.1";
        var cached = Path.Combine(receiver.MinecraftRoot, "versions", version);
        Directory.CreateDirectory(cached);
        await File.WriteAllTextAsync(
            Path.Combine(cached, version + ".json"),
            """{"id":"1.21.1","libraries":[]}"""
        );

        var instance = new Instance
        {
            Name = " Transfer sample ",
            GameVersion = version,
            LoaderVersion = "",
        };
        var game = sender.GameDirectory(instance);
        Directory.CreateDirectory(Path.Combine(game, "saves", "world"));
        Directory.CreateDirectory(Path.Combine(game, "mods"));
        await File.WriteAllTextAsync(Path.Combine(game, "saves", "world", "level.dat"), "world");
        await File.WriteAllTextAsync(Path.Combine(game, "mods", "excluded.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(game, "options.txt"), "lang:zh_cn");

        var second = new Instance { Name = instance.Name, GameVersion = version };
        var secondGame = sender.GameDirectory(second);
        Directory.CreateDirectory(Path.Combine(secondGame, "mods"));
        await File.WriteAllTextAsync(Path.Combine(secondGame, "mods", "included.jar"), "other mod");
        await File.WriteAllTextAsync(Path.Combine(secondGame, "options.txt"), "not selected");

        var ready = new TaskCompletionSource<(string Address, string Code)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sending = InstanceTransfer.Send(
            sender,
            [new(instance, ["saves", "options.txt"]), new(second, ["mods"])],
            false,
            (addresses, code) => ready.TrySetResult((addresses, code)),
            timeout.Token
        );
        try
        {
            var (addresses, code) = await ready.Task.WaitAsync(timeout.Token);
            var port = addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split(':')[1];
            var batch = await InstanceTransfer.Receive(
                receiver,
                core,
                "127.0.0.1:" + port,
                code,
                () => { },
                timeout.Token
            );
            if (batch.Count != 2)
                throw new Exception("Expected both selected instances in one transfer.");
            var imported = batch[0];
            var other = batch[1];
            var destination = receiver.GameDirectory(imported);
            var otherDestination = receiver.GameDirectory(other);
            var saved = new Store(receiver.Root).Config.Instances;
            if (
                imported.Id == instance.Id
                || await File.ReadAllTextAsync(
                    Path.Combine(destination, "saves", "world", "level.dat")
                ) != "world"
                || await File.ReadAllTextAsync(Path.Combine(destination, "options.txt"))
                    != "lang:zh_cn"
                || File.Exists(Path.Combine(destination, "mods", "excluded.jar"))
                || await File.ReadAllTextAsync(
                    Path.Combine(otherDestination, "mods", "included.jar")
                ) != "other mod"
                || File.Exists(Path.Combine(otherDestination, "saves", "world", "level.dat"))
                || await File.ReadAllTextAsync(Path.Combine(otherDestination, "options.txt"))
                    == "not selected"
                || other.Name == imported.Name
                || batch.Any(x => saved.All(y => y.Id != x.Id))
            )
                throw new Exception(
                    "Instance transfer did not preserve selected content and register the new instance."
                );
            Console.WriteLine(
                "PASS one paired transfer preserves each instance selection and persists both instances"
            );
        }
        finally
        {
            timeout.Cancel();
            try
            {
                await sending;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
    }
}
