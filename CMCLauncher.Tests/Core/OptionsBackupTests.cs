using System.Diagnostics;
using System.Text;
using CMCLauncher;

static class OptionsBackupTests
{
    public static void Run(string root)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            throw new Exception("Unsafe options operation was accepted.");
        }
        var store = new Store(Path.Combine(root, "store"));
        var service = new OptionsBackups(store);
        Instance Fixture(string name)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            return new Instance
            {
                Name = name,
                Directory = folder,
                OptionsBackupLimit = 2,
            };
        }
        string FileOf(Instance instance, string name = "options.txt") =>
            Path.Combine(instance.Directory!, name);
        var instance = Fixture("game");
        var manualInstance = Fixture("manual");
        Check(service.BackupNow(manualInstance) == null, "Manual backup requires options.txt.");
        File.WriteAllText(FileOf(manualInstance), "lang:zh_cn");
        var manual = service.BackupNow(manualInstance)!;
        File.WriteAllText(FileOf(manualInstance), "lang:en_us");
        var otherManual = service.BackupNow(manualInstance)!;
        Reject(() => service.Delete(instance, manual.Id));
        service.Delete(manualInstance, manual.Id);
        Check(
            manual.State == OptionsBackupState.Manual
                && service.List(manualInstance).Single().Id == otherManual.Id
                && File.ReadAllText(FileOf(manualInstance)) == "lang:en_us",
            "Deleting a manual backup must leave other snapshots and current game options intact."
        );
        Check(
            service.BeforeLaunch(instance) == null,
            "Missing options must not create an empty snapshot."
        );
        var a = new byte[] { 0xef, 0xbb, 0xbf }
            .Concat(
                Encoding.UTF8.GetBytes(
                    "resourcePacks:[\"vanilla\"]\r\nkey_key.forward:key.keyboard.w\r\nlang:zh_cn\r\n"
                )
            )
            .ToArray();
        File.WriteAllBytes(FileOf(instance), a);
        File.WriteAllText(FileOf(instance, "optionsof.txt"), "ofRenderDistanceChunks:12\n");
        var first = service.BeforeLaunch(instance)!;
        Check(
            File.ReadAllBytes(Path.Combine(service.DirectoryFor(instance), first.Id, "options.txt"))
                .SequenceEqual(a),
            "Backup changed BOM, Unicode or line endings."
        );
        var b = Encoding.UTF8.GetBytes(
            "resourcePacks:[\"file/working.zip\"]\r\nkey_key.forward:key.keyboard.up\r\n"
        );
        File.WriteAllBytes(FileOf(instance), b);
        service.Complete(instance, first.Id, 0);
        var second = service.BeforeLaunch(instance)!;
        Check(
            second.VerifiedAtStart,
            "Settings confirmed at the end of a normal session were not recognized at the next launch."
        );
        var broken = Encoding.UTF8.GetBytes("resourcePacks:[\"file/broken.zip\"]\n");
        File.WriteAllBytes(FileOf(instance), broken);
        File.WriteAllText(FileOf(instance, "optionsshaders.txt"), "shaderPack=broken.zip\n");
        service.Observe(
            instance,
            second.Id,
            "[Render thread/ERROR]: Failed to load resource packs"
        );
        service.Complete(instance, second.Id, 1);
        var recovery = service.PendingRecovery(instance)!;
        Check(
            recovery.Recommended?.Id == second.Id,
            "A known-good pre-launch snapshot was rejected after an in-game reload error."
        );
        service.Restore(instance, recovery.Recommended!.Id);
        Check(
            File.ReadAllBytes(FileOf(instance)).SequenceEqual(b),
            "Restore did not preserve exact options bytes."
        );
        Check(
            !File.Exists(FileOf(instance, "optionsshaders.txt")),
            "Files absent from the selected snapshot were not restored to their original absence."
        );
        var safety = service
            .List(instance)
            .Single(s => s.State == OptionsBackupState.BeforeRestore);
        Check(
            service.List(instance).Count <= 2 && service.PendingRecovery(instance) == null,
            "Retention or stale recovery acknowledgement failed."
        );
        service.Restore(instance, safety.Id);
        Check(
            File.ReadAllBytes(FileOf(instance)).SequenceEqual(broken)
                && File.Exists(FileOf(instance, "optionsshaders.txt")),
            "Pre-restore safety snapshot cannot undo the operation."
        );

        var repeated = Fixture("repeated-failures");
        File.WriteAllText(FileOf(repeated), "resourcePacks:[]\n");
        var healthy = service.BeforeLaunch(repeated)!;
        service.Complete(repeated, healthy.Id, 0);
        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(FileOf(repeated), "resourcePacks:[\"broken" + i + "\"]\n");
            var failed = service.BeforeLaunch(repeated)!;
            Check(
                !failed.VerifiedAtStart,
                "A modified, untested configuration was marked healthy."
            );
            service.Observe(repeated, failed.Id, "Resource reload failed");
            service.Complete(repeated, failed.Id, 1);
            Check(
                service.List(repeated).Count == 2
                    && service.PendingRecovery(repeated)?.Recommended?.Id == healthy.Id,
                "Repeated failures pruned the last confirmed-good snapshot."
            );
        }
        var pending = service.PendingRecovery(repeated)!;
        service.Acknowledge(repeated, pending.Failure.Id);
        Check(service.PendingRecovery(repeated) == null, "Dismissed failures prompt repeatedly.");

        var logs = Fixture("logs");
        File.WriteAllText(FileOf(logs), "resourcePacks:[]\n");
        Directory.CreateDirectory(Path.Combine(logs.Directory!, "logs"));
        var logPath = Path.Combine(logs.Directory!, "logs", "latest.log");
        File.WriteAllText(logPath, "[ERROR] Resource reload failed\n");
        var oldLog = service.BeforeLaunch(logs)!;
        service.Complete(logs, oldLog.Id, 7);
        Check(
            service.PendingRecovery(logs) == null,
            "A previous session's unchanged log caused a recovery prompt."
        );
        var fresh = service.BeforeLaunch(logs)!;
        File.AppendAllText(logPath, "[ERROR] Failed to reload resource packs\n");
        var restartedService = new OptionsBackups(store);
        Check(
            restartedService.PendingRecovery(logs)?.Failure.Id == fresh.Id,
            "A launcher restart lost a pending resource-reload failure."
        );
        Check(
            !OptionsBackups.IsResourceReloadFailure("[CHAT] Resource reload failed")
                && !OptionsBackups.IsResourceReloadFailure(
                    "Reloading ResourceManager: vanilla, fabric"
                )
                && !OptionsBackups.IsResourceReloadFailure("Missing mod dependency")
                && OptionsBackups.IsResourceReloadFailure(
                    "[Render thread/WARN]: Resource reload failed"
                ),
            "Resource reload detection is too broad or misses a real failure."
        );
        var healthyLog = Fixture("unrelated-crash");
        File.WriteAllText(FileOf(healthyLog), "resourcePacks:[]\n");
        var unrelated = service.BeforeLaunch(healthyLog)!;
        service.Complete(healthyLog, unrelated.Id, 9);
        Check(
            service.PendingRecovery(healthyLog) == null,
            "An unrelated nonzero exit offered options recovery."
        );

        File.WriteAllText(FileOf(repeated), "new working config");
        var laterSuccess = service.BeforeLaunch(repeated)!;
        service.Complete(repeated, laterSuccess.Id, 0);
        Check(
            service.PendingRecovery(repeated) == null,
            "An old resource failure was attributed to a later successful session."
        );
        var rollback = Fixture("rollback");
        File.WriteAllText(FileOf(rollback), "saved-primary");
        File.WriteAllText(FileOf(rollback, "optionsof.txt"), "saved-optifine");
        var rollbackPoint = service.BeforeLaunch(rollback)!;
        service.Complete(rollback, rollbackPoint.Id, 0);
        File.WriteAllText(FileOf(rollback), "live-primary");
        File.WriteAllText(FileOf(rollback, "optionsof.txt"), "live-optifine");
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(
                FileOf(rollback, "optionsof.txt"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            Reject(() => service.Restore(rollback, rollbackPoint.Id));
            Check(
                File.ReadAllText(FileOf(rollback)) == "live-primary",
                "A partially failed restore did not roll back the already replaced file."
            );
        }
        var corrupt = Fixture("corrupt");
        File.WriteAllText(FileOf(corrupt), "original");
        var snapshot = service.BeforeLaunch(corrupt)!;
        service.Complete(corrupt, snapshot.Id, 0);
        File.WriteAllText(
            Path.Combine(service.DirectoryFor(corrupt), snapshot.Id, "options.txt"),
            "tampered"
        );
        File.WriteAllText(FileOf(corrupt), "current");
        Reject(() => service.Restore(corrupt, snapshot.Id));
        Check(
            File.ReadAllText(FileOf(corrupt)) == "current",
            "Corrupt backup overwrote live options."
        );
        Reject(() => service.Restore(corrupt, "../escape"));
        var moved = new Instance { Id = corrupt.Id, Directory = Fixture("moved").Directory };
        File.WriteAllText(FileOf(moved), "other-instance");
        Reject(() => service.Restore(moved, snapshot.Id));
        Check(
            File.ReadAllText(FileOf(moved)) == "other-instance",
            "A snapshot was restored into another game directory."
        );
        var canceled = Fixture("canceled");
        File.WriteAllText(FileOf(canceled), "test");
        using (var ct = new CancellationTokenSource())
        {
            ct.Cancel();
            try
            {
                service.BeforeLaunch(canceled, ct.Token);
                throw new Exception("Options backup ignored cancellation.");
            }
            catch (OperationCanceledException) { }
        }
        Check(service.List(canceled).Count == 0, "Canceled snapshot was published.");
        var running = Fixture("running");
        File.WriteAllText(FileOf(running), "current");
        var active = service.BeforeLaunch(running)!;
        using var current = Process.GetCurrentProcess();
        service.Attach(running, active.Id, current);
        Reject(() => service.Restore(running, active.Id));
        Check(
            File.ReadAllText(FileOf(running)) == "current",
            "Restoring modified a running game's configuration."
        );
    }
}
