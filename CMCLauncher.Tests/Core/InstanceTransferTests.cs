using CMCLauncher;

static class InstanceTransferTests
{
    public static async Task Run(string temporary)
    {
        var sender = new Store(Path.Combine(temporary, "sender"));
        var receiver = new Store(Path.Combine(temporary, "receiver"));
        receiver.Config.Settings.GameLanguage = "en_us";
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

        const string version = "1.21.1";

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

        var second = new Instance
        {
            Name = instance.Name,
            GameVersion = version,
            Loader = "neoforge",
            LoaderVersion = "21.1.249",
        };
        var secondGame = sender.GameDirectory(second);
        Directory.CreateDirectory(Path.Combine(secondGame, "mods"));
        await File.WriteAllTextAsync(Path.Combine(secondGame, "mods", "included.jar"), "other mod");
        await File.WriteAllTextAsync(Path.Combine(secondGame, "options.txt"), "not selected");

        var ready = new TaskCompletionSource<(string Address, string Code)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var verification = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var approved = false;
        var interrupt = false;
        var sentProgress = new List<(long Transferred, long Total)>();
        var receivedProgress = new List<(long Transferred, long Total)>();
        var sending = InstanceTransfer.Send(
            sender,
            [new(instance, ["saves", "options.txt"]), new(second, ["mods"])],
            false,
            (addresses, code) => ready.TrySetResult((addresses, code)),
            timeout.Token,
            async (_, senderVerification, ct) =>
            {
                if (await verification.Task.WaitAsync(ct) != senderVerification)
                    throw new Exception(
                        "The sender and receiver displayed different verification codes."
                    );
                if (receiver.Config.Instances.Count != 0)
                    throw new Exception("The receiver imported content before approval.");
                return approved;
            },
            (transferred, total) =>
            {
                if (interrupt)
                    throw new IOException("Simulated disconnect before the archive is written.");
                sentProgress.Add((transferred, total));
            }
        );
        try
        {
            var (addresses, code) = await ready.Task.WaitAsync(timeout.Token);
            var port = addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split(':')[1];
            var endpoint = "127.0.0.1:" + port;
            var wrongCode = (code[0] == '0' ? "1" : "0") + code[1..];
            try
            {
                await InstanceTransfer.Receive(
                    receiver,
                    endpoint,
                    wrongCode,
                    () => { },
                    timeout.Token
                );
                throw new Exception("The receiver accepted a pairing code for a different sender.");
            }
            catch (IOException ex)
                when (ex.Message
                    == "The pairing code does not match this sender. Copy its current pairing code."
                ) { }

            try
            {
                await InstanceTransfer.RequestReceive(
                    receiver,
                    endpoint,
                    value => verification.TrySetResult(value),
                    () => { },
                    timeout.Token
                );
                throw new Exception("A declined request was allowed to receive data.");
            }
            catch (IOException ex)
                when (ex.Message == "The sending device declined the connection request.") { }
            verification = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            approved = true;
            interrupt = true;
            try
            {
                await InstanceTransfer.RequestReceive(
                    receiver,
                    endpoint,
                    value => verification.TrySetResult(value),
                    () => { },
                    timeout.Token
                );
                throw new Exception("A truncated transfer was accepted.");
            }
            catch (IOException ex)
                when (ex.Message.StartsWith(
                        "Could not receive the complete archive.",
                        StringComparison.Ordinal
                    )
                ) { }
            if (receiver.Config.Instances.Count != 0 || sending.IsCompleted)
                throw new Exception(
                    "An incomplete archive must not be imported or marked as sent."
                );
            verification = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            interrupt = false;
            var batch = await InstanceTransfer.RequestReceive(
                receiver,
                endpoint,
                value => verification.TrySetResult(value),
                () => { },
                timeout.Token,
                (transferred, total) => receivedProgress.Add((transferred, total))
            );
            await sending;
            var total = sentProgress.Last().Total;
            if (
                total <= 0
                || sentProgress.First() != (0L, total)
                || receivedProgress.First() != (0L, total)
                || sentProgress.Last() != (total, total)
                || receivedProgress.Last() != (total, total)
            )
                throw new Exception(
                    "Both endpoints must report progress from zero through the full archive size."
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
                || Path.GetFileName(destination) != imported.Id
                || !saved.Single(x => x.Id == imported.Id).TransferredFromDevice
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
                || File.Exists(Path.Combine(otherDestination, "options.txt"))
                || !other.PendingTransferredLoaderSetup
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
        var forge = new Instance
        {
            GameVersion = "1.20.1",
            Loader = "forge",
            LoaderVersion = "47.4.0",
        };
        if (LauncherCore.TransferredLoaderBuild(forge) != "1.20.1-47.4.0")
            throw new Exception("A short Forge version must expand to its Maven build.");
        forge.LoaderVersion = "1.20.1-47.4.0";
        if (LauncherCore.TransferredLoaderBuild(forge) != "1.20.1-47.4.0")
            throw new Exception("A complete Forge build must not be prefixed twice.");
        Console.WriteLine("PASS transferred Forge build normalization");
        await VerifyFailedImportReceipt(sender, receiver, instance);
        await VerifyCompleteTransfer(temporary);
    }

    static async Task VerifyCompleteTransfer(string temporary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sender = new Store(Path.Combine(temporary, "complete-sender"));
        var receiver = new Store(Path.Combine(temporary, "complete-receiver"));
        using var sourceCore = new LauncherCore(sender, new NoNetwork());
        var instance = new Instance
        {
            Name = "Offline transfer",
            GameVersion = "1.21.1",
            Loader = "neoforge",
            LoaderVersion = "21.1.249",
            VersionId = "1.21.1",
            ImportRoot = sender.Library,
        };
        var versionDir = Path.Combine(sender.Library, "versions", instance.GameVersion);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.21.1.jar"), "client", timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.21.1.json"), """
            {
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "assetIndex": { "id": "fixture", "size": 88 },
              "logging": { "client": { "file": { "id": "fixture.xml", "size": 3 } } },
              "libraries": [{
                "name": "fixture:library:1",
                "natives": { "windows": "windows-test", "linux": "linux-test" },
                "downloads": { "classifiers": {
                  "windows-test": { "path": "windows-test.jar", "size": 22 },
                  "linux-test": { "path": "linux-test.jar", "size": 22 }
                } }
              }]
            }
            """, timeout.Token);
        var libraries = Directory.CreateDirectory(Path.Combine(sender.Library, "libraries"));
        foreach (var platform in new[] { "windows", "linux" })
        {
            using var zip = System.IO.Compression.ZipFile.Open(
                Path.Combine(libraries.FullName, platform + "-test.jar"),
                System.IO.Compression.ZipArchiveMode.Create
            );
        }
        await File.WriteAllTextAsync(Path.Combine(libraries.FullName, "unrelated.jar"), "exclude", timeout.Token);
        const string hash = "a9993e364706816aba3e25717850c26c9cd0d89d";
        Directory.CreateDirectory(Path.Combine(sender.Library, "assets", "indexes"));
        Directory.CreateDirectory(Path.Combine(sender.Library, "assets", "objects", "a9"));
        Directory.CreateDirectory(Path.Combine(sender.Library, "assets", "log_configs"));
        await File.WriteAllTextAsync(Path.Combine(sender.Library, "assets", "indexes", "fixture.json"),
            """{"objects":{"fixture.txt":{"hash":"a9993e364706816aba3e25717850c26c9cd0d89d","size":3}}}""", timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(sender.Library, "assets", "objects", "a9", hash), "abc", timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(sender.Library, "assets", "log_configs", "fixture.xml"), "log", timeout.Token);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = InstanceTransfer.Send(
            sender, [new(instance, [])], false, (addresses, _) => ready.TrySetResult(addresses),
            timeout.Token, (_, _, _) => Task.FromResult(true), fullRuntime: sourceCore
        );
        try
        {
            var addresses = await ready.Task.WaitAsync(timeout.Token);
            var port = addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split(':')[1];
            await InstanceTransfer.RequestReceive(receiver, "127.0.0.1:" + port,
                _ => { }, () => { }, timeout.Token);
            var reopened = new Store(receiver.Root);
            var imported = reopened.Config.Instances.Single();
            var runtime = Path.Combine(reopened.GameDirectory(imported), InstanceTransfer.RuntimeFolder);
            if (!imported.HasTransferredRuntime || imported.PendingTransferredLoaderSetup
                || !File.Exists(Path.Combine(runtime, "libraries", "windows-test.jar"))
                || !File.Exists(Path.Combine(runtime, "libraries", "linux-test.jar"))
                || File.Exists(Path.Combine(runtime, "libraries", "unrelated.jar")))
                throw new Exception("Complete migration did not preserve its private cross-platform runtime.");
            using var reopenedCore = new LauncherCore(reopened, new NoNetwork());
            await reopenedCore.DownloadGame(imported, timeout.Token);
            await sending;
            Console.WriteLine("PASS complete migration prepares the imported game with all network requests denied");
        }
        finally
        {
            timeout.Cancel();
            await sending;
        }
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new IOException("Unexpected transfer test download: " + request.RequestUri);
    }

