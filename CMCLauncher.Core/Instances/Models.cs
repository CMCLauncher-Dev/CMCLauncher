using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// 版本目录删除被阻止的原因。None 表示可以安全删除，其余值对应 InspectVersionDeletion
// 检查出的具体风险，UI 据此提示用户并阻止删除操作。
public enum VersionDeletionBlockReason
{
    None,
    // 目标目录不在 versions/ 或 instances/ 下，不属于可回收的版本目录。
    OutsideVersionDirectory,
    // 规范化后路径越过存储根目录，或包含非法片段。
    UnsafePath,
    // 其他实例也指向同一目录，删除会破坏它们。
    SharedDirectory,
    // 其他版本档案通过 inheritsFrom 或 jar 字段引用了该版本。
    Dependency,
    // 扫描依赖时遇到无法解析的版本档案。
    UnreadableProfile,
}

// 版本目录删除检查的结果。Directory 是被检查的目录，Reason 说明能否删除，
// RelatedVersions 列出与之相关的版本名（共享或依赖时供 UI 展示）。
public sealed record VersionDeletionInspection(
    string? Directory,
    VersionDeletionBlockReason Reason,
    IReadOnlyList<string> RelatedVersions
)
{
    // 仅当检查通过（Reason == None）时返回可删除目录，否则为 null。
    public string? DeletableDirectory =>
        Reason == VersionDeletionBlockReason.None ? Directory : null;
}

// settings.json 中的全局偏好；实例和账号另存在 Configuration 的列表里。
public sealed class Settings
{
    public int CmsRequestIntervalMs { get; set; } = 1500;
    public bool MinecraftDirectorySetupPending { get; set; }
    public string FrpcVersion { get; set; } = "";
    public string FrpcConfigPath { get; set; } = "";
    public bool DebugLogging { get; set; }
    public string JavaPath { get; set; } = "";
    public List<JavaInstallation> JavaInstallations { get; set; } = [];
    public List<string> IgnoredJavaPaths { get; set; } = [];
    public int MemoryMb { get; set; } = 4096;
    public int DownloadThreads { get; set; } = 16;

