using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CMCLauncher.Core.Minecraft.DataPacks;

// 数据包模板选择（Flags）：可组合勾选要生成的示例条目类型。
[Flags]
public enum DataPackTemplates
{
    None = 0,
    Recipe = 1,
    LootTable = 2,
    Advancement = 4,
    Functions = 8,
    All = Recipe | LootTable | Advancement | Functions,
}

// 数据包生成选项：名称、游戏版本、描述、命名空间，
// 以及要生成的模板类型、自定义条目列表和示例条目使用的物品 ID。
public sealed record DataPackOptions(
    string Name,
    string GameVersion,
    string Description = "",
    string Namespace = "my_pack"
)
{
    public DataPackTemplates Templates { get; init; }
    public IReadOnlyList<DataPackEntry> Entries { get; init; } = Array.Empty<DataPackEntry>();
    public string Ingredient { get; init; } = "minecraft:cobblestone";
    public string Result { get; init; } = "minecraft:stone";
    public string LootItem { get; init; } = "minecraft:apple";
}

// 数据包生成器（partial）：本部分负责按选项生成全部文件内容（Preview）、
// 写入目录（CreateDataPack）和导出 ZIP（ExportZip）。
public static partial class DataPackGenerator
{
    /// <summary>返回用于预览和导出的精确 UTF-8 文件内容（相对路径 → 文本）；不执行任何 IO。</summary>
    public static IReadOnlyDictionary<string, string> Preview(DataPackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        if ((options.Templates & ~DataPackTemplates.All) != 0)
            throw new ArgumentException("Invalid data pack template selection.");
        var (major, minor) = GetDataPackFormat(options.GameVersion);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        void Json(string path, object value) =>
            files.Add(
                path,
                JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true })
                    + "\n"
            );
        Json(
            "pack.mcmeta",
            new
            {
                pack = major >= 82
                    ? (object)
                        new
                        {
                            description = options.Description,
                            min_format = new[] { major, minor },
                            max_format = new[] { major, minor },
                        }
                    : new { pack_format = major, description = options.Description },
            }
        );
        // 1.21 起注册表目录改为单数命名；1.21.2 起配方材料对象简化为物品 ID
        string Folder(string singular, string plural) => major >= 48 ? singular : plural;
        var root = "data/" + options.Namespace + "/";
        if (options.Templates.HasFlag(DataPackTemplates.Recipe))
        {
            ValidateItem(options.Ingredient);
            ValidateItem(options.Result);
            Json(
                root + Folder("recipe", "recipes") + "/example.json",
                new
                {
                    type = "minecraft:crafting_shapeless",
                    ingredients = new object[]
                    {
                        major >= 57 ? options.Ingredient : new { item = options.Ingredient },
                    },
                    result = major >= 41
                        ? (object)new { id = options.Result, count = 1 }
                        : new { item = options.Result, count = 1 },
                }
            );
        }
        if (options.Templates.HasFlag(DataPackTemplates.LootTable))
        {
            ValidateItem(options.LootItem);
            Json(
                root + Folder("loot_table", "loot_tables") + "/example.json",
                new
                {
                    pools = new[]
                    {
                        new
                        {
                            rolls = 1,
                            entries = new[]
                            {
                                new { type = "minecraft:item", name = options.LootItem },
                            },
                        },
                    },
                }
            );
        }
        if (options.Templates.HasFlag(DataPackTemplates.Advancement))
            Json(
                root + Folder("advancement", "advancements") + "/example.json",
                new
                {
                    display = new
                    {
                        icon = major >= 41
                            ? (object)new { id = "minecraft:book" }
                            : new { item = "minecraft:book" },
                        title = new { text = options.Name },
                        description = new { text = options.Description },
                        frame = "task",
                        show_toast = true,
                        announce_to_chat = false,
                        hidden = false,
                    },
                    criteria = new { example = new { trigger = "minecraft:impossible" } },
                }
            );
        if (options.Templates.HasFlag(DataPackTemplates.Functions))
        {
            var functions = Folder("function", "functions");
            files.Add(
                root + functions + "/load.mcfunction",
                "# Runs when this data pack is loaded. Add commands below.\n"
            );
            files.Add(
                root + functions + "/tick.mcfunction",
                "# Runs every game tick. Avoid expensive commands.\n"
            );
            Json(
                "data/minecraft/tags/" + functions + "/load.json",
                new { values = new[] { options.Namespace + ":load" } }
            );
            Json(
                "data/minecraft/tags/" + functions + "/tick.json",
                new { values = new[] { options.Namespace + ":tick" } }
            );
        }
        ArgumentNullException.ThrowIfNull(options.Entries);
        if (options.Entries.Count > 512)
            throw new ArgumentException("A project supports at most 512 entries.");
        var hooks = new Dictionary<string, List<string>>();
        long textBytes = files.Values.Sum(value => (long)Encoding.UTF8.GetByteCount(value));
        foreach (var entry in options.Entries)
        {
            var document = DataPackEntryEditor.Build(entry, options.GameVersion, options.Namespace);
            textBytes += Encoding.UTF8.GetByteCount(document.Value);
            if (textBytes > 32L * 1024 * 1024)
                throw new ArgumentException(
                    "The generated text files exceed the editor's 32 MB limit."
                );
            if (!files.TryAdd(document.Key, document.Value))
                throw new ArgumentException(
                    "Two entries generate the same file. Change the entry ID or directory."
                );
            if (entry.Kind != DataPackEntryKind.Function)
                continue;
            foreach (var hook in new[] { "load", "tick" })
            {
                if (
                    !entry.Fields.TryGetValue(hook, out var enabled)
                    || !DataPackEntryEditor.Boolean(enabled)
                )
                    continue;
                if (!hooks.TryGetValue(hook, out var values))
                    hooks[hook] = values = [];
                values.Add((entry.Namespace ?? options.Namespace) + ":" + entry.Id);
            }
        }
        foreach (var (hook, values) in hooks)
        {
            var path =
                "data/minecraft/tags/" + Folder("function", "functions") + "/" + hook + ".json";
            var tag = files.TryGetValue(path, out var existing)
                ? JsonNode.Parse(existing) as JsonObject
                : new JsonObject();
            if (tag == null || tag["values"] is not null and not JsonArray)
                throw new ArgumentException("Function hook tags must contain a values array.");
            var array = tag["values"] as JsonArray ?? new JsonArray();
            if (array.Parent == null)
                tag["values"] = array;
            foreach (var value in values.Distinct(StringComparer.Ordinal))
                if (
                    !array.Any(n =>
                        n is JsonValue v && v.TryGetValue<string>(out var old) && old == value
                    )
                )
                    array.Add(value);
            files[path] =
                tag.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n";
        }
        if (files.Values.Sum(value => (long)Encoding.UTF8.GetByteCount(value)) > 32L * 1024 * 1024)
            throw new ArgumentException(
                "The generated text files exceed the editor's 32 MB limit."
            );
        return new ReadOnlyDictionary<string, string>(files);
    }

    // 校验物品 ID 的资源位置语法（namespace:path）；不校验该物品是否真实存在于所选游戏/Mod 中。
    static void ValidateItem(string value)
    {
        // 只校验资源位置（namespace:path）语法，不校验该物品是否真实存在于所选游戏/Mod 中
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new ArgumentException("Enter a valid item ID, such as minecraft:stone.");
        var parts = value.Split(':');
        if (
            parts.Length != 2
            || parts.Any(string.IsNullOrEmpty)
            || parts[0]
                .Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.'))
            || parts[1]
                .Any(c =>
                    !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/')
                )
        )
            throw new ArgumentException("Enter a valid item ID, such as minecraft:stone.");
        if (value == "minecraft:air")
            throw new ArgumentException("Air cannot be used in these templates.");
    }

    // 在 parentDirectory 下创建数据包目录：先写入临时暂存目录，成功后原子移动为目标目录。
    public static string CreateDataPack(
        string parentDirectory,
        DataPackOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var files = Preview(options);
        ArgumentNullException.ThrowIfNull(parentDirectory);
        if (string.IsNullOrWhiteSpace(parentDirectory))
            throw new ArgumentException("Value cannot be empty or whitespace.", nameof(parentDirectory));
        cancellationToken.ThrowIfCancellationRequested();
        var parent = Path.GetFullPath(parentDirectory);
        var destination = Path.Combine(parent, options.Name);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("A file or directory with this pack name already exists.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".cmcl-datapack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "data", options.Namespace));
            foreach (var (path, content) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content, new UTF8Encoding(false));
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    /// <summary>将 pack.mcmeta 写入 ZIP 根目录，原子提交，绝不覆盖已存在的文件。</summary>
    public static string ExportZip(
        string destination,
        DataPackOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var files = Preview(options);
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Value cannot be empty or whitespace.", nameof(destination));
        if (
            !string.Equals(
                Path.GetExtension(destination),
                ".zip",
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ArgumentException("Choose a ZIP file for data pack export.");
        destination = Path.GetFullPath(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("The destination already exists. Choose another name.");
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            ".cmcl-datapack-" + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            using (
                var stream = new FileStream(
                    staging,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None
                )
            )
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var (path, content) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var writer = new StreamWriter(
                        archive.CreateEntry(path).Open(),
                        new UTF8Encoding(false)
                    );
                    writer.Write(content);
                }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }
}
