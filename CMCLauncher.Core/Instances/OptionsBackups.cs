using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CMCLauncher;

// 选项备份状态：
// Pending=启动前快照（等待游戏退出后判定结果）、Healthy=游戏正常退出、
// ReloadFailed=资源重载失败、Unknown=异常退出、BeforeRestore=恢复前自动备份、Manual=手动备份。
public enum OptionsBackupState
{
    Pending,
    Healthy,
    ReloadFailed,
    Unknown,
    BeforeRestore,
    Manual,
}

// 单个选项备份记录：
// 保存 options.txt / optionsof.txt / optionsshaders.txt 的字节快照与 SHA-256 哈希，
// 以及关联的游戏进程信息、日志前缀（用于检测日志追加）与失败证据。
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

// 选项恢复建议：包含导致失败的备份与推荐的回滚目标（最近一次健康快照）。
public sealed record OptionsRecovery(OptionsBackup Failure, OptionsBackup? Recommended);

// 按实例的选项文件字节级快照管理：
// 启动前自动备份 options.txt 等文件，游戏退出后根据日志判定健康/失败，
// 失败时提供一键回滚到最近健康快照的能力。不复制资源包文件本身。
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

    // 计算实例绑定标识（实例 ID + 游戏目录路径的 SHA-256），用于隔离不同实例的备份。
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

    // 返回实例备份根目录。
    public string DirectoryFor(Instance instance) =>
        Store.SafePath(store.Root, "options-backups/" + Binding(instance));

    // 按绑定标识哈希选择 64 个锁槽之一，避免不同实例互相阻塞。
    object Gate(Instance instance) =>
        Gates[(int)(Convert.ToUInt32(Binding(instance)[..8], 16) % (uint)Gates.Length)];

    // 校验备份 ID 格式（32 位十六进制 GUID）并返回其目录。
    string Folder(Instance instance, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new IOException("Invalid options backup ID.");
        return Store.SafePath(DirectoryFor(instance), id);
    }

    // 获取实例游戏目录下的选项文件完整路径。
    string GameFile(Instance instance, string name) =>
        Store.SafePath(store.GameDirectory(instance), name);

    // 读取选项文件字节（最大 8MB，使用共享读避免锁定游戏正在写入的文件）。
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

    // 计算字节数组的 SHA-256 哈希。
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // 读取文件前 N 字节的哈希（用于验证日志是否被追加而非截断）。
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

    // 原子写入备份元数据（先写临时文件再移动，防止中途崩溃导致损坏）。
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

    // 读取实例全部有效备份（校验 ID、绑定、哈希键与状态枚举），按创建时间降序排列。
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

    // 列出实例全部备份（加锁）。
    public IReadOnlyList<OptionsBackup> List(Instance instance)
    {
        lock (Gate(instance))
            return Read(instance);
    }

    // 清理超出数量限制的旧备份（保留最近的健康快照与指定保留项）。
    public void Prune(Instance instance)
    {
        lock (Gate(instance))
            PruneCore(instance);
    }

    // 清理核心逻辑：
    // 保留指定项 + 最近一次健康快照 + 按时间倒序填充至上限（2~100），
    // 删除不在保留列表且未被运行中游戏占用的备份目录。
    void PruneCore(Instance instance, string? retain = null)
    {
        var backups = Read(instance);
        var limit = Math.Clamp(instance.OptionsBackupLimit, 2, 100);
        var keep = new HashSet<string>();
        if (retain != null)
            keep.Add(retain);
        // 即使多次启动失败，也保留最近一次确认良好的配置。
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

    // 创建备份：
    // 读取现有选项文件 → 写入暂存目录 → 生成元数据（含哈希与日志前缀）→
    // 原子移动到目标目录 → 清理旧备份。
    // Pending 状态下额外检查是否与已有健康快照哈希一致（标记 VerifiedAtStart）。
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

    // 启动前备份：先对账（将遗留的 Pending 状态判定为最终状态），再创建 Pending 快照。
    public OptionsBackup? BeforeLaunch(Instance instance, CancellationToken ct = default)
    {
        lock (Gate(instance))
        {
            Reconcile(instance);
            return Create(instance, OptionsBackupState.Pending, ct);
        }
    }

    // 手动备份：游戏运行中禁止操作，创建 Manual 状态快照。
    public OptionsBackup? BackupNow(Instance instance)
    {
        lock (Gate(instance))
        {
            if (Read(instance).Any(Running))
                throw new IOException("Exit the game before backing up options.");
            return Create(instance, OptionsBackupState.Manual, default);
        }
    }

    // 删除指定备份（运行中游戏占用的备份不可删除）。
    public void Delete(Instance instance, string id)
    {
        lock (Gate(instance))
        {
            var backup = Read(instance).FirstOrDefault(item => item.Id == id)
                ?? throw new IOException("Options backup no longer exists.");
            if (Running(backup))
                throw new IOException("Cannot delete a backup used by a running game.");
            var folder = Folder(instance, backup.Id);
            Directory.Delete(folder, true);
        }
    }

    // 将备份与游戏进程关联（记录 PID 与启动时间，用于后续判定进程是否仍存活）。
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

    // 判断备份关联的游戏进程是否仍在运行（通过 PID + 启动时间双重校验）。
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

    // 判断日志行是否为资源重载失败标记（排除聊天消息）。
    public static bool IsResourceReloadFailure(string line)
    {
        if (line.Contains("[CHAT]", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var value in FailureMarkers)
            if (line.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // 实时观察日志：检测到资源重载失败时立即将备份标记为 ReloadFailed 并记录证据。
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

    // 查找失败证据：
    // 先扫描 latest.log（验证日志前缀未被截断后从备份时长度处开始读取），
    // 再扫描 crash-reports 目录中最近的崩溃报告。
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

    // 完成备份判定：
    // Pending → ReloadFailed（发现失败证据）/ Healthy（退出码 0）/ Unknown（其他）。
    // Healthy 时记录退出时各选项文件的哈希（用于后续验证）。
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

    // 游戏退出后调用：完成备份判定并清理旧备份。
    public void Complete(Instance instance, string id, int? exitCode)
    {
        lock (Gate(instance))
        {
            if (Read(instance).FirstOrDefault(b => b.Id == id) is { } backup)
                Finish(instance, backup, exitCode);
            PruneCore(instance);
        }
    }

    // 对账：将遗留的 Pending 状态备份（进程已不存在）判定为最终状态。
    void Reconcile(Instance instance)
    {
        foreach (
            var backup in Read(instance)
                .Where(b => b.State == OptionsBackupState.Pending && !Running(b))
        )
            Finish(instance, backup, null);
    }

    // 获取待恢复建议：
    // 若存在未确认的 ReloadFailed 备份，返回该备份与最近一次健康快照作为回滚目标。
    public OptionsRecovery? PendingRecovery(Instance instance)
    {
        lock (Gate(instance))
        {
            Reconcile(instance);
            var all = Read(instance);
            if (all.Any(Running))
                return null;
            var failure = all.FirstOrDefault(b =>
                b.State is not (OptionsBackupState.BeforeRestore or OptionsBackupState.Manual)
            );
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

    // 确认（忽略）备份失败，不再提示恢复。
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

    // 恢复选项文件：
    // 校验备份完整性（SHA-256）→ 自动创建 BeforeRestore 备份 →
    // 逐文件替换（失败时回滚）→ 确认所有未处理的 ReloadFailed 备份。
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