    // JSON 中的值会覆盖此地区默认值，包括显式关闭的情况。
    public bool UseBmclApi { get; set; } = MicrosoftAuth.IsMainlandMachine;
    public string GameDownloadSource { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveGameDownloadSource =>
        GameDownloadSource is "mirror" or "auto" or "official" ? GameDownloadSource
        : UseBmclApi ? "mirror"
        : "official";
    public bool AutoMemory { get; set; } = true;
    public bool OptimizeMemoryBeforeLaunch { get; set; }
    public string MicrosoftClientId { get; set; } = "";
    public string Proxy { get; set; } = "";
    public string ProxyMode { get; set; } = "auto";
    public string ProxyUsername { get; set; } = "";
    public string? ProxyPasswordProtected { get; set; }
    public string ProxyBypassHosts { get; set; } = "";
    public bool ProxyBypassLocal { get; set; }
    public bool ProxyDirectFallback { get; set; } = true;
    public bool IgnoreSslErrors { get; set; }
    public string Theme { get; set; } = "dark";
    public string ColorPreset { get; set; } = "craft";
    public bool ShowScrollBars { get; set; }
    public bool MicaEffect { get; set; } = true;
    public bool ShowGlassCardBorders { get; set; } = true;
    public int GlassOpacityPercent { get; set; } = 20;

    // 液态玻璃的视觉效果风格："transparent"（纯透明，透出桌面）或
    // "aero"（模拟 Windows 7 Aero 玻璃，叠加顶部高光与毛玻璃质感）。
    // 仅当 MicaEffect 为 true 时生效。
    public string GlassStyle { get; set; } = "transparent";

    // 自定义背景图：绝对路径，非空时在内容区底层铺图；为空表示不启用。
    // 图片由启动器读取，不影响已安装模组或游戏文件。
    public string CustomBackground { get; set; } = "";
    public string CustomBackgroundFit { get; set; } = "cover";
    public double UiScale { get; set; } = 1;
    public double UiButtonScale { get; set; } = 1;
    public double UiMenuScale { get; set; } = 1;
    public string UiFontFamily { get; set; } = "Segoe UI Variable Text";
    public double UiFontSize { get; set; } = 14;
    public Dictionary<string, string> CustomLightColors { get; set; } = new();
    public Dictionary<string, string> CustomDarkColors { get; set; } = new();
    public List<CustomAppearancePreset> CustomAppearancePresets { get; set; } = [];
    public string GameLanguage { get; set; } = "auto";
    public string CustomJvmArgs { get; set; } = "";
    public string PlayerPriority { get; set; } = "normal";
    public bool PreferHighPerformanceGpu { get; set; } = true;
    public Dictionary<string, string?> ManagedGpuPreferences { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string GameWindowTitle { get; set; } = "";
    public string MinecraftRoot { get; set; } = "";
    public string DownloadMirror { get; set; } = "";
    public bool StartMaximized { get; set; }
    public string LauncherVisibility { get; set; } = "keep";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 820;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool ExpandProjectIntroduction { get; set; } = true;
    public string LauncherLanguage { get; set; } = "en_us";
    public bool CheckLauncherUpdates { get; set; } = true;
    public bool ReceiveBetaUpdates { get; set; }
    public bool ModTranslationEnabled { get; set; }
    public string ModTranslationService { get; set; } = "mymemory";
    public bool ContinuousStatsSampling { get; set; } = true;
    public string InstanceView { get; set; } = "list";
    public List<HomeCardPlacement> HomeCards { get; set; } =
    [new() { Id = "account" }, new() { Id = "game" }];
    public string HomeNote { get; set; } = "";
}

// 首页卡片在网格中的摆放位置。Width/Height 以网格单元为单位，
// 用于持久化用户拖拽调整后的首页布局。
public sealed class HomeCardPlacement
{
    public string Id { get; set; } = "";
    public int Width { get; set; } = 2;
    public int Height { get; set; } = 1;
}

// 外观预设只保存配色和排版，不携带账号、路径或下载设置。
public sealed class CustomAppearancePreset
{
    public string Name { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public string ColorPreset { get; set; } = "craft";
    public bool ShowScrollBars { get; set; }
    public double UiScale { get; set; } = 1;
    public double UiButtonScale { get; set; } = 1;
    public double UiMenuScale { get; set; } = 1;
    public string UiFontFamily { get; set; } = "Segoe UI Variable Text";
    public double UiFontSize { get; set; } = 14;
    public Dictionary<string, string> CustomLightColors { get; set; } = new();
    public Dictionary<string, string> CustomDarkColors { get; set; } = new();
}

// 一个已发现的 Java 运行时。ManagedRoot/ManagedHash 非空表示这是启动器
// 自行下载并托管的运行时（位于 runtimes/ 下），否则是系统扫描到的外部安装。
public sealed class JavaInstallation
{
    public string ManagedRoot { get; set; } = "";
    public string ManagedHash { get; set; } = "";
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public int Major { get; set; }
    public string Vendor { get; set; } = "";
    public string Architecture { get; set; } = "";
}

// Directory 是游戏目录，StorageRoot 是依赖库根目录；导入实例还保留原版本 ID。
public sealed class Instance
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "新世界";
    public string Description { get; set; } = "";
    public string? IconPath { get; set; }
    public string? StorageRoot { get; set; }
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public string? ImportRoot { get; set; }
    public string? Directory { get; set; }
    public string? VersionId { get; set; }
    public DateTime? LastPlayed { get; set; }
    public bool Favorite { get; set; }
    public bool DebugMode { get; set; }
    public bool? OptimizeMemoryBeforeLaunch { get; set; }
    public int OptionsBackupLimit { get; set; } = 10;

    public override string ToString() => $"{Name}  ·  {GameVersion} / {Loader}";
}

// 启动器账号。Type 取值："offline"（离线）、"microsoft"（微软在线）、
// "yggdrasil"（authlib-injector 外部服务器）。Uuid 是游戏内玩家 UUID，
// 用于启动参数；Key 是启动器内部用于选择/删除账号的唯一键。
public sealed class Account
{
    public string Type { get; set; } = "offline";
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string? Secret { get; set; }
    public string AuthServer { get; set; } = "";
    public string LoginName { get; set; } = "";

    // 外部服务器可能签发与其他提供商相同的 UUID。将游戏 UUID
    // 与用于选择和删除启动器账号的键分开保存。
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key =>
        Type == "yggdrasil"
            ? "yggdrasil:"
                + Convert
                    .ToHexString(
                        SHA256.HashData(
                            Encoding.UTF8.GetBytes(AuthServer + "\n" + LoginName + "\n" + Uuid)
                        )
                    )
                    .ToLowerInvariant()
            : Uuid;

    public override string ToString() =>
        $"{Name} · {(Type == "microsoft" ? "Microsoft" : Type == "yggdrasil" ? AuthServer : "离线身份")}";

    public static Account Offline(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
            throw new Exception("玩家名须为 3–16 位字母、数字或下划线");
        // 离线 UUID 要与 Minecraft 的 OfflinePlayer 命名规则一致，不能换成随机 GUID。
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 15) | 48);
        bytes[8] = (byte)((bytes[8] & 63) | 128);
        return new() { Name = name, Uuid = Convert.ToHexString(bytes).ToLowerInvariant() };
    }
}