    static async Task VerifyFailedImportReceipt(Store sender, Store receiver, Instance instance)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ready = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sending = InstanceTransfer.Send(
            sender,
            [new(instance, ["saves"])],
            false,
            (addresses, _) => ready.TrySetResult(addresses),
            timeout.Token,
            (_, _, _) => Task.FromResult(true)
        );
        try
        {
            var addresses = await ready.Task.WaitAsync(timeout.Token);
            var port = addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split(':')[1];
            try
            {
                await InstanceTransfer.RequestReceive(
                    receiver,
                    "127.0.0.1:" + port,
                    _ => { },
                    () => throw new IOException("Simulated receiver import failure."),
                    timeout.Token
                );
                throw new Exception("The import should fail.");
            }
            catch (IOException ex) when (ex.Message == "Simulated receiver import failure.") { }

            try
            {
                await sending;
                throw new Exception(
                    "The sender reported success after the receiver failed to import."
                );
            }
            catch (IOException ex)
                when (ex.Message == "The receiving device could not import the archive.") { }
            Console.WriteLine("PASS receiver import failure sends a failure receipt to the sender");
        }
        finally
        {
            timeout.Cancel();
            // Observe the sender task if the receiver failed before the receipt check.
            try
            {
                await sending;
            }
            catch (IOException) { }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
    }
}
