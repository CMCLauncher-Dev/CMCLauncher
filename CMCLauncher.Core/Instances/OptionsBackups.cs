using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CMCLauncher;

public enum OptionsBackupState
{
    Pending,
    Healthy,
    ReloadFailed,
    Unknown,
    BeforeRestore,
}

public sealed class OptionsBackup
{
    public string Id { get; set; } = "";
    public string Binding { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public OptionsBackupState State { get; set; }
    public Dictionary<string, string> Hashes { get; set; } = [];
    public int ProcessId { get; set; }
    public long ProcessStartTicks { get; set; }
    public bool Acknowledged { get; set; }
    public bool VerifiedAtStart { get; set; }
    public Dictionary<string, string> HealthyEndHashes { get; set; } = [];
    public long LogLength { get; set; }
    public string LogPrefix { get; set; } = "";
    public string? Evidence { get; set; }
}

public sealed record OptionsRecovery(OptionsBackup Failure, OptionsBackup? Recommended);

/// <summary>Per-instance, byte-preserving options snapshots. No resource pack files are copied.</summary>
public sealed class OptionsBackups(Store store)
{
    static readonly string[] Names = ["options.txt", "optionsof.txt", "optionsshaders.txt"];
    static readonly string[] FailureMarkers =
    [
        "Resource reload failed",
        "Failed to load resource packs",
        "Failed to reload resource packs",
        "资源重载失败",
        "资源包重载失败",
    ];
    static readonly object[] Gates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    const int MaximumFileBytes = 8 * 1024 * 1024;