// settings.json 的完整结构：全局设置 + 实例列表 + 账号列表 + 已注册的
// Minecraft 目录 + 当前选中的账号与实例。整个对象由 Store 负责读写。
public sealed class Configuration
{
    public Settings Settings { get; set; } = new();
    public List<Instance> Instances { get; set; } = [];
    public List<Instance> HiddenImportedInstances { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public List<MinecraftDirectory> MinecraftDirectories { get; set; } = [];
    public string? SelectedAccount { get; set; }
    public string? SelectedInstanceId { get; set; }
}

// 集中处理配置落盘和实例路径，调用方不必自行拼接新旧目录布局。
public sealed class Store
{
    readonly SemaphoreSlim saveLock = new(1, 1);
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // 通过 Add<T> 创建的 JsonNode 值，在首次保存设置之前也需要类型解析器。
        TypeInfoResolver =
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
    public string Root { get; }
    public string Library => Path.Combine(Root, "library");
    public string MinecraftRoot =>
        string.IsNullOrWhiteSpace(Config.Settings.MinecraftRoot)
            ? Library
            : Path.GetFullPath(Config.Settings.MinecraftRoot);

    public string StorageDirectory(Instance instance) =>
        instance.ImportRoot ?? instance.StorageRoot ?? Library;

    public Configuration Config { get; }

    public Store(string? root = null)
    {
        Root =
            root
            ?? DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        System.IO.Directory.CreateDirectory(Library);
        var file = Path.Combine(Root, "settings.json");
        Configuration? config = null;
        if (File.Exists(file))
        {
            try
            {
                config = JsonSerializer.Deserialize<Configuration>(
                    File.ReadAllText(file),
                    JsonOptions
                );
                if (
                    config?.Settings == null
                    || config.Instances == null
                    || config.HiddenImportedInstances == null
                    || config.HiddenImportedInstances.Any(x => x == null)
                    || config.Accounts == null
                    || config.MinecraftDirectories == null
                    || config.Instances.Any(x => x == null)
                    || config.Accounts.Any(x => x == null)
                    || config.MinecraftDirectories.Any(x => x == null)
                )
                    throw new JsonException("Invalid launcher configuration.");
            }
            catch (JsonException)
            {
                var backup = Path.Combine(
                    Root,
                    $"settings.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"
                );
                File.Move(file, backup);
                RecoveredSettingsPath = backup;
                config = null;
            }
        }
        Config = config ?? new();
        if (config == null)
        {
            Config.Settings.MinecraftDirectorySetupPending = true;
            Config.Settings.LauncherLanguage = InterfaceLanguageForSystem();
        }
        var restored = Config.HiddenImportedInstances.Where(HasReadableImportedVersion).ToArray();
        foreach (var instance in restored)
        {
            Config.HiddenImportedInstances.Remove(instance);
            if (!Config.Instances.Any(active => active.Id == instance.Id))
                Config.Instances.Add(instance);
        }
        NewlyHiddenImportedInstances = Config.Instances
            .Where(instance => instance.ImportRoot != null && !HasReadableImportedVersion(instance))
            .ToArray();
        foreach (var instance in NewlyHiddenImportedInstances)
        {
            Config.Instances.Remove(instance);
            Config.HiddenImportedInstances.Add(instance);
        }
        if (
            new MinecraftDirectoryManager(this).RegisterDefaultLibrary()
            || NewlyHiddenImportedInstances.Count > 0
            || restored.Length > 0
            || RecoveredSettingsPath != null
        )
        {
            var pending = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pending, JsonSerializer.Serialize(Config, JsonOptions));
                File.Move(pending, file, overwrite: true);
            }
            finally
            {
                File.Delete(pending);
            }
        }
    }

    public string? RecoveredSettingsPath { get; }
    public IReadOnlyList<Instance> NewlyHiddenImportedInstances { get; }
    internal IEnumerable<Instance> AllInstances => Config.Instances.Concat(Config.HiddenImportedInstances);

    static bool HasReadableImportedVersion(Instance instance)
    {
        if (instance.ImportRoot is not { } root || string.IsNullOrWhiteSpace(instance.VersionId))
            return false;
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var id = instance.VersionId;
            while (seen.Count < 10 && seen.Add(id))
            {
                var path = SafePath(root, $"versions/{id}/{id}.json");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.ValueKind != JsonValueKind.Object)
                    return false;
                if (!json.RootElement.TryGetProperty("inheritsFrom", out var parent)
                    || parent.ValueKind == JsonValueKind.Null)
                    return true;
                if (parent.ValueKind != JsonValueKind.String)
                    return false;
                id = parent.GetString()!;
                if (string.IsNullOrEmpty(id))
                    return true;
                // A missing parent may be fetched when launching a valid local profile.
                if (!File.Exists(SafePath(root, $"versions/{id}/{id}.json")))
                    return id.All(c => (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') || c is '.' or '_' or '-' or '+');
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
        return false;
    }

    public async Task ForgetHiddenImportedInstances()
    {
        var removed = Config.HiddenImportedInstances.Where(NewlyHiddenImportedInstances.Contains).ToArray();
        if (removed.Length == 0)
            return;
        var selected = Config.SelectedInstanceId;
        foreach (var instance in removed)
            Config.HiddenImportedInstances.Remove(instance);
        if (removed.Any(instance => instance.Id == selected))
            Config.SelectedInstanceId = null;
        try
        {
            await Save();
        }
        catch
        {
            Config.HiddenImportedInstances.AddRange(removed);
            Config.SelectedInstanceId = selected;
            throw;
        }
    }

    internal static string DefaultRoot(string appData)
    {
        var current = Path.Combine(appData, "CMCLauncher.Native");
        var legacy = Path.Combine(appData, "CraftLauncher.Native");
        if (System.IO.Directory.Exists(current) || !System.IO.Directory.Exists(legacy))
            return current;
        // 旧数据目录被符号链接指向外部时拒绝迁移，回退用当前目录，而不是让首次启动崩溃。
        if ((File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0)
            return current;
        var settings = Path.Combine(legacy, "settings.json");
        var pending = Path.Combine(
            legacy,
            "settings-rename-" + Guid.NewGuid().ToString("N") + ".tmp"
        );
        // 仅重写以旧数据目录为根的路径；外部游戏目录保持原位。
        void Rewrite(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    if (
                        obj[key] is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && (
                            text.Equals(legacy, StringComparison.OrdinalIgnoreCase)
                            || text.StartsWith(
                                legacy + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                        obj[key] = current + text[legacy.Length..];
                    else
                        Rewrite(obj[key]);
                }
            else if (node is JsonArray array)
                foreach (var child in array)
                    Rewrite(child);
        }
        if (File.Exists(settings))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(settings)) is JsonObject config)
                {
                    Rewrite(config);
                    File.WriteAllText(pending, config.ToJsonString(JsonOptions));
                }
            }
            catch (JsonException)
            {
                // The store will quarantine the original file after the directory is migrated.
            }
        }
        try
        {
            System.IO.Directory.Move(legacy, current);
            try
            {
                var movedPending = Path.Combine(current, Path.GetFileName(pending));
                if (File.Exists(movedPending))
                    File.Move(movedPending, Path.Combine(current, "settings.json"), true);
            }
            catch
            {
                System.IO.Directory.Move(current, legacy);
                throw;
            }
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
        return current;
    }

    // 首次启动时根据系统 UI 语言推断启动器界面语言；不支持的语言回退到英文。
    static string InterfaceLanguageForSystem()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh_cn";
        var language = name.Split('-')[0].ToLowerInvariant();
        return language switch
        {
            "ja" => "ja_jp",
            "fr" => "fr_fr",
            "es" => "es_es",
            "ru" => "ru_ru",
            "ar" => "ar_sa",
            _ => "en_us",
        };
    }

    // 多个 UI 回调可能同时保存，用锁串行执行写入。
    public async Task Save()
    {
        await saveLock.WaitAsync();
        try
        {
            await AtomicJson(
                Path.Combine(Root, "settings.json"),
                JsonSerializer.SerializeToNode(Config, JsonOptions)!
            );
        }
        finally
        {
            saveLock.Release();
        }
    }

    // 兼容导入版本的隔离目录，以及早期使用 instances/<id> 的本地布局。
    public string GameDirectory(Instance instance)
    {
        if (
            instance.ImportRoot != null
            && instance.VersionId != null
            && (
                instance.Directory == null
                || string.Equals(
                    Path.GetFullPath(instance.Directory),
                    Path.GetFullPath(instance.ImportRoot),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            var configured = LauncherInstanceFormat.GameDirectory(
                instance.ImportRoot,
                instance.VersionId
            );
            if (configured != null)
                return configured;
            var isolated = SafePath(instance.ImportRoot, "versions/" + instance.VersionId);
            if (
                new[] { "mods", "saves", "options.txt" }.Any(name =>
                    System.IO.Directory.Exists(Path.Combine(isolated, name))
                    || File.Exists(Path.Combine(isolated, name))
                )
            )
                return isolated;
        }
        var storage = StorageDirectory(instance);
        var legacy = SafePath(storage, "instances/" + instance.Id);
        return instance.Directory
            ?? (
                System.IO.Directory.Exists(legacy)
                    ? legacy
                    : SafePath(storage, "versions/" + instance.Id)
            );
    }

    // 保护共享的版本档案文件，同时允许单独回收各个导入的版本文件夹。
    public string? DeletableInstanceDirectory(Instance instance) =>
        InspectVersionDeletion(instance).DeletableDirectory;

    public VersionDeletionInspection InspectVersionDeletion(Instance instance)
    {
        var storage = Path.GetFullPath(StorageDirectory(instance))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // 导入的版本可以共享运行时根目录，同时各自拥有独立的 versions/<id> 文件夹。
        string VersionDirectory(Instance candidate)
        {
            if (
                candidate.ImportRoot != null
                && candidate.VersionId != null
                && (
                    candidate.Directory == null
                    || string.Equals(
                        Path.GetFullPath(candidate.Directory),
                        Path.GetFullPath(candidate.ImportRoot),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
                return SafePath(candidate.ImportRoot, "versions/" + candidate.VersionId);
            return Path.GetFullPath(GameDirectory(candidate));
        }
        var target = VersionDirectory(instance);
        target = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(target);
        if (
            !string.Equals(
                parent,
                Path.Combine(storage, "versions"),
                StringComparison.OrdinalIgnoreCase
            )
            && !string.Equals(
                parent,
                Path.Combine(storage, "instances"),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return new(target, VersionDeletionBlockReason.OutsideVersionDirectory, []);
        try
        {
            target = SafePath(storage, Path.GetRelativePath(storage, target));
        }
        catch
        {
            return new(target, VersionDeletionBlockReason.UnsafePath, []);
        }
        static bool Overlaps(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || left.StartsWith(
                right + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
            || right.StartsWith(
                left + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
        var shared = Config
            .Instances.Where(other =>
                other.Id != instance.Id
                && Overlaps(
                    Path.GetFullPath(VersionDirectory(other))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    target
                )
            )
            .Select(other => other.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (shared.Length > 0)
            return new(target, VersionDeletionBlockReason.SharedDirectory, shared);

        // 仅凭基础游戏版本不能证明存在依赖关系：需检查实际的版本档案引用。
        if (
            string.Equals(
                parent,
                Path.Combine(storage, "versions"),
                StringComparison.OrdinalIgnoreCase
            ) && System.IO.Directory.Exists(parent)
        )
        {
            var id = Path.GetFileName(target);
            var references = new List<string>();
            string? reading = null;
            try
            {
                foreach (var folder in System.IO.Directory.EnumerateDirectories(parent))
                {
                    if (
                        string.Equals(
                            Path.GetFullPath(folder),
                            target,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;
                    var profileId = Path.GetFileName(folder);
                    reading = profileId;
                    var profilePath = SafePath(storage, $"versions/{profileId}/{profileId}.json");
                    if (!File.Exists(profilePath))
                        continue;
                    var profile = JsonNode.Parse(File.ReadAllText(profilePath));
                    if (profile is not JsonObject)
                        throw new InvalidDataException("Invalid version profile.");
                    if (
                        string.Equals(
                            profile.S("inheritsFrom"),
                            id,
                            StringComparison.OrdinalIgnoreCase
                        ) || string.Equals(profile.S("jar"), id, StringComparison.OrdinalIgnoreCase)
                    )
                        references.Add(profileId);
                }
            }
            catch
            {
                return new(
                    target,
                    VersionDeletionBlockReason.UnreadableProfile,
                    reading == null ? [] : [reading]
                );
            }
            if (references.Count > 0)
                return new(
                    target,
                    VersionDeletionBlockReason.Dependency,
                    references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                );
        }
        return new(target, VersionDeletionBlockReason.None, []);
    }

    public void PrepareIsolatedInstance(Instance instance)
    {
        if (instance.Directory != null || instance.ImportRoot != null)
            return;
        instance.StorageRoot ??= MinecraftRoot;
        var name = instance.Name.Trim();
        var path = SafePath(StorageDirectory(instance), "versions/" + name);
        if (
            System.IO.Directory.Exists(path)
            || Config.Instances.Any(i =>
                string.Equals(GameDirectory(i), path, StringComparison.OrdinalIgnoreCase)
            )
        )
            path = SafePath(StorageDirectory(instance), "versions/" + name + "-" + instance.Id);
        instance.Directory = path;
    }

    public void EnsureUniqueInstanceName(Instance instance)
    {
        var name = instance.Name.Trim();
        if (name.Length == 0)
            throw new Exception("请填写实例名称");
        if (name.Contains('/') || name.Contains('\\'))
            throw new Exception("文件路径不安全");
        SafePath(instance.StorageRoot ?? MinecraftRoot, "versions/" + name);
        if (
            Config.Instances.Any(existing =>
                string.Equals(existing.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    Path.GetFullPath(StorageDirectory(existing))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(instance.ImportRoot ?? instance.StorageRoot ?? MinecraftRoot)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            throw new Exception("已有同名游戏版本，请更换名称后再下载：" + name);
        if (instance.ImportRoot == null && instance.Directory == null)
        {
            var path = SafePath(instance.StorageRoot ?? MinecraftRoot, "versions/" + name);
            if (System.IO.Directory.Exists(path))
                throw new Exception("游戏目录中已存在同名版本：" + name);
        }
    }

    // 临时文件放在目标旁边，写完再替换，避免留下半份 JSON。
    public static async Task AtomicJson(string file, JsonNode value)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid() + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, value.ToJsonString(JsonOptions));
            File.Move(temp, file, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // 先检查路径片段，再检查规范化后的边界；Windows 设备名和联接也不能当普通文件用。
    public static string SafePath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (
            string.IsNullOrWhiteSpace(relative)
            || Path.IsPathRooted(relative)
            || parts.Any(p =>
                p is ".." or "."
                || p.Length == 0
                || p.EndsWith('.')
                || p.EndsWith(' ')
                || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || System.Text.RegularExpressions.Regex.IsMatch(
                    p,
                    @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            )
        )
            throw new Exception("文件路径不安全");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (
            !full.StartsWith(
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new Exception("路径越过实例目录");
        // 拒绝读写路径中的目录联接和符号链接。
        for (
            var cursor = full;
            cursor != null && cursor.Length >= Path.GetFullPath(root).Length;
            cursor = Path.GetDirectoryName(cursor)
        )
            if (
                (File.Exists(cursor) || System.IO.Directory.Exists(cursor))
                && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint)
            )
                throw new Exception("内容路径包含符号链接或目录联接");
        return full;
    }
}

// 版本清单中不少字段可选，这些扩展统一处理空值和默认值。
public static class J
{
    public static string S(this JsonNode? node, string key, string fallback = "") =>
        node?[key]?.ToString() ?? fallback;

    public static IEnumerable<JsonNode> Items(this JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonNode>() : [];

    public static int Int(this JsonNode? node, string key, int fallback = 0) =>
        int.TryParse(node.S(key), out var n) ? n : fallback;
}