    string Binding(Instance instance)
    {
        var path = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(store.GameDirectory(instance))
        );
        if (OperatingSystem.IsWindows())
            path = path.ToUpperInvariant();
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instance.Id + "\n" + path)))
            .ToLowerInvariant();
    }

    public string DirectoryFor(Instance instance) =>
        Store.SafePath(store.Root, "options-backups/" + Binding(instance));

    object Gate(Instance instance) =>
        Gates[(int)(Convert.ToUInt32(Binding(instance)[..8], 16) % (uint)Gates.Length)];

    string Folder(Instance instance, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new IOException("Invalid options backup ID.");
        return Store.SafePath(DirectoryFor(instance), id);
    }

    string GameFile(Instance instance, string name) =>
        Store.SafePath(store.GameDirectory(instance), name);

    static byte[] Bytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes)
            throw new IOException("An options file exceeds the supported 8 MB size.");
        using var target = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (target.Length + read > MaximumFileBytes)
                throw new IOException("An options file exceeds the supported 8 MB size.");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static string Prefix(string path, long length)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        var bytes = new byte[(int)Math.Min(1024, length)];
        var count = stream.ReadAtLeast(bytes, bytes.Length, false);
        return Hash(bytes[..count]);
    }

    void Write(Instance instance, OptionsBackup backup)
    {
        var file = Store.SafePath(Folder(instance, backup.Id), "snapshot.json");
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(backup, Store.JsonOptions),
                new UTF8Encoding(false)
            );
            File.Move(temp, file, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    List<OptionsBackup> Read(Instance instance)
    {
        var root = DirectoryFor(instance);
        var result = new List<OptionsBackup>();
        if (!Directory.Exists(root))
            return result;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _))
                continue;
            try
            {
                var file = Store.SafePath(Folder(instance, id), "snapshot.json");
                if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024)
                    continue;
                var backup = JsonSerializer.Deserialize<OptionsBackup>(
                    File.ReadAllText(file),
                    Store.JsonOptions
                );
                if (
                    backup == null
                    || backup.Id != id
                    || backup.Binding != Binding(instance)
                    || backup.Hashes == null
                    || backup.Hashes.Count == 0
                    || (
                        !backup.Hashes.ContainsKey("options.txt")
                        && backup.State != OptionsBackupState.BeforeRestore
                    )
                    || backup.Hashes.Keys.Any(k => !Names.Contains(k))
                    || !Enum.IsDefined(backup.State)
                )
                    continue;
                result.Add(backup);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result
            .OrderByDescending(b => b.CreatedUtc)
            .ThenByDescending(b => b.Id, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<OptionsBackup> List(Instance instance)
    {
        lock (Gate(instance))
            return Read(instance);
    }

    public void Prune(Instance instance)
    {
        lock (Gate(instance))
            PruneCore(instance);
    }

    void PruneCore(Instance instance, string? retain = null)
    {
        var backups = Read(instance);
        var limit = Math.Clamp(instance.OptionsBackupLimit, 2, 100);
        var keep = new HashSet<string>();
        if (retain != null)
            keep.Add(retain);
        // Preserve the latest confirmed-good config even after repeated failed launches.
        if (
            backups.FirstOrDefault(b =>
                (b.State == OptionsBackupState.Healthy || b.VerifiedAtStart)
            ) is
            { } healthy
        )
            keep.Add(healthy.Id);
        foreach (var backup in backups)
        {
            if (keep.Count >= limit)
                break;
            keep.Add(backup.Id);
        }
        foreach (var backup in backups.Where(b => !keep.Contains(b.Id) && !Running(b)))
            Directory.Delete(Folder(instance, backup.Id), true);
    }

    OptionsBackup? Create(Instance instance, OptionsBackupState state, CancellationToken ct)
    {
        var values = Names
            .Where(name => File.Exists(GameFile(instance, name)))
            .ToDictionary(name => name, name => Bytes(GameFile(instance, name)));
        if (
            !values.ContainsKey("options.txt")
            && (state != OptionsBackupState.BeforeRestore || values.Count == 0)
        )
            return null;
        ct.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        var destination = Folder(instance, id);
        var staging = Store.SafePath(DirectoryFor(instance), ".pending-" + id);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (name, bytes) in values)
            {
                ct.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(staging, name), bytes);
            }
            var backup = new OptionsBackup
            {
                Id = id,
                Binding = Binding(instance),
                CreatedUtc = DateTimeOffset.UtcNow,
                State = state,
                Hashes = values.ToDictionary(p => p.Key, p => Hash(p.Value)),
            };
            if (state == OptionsBackupState.Pending)
            {
                bool Same(Dictionary<string, string>? other) =>
                    other != null
                    && other.Count == backup.Hashes.Count
                    && other.All(p =>
                        backup.Hashes.TryGetValue(p.Key, out var hash) && hash == p.Value
                    );
                backup.VerifiedAtStart = Read(instance)
                    .Any(b =>
                        (b.State == OptionsBackupState.Healthy && Same(b.HealthyEndHashes))
                        || (
                            (b.State == OptionsBackupState.Healthy || b.VerifiedAtStart)
                            && Same(b.Hashes)
                        )
                    );
            }
            var log = GameFile(instance, "logs/latest.log");
            if (File.Exists(log))
            {
                backup.LogLength = new FileInfo(log).Length;
                backup.LogPrefix = Prefix(log, backup.LogLength);
            }
            File.WriteAllText(
                Path.Combine(staging, "snapshot.json"),
                JsonSerializer.Serialize(backup, Store.JsonOptions),
                new UTF8Encoding(false)
            );
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            PruneCore(instance, backup.Id);
            return backup;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    public OptionsBackup? BeforeLaunch(Instance instance, CancellationToken ct = default)
    {
        lock (Gate(instance))
        {
            Reconcile(instance);
            return Create(instance, OptionsBackupState.Pending, ct);
        }
    }

    public void Attach(Instance instance, string id, Process process)
    {
        lock (Gate(instance))
        {
            var backup = Read(instance).Single(b => b.Id == id);
            backup.ProcessId = process.Id;
            try
            {
                backup.ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (InvalidOperationException) { }
            Write(instance, backup);
        }
    }

    static bool Running(OptionsBackup backup)
    {
        if (backup.ProcessId <= 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(backup.ProcessId);
            return !process.HasExited
                && (
                    backup.ProcessStartTicks == 0
                    || process.StartTime.ToUniversalTime().Ticks == backup.ProcessStartTicks
                );
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return false;
        }
    }

    public static bool IsResourceReloadFailure(string line)
    {
        if (line.Contains("[CHAT]", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var value in FailureMarkers)
            if (line.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void Observe(Instance instance, string id, string line)
    {
        if (!IsResourceReloadFailure(line))
            return;
        lock (Gate(instance))
        {
            var backup = Read(instance).FirstOrDefault(b => b.Id == id);
            if (backup == null || backup.State == OptionsBackupState.ReloadFailed)
                return;
            backup.State = OptionsBackupState.ReloadFailed;
            backup.Evidence = line.Length > 500 ? line[..500] : line;
            Write(instance, backup);
        }
    }

    string? FindFailure(Instance instance, OptionsBackup backup)
    {
        var log = GameFile(instance, "logs/latest.log");
        if (
            File.Exists(log)
            && File.GetLastWriteTimeUtc(log) >= backup.CreatedUtc.UtcDateTime.AddSeconds(-1)
        )
        {
            using var stream = new FileStream(
                log,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            var append =
                backup.LogLength > 0
                && stream.Length >= backup.LogLength
                && Prefix(log, backup.LogLength) == backup.LogPrefix;
            stream.Position = Math.Max(
                append ? backup.LogLength : 0,
                stream.Length - 2 * 1024 * 1024
            );
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
                if (IsResourceReloadFailure(line))
                    return line.Length > 500 ? line[..500] : line;
        }
        var crashRoot = GameFile(instance, "crash-reports");
        if (Directory.Exists(crashRoot))
        {
            foreach (
                var path in Directory
                    .EnumerateFiles(crashRoot, "*.txt")
                    .Where(p => File.GetLastWriteTimeUtc(p) >= backup.CreatedUtc.UtcDateTime)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(2)
            )
            {
                var text = LogTail.Read(Store.SafePath(crashRoot, Path.GetFileName(path)), 250_000);
                var line = text.Split('\n').FirstOrDefault(IsResourceReloadFailure);
                if (line != null)
                    return line.Length > 500 ? line[..500] : line;
            }
        }
        return null;
    }

    void Finish(Instance instance, OptionsBackup backup, int? exitCode)
    {
        if (backup.State != OptionsBackupState.Pending)
            return;
        var failure = FindFailure(instance, backup);
        backup.Evidence = failure;
        backup.State =
            failure != null ? OptionsBackupState.ReloadFailed
            : exitCode == 0 ? OptionsBackupState.Healthy
            : OptionsBackupState.Unknown;
        if (backup.State == OptionsBackupState.Healthy)
            backup.HealthyEndHashes = Names
                .Where(name => File.Exists(GameFile(instance, name)))
                .ToDictionary(name => name, name => Hash(Bytes(GameFile(instance, name))));
        Write(instance, backup);
    }

    public void Complete(Instance instance, string id, int? exitCode)
    {
        lock (Gate(instance))
        {
            if (Read(instance).FirstOrDefault(b => b.Id == id) is { } backup)
                Finish(instance, backup, exitCode);
            PruneCore(instance);
        }
    }

    void Reconcile(Instance instance)
    {
        foreach (
            var backup in Read(instance)
                .Where(b => b.State == OptionsBackupState.Pending && !Running(b))
        )
            Finish(instance, backup, null);
    }

    public OptionsRecovery? PendingRecovery(Instance instance)
    {
        lock (Gate(instance))
        {
            Reconcile(instance);
            var all = Read(instance);
            if (all.Any(Running))
                return null;
            var failure = all.FirstOrDefault(b => b.State != OptionsBackupState.BeforeRestore);
            if (
                failure == null
                || failure.State != OptionsBackupState.ReloadFailed
                || failure.Acknowledged
            )
                return null;
            return new(
                failure,
                all.FirstOrDefault(b =>
                    (b.State == OptionsBackupState.Healthy || b.VerifiedAtStart)
                    && b.CreatedUtc <= failure.CreatedUtc
                )
            );
        }
    }

    public void Acknowledge(Instance instance, string id)
    {
        lock (Gate(instance))
        {
            if (Read(instance).FirstOrDefault(b => b.Id == id) is not { } backup)
                return;
            backup.Acknowledged = true;
            Write(instance, backup);
        }
    }

    public void Restore(Instance instance, string id)
    {
        lock (Gate(instance))
        {
            var all = Read(instance);
            if (all.Any(Running))
                throw new IOException("Exit the game before restoring options.");
            var selected =
                all.FirstOrDefault(b => b.Id == id)
                ?? throw new IOException("The options backup no longer exists.");
            var saved = new Dictionary<string, byte[]>();
            foreach (var (name, hash) in selected.Hashes)
            {
                var bytes = Bytes(Store.SafePath(Folder(instance, id), name));
                if (!string.Equals(Hash(bytes), hash, StringComparison.Ordinal))
                    throw new IOException("Options backup integrity check failed.");
                saved[name] = bytes;
            }
            var original = Names
                .Where(name => File.Exists(GameFile(instance, name)))
                .ToDictionary(name => name, name => Bytes(GameFile(instance, name)));
            if (original.Count > 0)
                Create(instance, OptionsBackupState.BeforeRestore, default);
            var changed = new List<string>();
            void Replace(string name, Dictionary<string, byte[]> source)
            {
                var target = GameFile(instance, name);
                if (!source.TryGetValue(name, out var bytes))
                {
                    File.Delete(target);
                    return;
                }
                var temp = target + ".cmcl-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temp, bytes);
                    File.Move(temp, target, true);
                }
                finally
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
            }
            try
            {
                foreach (var name in Names)
                {
                    Replace(name, saved);
                    changed.Add(name);
                }
            }
            catch
            {
                foreach (var name in changed)
                    Replace(name, original);
                throw;
            }
            foreach (
                var failure in Read(instance)
                    .Where(b => b.State == OptionsBackupState.ReloadFailed && !b.Acknowledged)
            )
            {
                failure.Acknowledged = true;
                Write(instance, failure);
            }
        }
    }
}
