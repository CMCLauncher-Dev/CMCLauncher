using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CMCLauncher.Core.Minecraft.DataPacks;

// 数据包条目的类型：配方（无序/有序/烹饪）、战利品表、进度、函数、标签、自定义 JSON。
public enum DataPackEntryKind
{
    ShapelessRecipe,
    ShapedRecipe,
    CookingRecipe,
    LootTable,
    Advancement,
    Function,
    Tag,
    CustomJson,
}

// 数据包编辑器中一个可填写字段的定义：键、显示标签、默认值、帮助文本、是否多行、可选值列表。
public sealed record DataPackField(
    string Key,
    string Label,
    string DefaultValue,
    string Help = "",
    bool Multiline = false,
    string[]? Choices = null
);

// 一条数据包条目：类型、资源 ID 和字段值集合。
// SourceOverride 非空时以原始 JSON/函数文本为准，忽略引导字段。
public sealed record DataPackEntry(
    DataPackEntryKind Kind,
    string Id,
    IReadOnlyDictionary<string, string> Fields
)
{
    // Empty uses guided fields. Otherwise the complete JSON/function source is user-owned.
    public string? SourceOverride { get; init; }
    public string? Namespace { get; init; }

    public override string ToString() => Id + " · " + DataPackEntryEditor.Label(Kind);
}

/// <summary>
/// 数据包条目编辑器：为两种 GUI 前端（WinUI / Avalonia）提供共享的字段定义
/// 和版本感知的文档生成逻辑。
/// </summary>
public static class DataPackEntryEditor
{
    // 返回新建数据包时的初始示例条目（无序配方、战利品表、进度、load/tick 函数）。
    public static IReadOnlyList<DataPackEntry> StarterEntries()
    {
        DataPackEntry Function(string id) =>
            Create(DataPackEntryKind.Function, id) with
            {
                Fields = new Dictionary<string, string>
                {
                    ["commands"] = "# Add commands below.\n",
                    [id] = "true",
                },
            };
        return Array.AsReadOnly(
            new[]
            {
                Create(DataPackEntryKind.ShapelessRecipe),
                Create(DataPackEntryKind.LootTable),
                Create(DataPackEntryKind.Advancement),
                Function("load"),
                Function("tick"),
            }
        );
    }

    public static string Label(DataPackEntryKind kind) =>
        kind switch
        {
            DataPackEntryKind.ShapelessRecipe => "Shapeless recipe",
            DataPackEntryKind.ShapedRecipe => "Shaped recipe",
            DataPackEntryKind.CookingRecipe => "Cooking recipe",
            DataPackEntryKind.LootTable => "Loot table",
            DataPackEntryKind.Advancement => "Advancement",
            DataPackEntryKind.Function => "Function",
            DataPackEntryKind.Tag => "Tag",
            DataPackEntryKind.CustomJson => "Custom JSON",
            _ => throw new ArgumentException("Unknown data pack entry type."),
        };

    public static IReadOnlyList<DataPackField> Fields(DataPackEntryKind kind)
    {
        var result = new List<DataPackField>();
        if (
            kind
            is DataPackEntryKind.ShapelessRecipe
                or DataPackEntryKind.ShapedRecipe
                or DataPackEntryKind.CookingRecipe
        )
        {
            result.Add(new("result", "Recipe result ID", "minecraft:stone"));
            if (kind != DataPackEntryKind.CookingRecipe)
                result.Add(
                    new(
                        "count",
                        "Result count",
                        "1",
                        "Use 1–64; the item must support this stack size."
                    )
                );
            result.Add(new("group", "Recipe book group", ""));
        }
        switch (kind)
        {
            case DataPackEntryKind.ShapelessRecipe:
                result.Add(
                    new(
                        "ingredients",
                        "Ingredients",
                        "minecraft:cobblestone",
                        "One item or #tag per line; 1–9 lines. Repeated lines require multiple items.",
                        true
                    )
                );
                break;
            case DataPackEntryKind.ShapedRecipe:
                result.Add(
                    new(
                        "pattern",
                        "Crafting pattern",
                        "AA\nAA",
                        "1–3 rows, each 1–3 characters wide. Spaces represent empty slots.",
                        true
                    )
                );
                result.Add(
                    new(
                        "keys",
                        "Pattern ingredients",
                        "A=minecraft:cobblestone",
                        "One symbol=item or symbol=#tag per line. Every pattern symbol must have a matching ingredient.",
                        true
                    )
                );
                break;
            case DataPackEntryKind.CookingRecipe:
                result.Add(
                    new(
                        "type",
                        "Cooking method",
                        "smelting",
                        Choices: ["smelting", "blasting", "smoking", "campfire_cooking"]
                    )
                );
                result.Add(
                    new(
                        "ingredient",
                        "Recipe ingredient ID",
                        "minecraft:cobblestone",
                        "An item ID or #tag."
                    )
                );
                result.Add(new("experience", "Experience", "0.1"));
                result.Add(new("time", "Cooking time (ticks)", "200", "20 ticks = 1 second."));
                break;
            case DataPackEntryKind.LootTable:
                result.Add(new("rolls", "Loot rolls", "1"));
                result.Add(
                    new(
                        "entries",
                        "Weighted loot entries",
                        "minecraft:apple,1,1\nminecraft:stick,2,3",
                        "One item,count,weight per line. Each roll selects one entry by weight.",
                        true
                    )
                );
                break;
            case DataPackEntryKind.Advancement:
                result.Add(new("title", "Title", "My advancement"));
                result.Add(new("description", "Description", "", Multiline: true));
                result.Add(new("icon", "Icon item ID", "minecraft:book"));
                result.Add(
                    new(
                        "frame",
                        "Advancement frame",
                        "task",
                        Choices: ["task", "goal", "challenge"]
                    )
                );
                result.Add(
                    new("parent", "Parent advancement ID", "", "Optional; use namespace:path.")
                );
                result.Add(
                    new(
                        "trigger",
                        "Criterion trigger",
                        "minecraft:impossible",
                        Choices:
                        [
                            "minecraft:impossible",
                            "minecraft:tick",
                            "minecraft:inventory_changed",
                        ]
                    )
                );
                result.Add(
                    new(
                        "conditions",
                        "Criterion conditions (JSON)",
                        "{}",
                        "For additional criteria, rewards or requirements, edit the full JSON below.",
                        true
                    )
                );
                result.Add(new("experience", "Reward experience", "0"));
                result.Add(new("rewardFunction", "Reward function ID", ""));
                result.Add(new("toast", "Show toast", "true", Choices: ["true", "false"]));
                result.Add(
                    new("announce", "Announce in chat", "false", Choices: ["true", "false"])
                );
                result.Add(
                    new("hidden", "Hidden advancement", "false", Choices: ["true", "false"])
                );
                break;
            case DataPackEntryKind.Function:
                result.Add(
                    new(
                        "commands",
                        "Commands",
                        "# Add one Minecraft command per line, without a leading slash.\nsay Hello!",
                        "Commands are saved, never executed by the launcher. Check command compatibility with your game version.",
                        true
                    )
                );
                result.Add(new("load", "Run when loaded", "false", Choices: ["true", "false"]));
                result.Add(
                    new(
                        "tick",
                        "Run every tick",
                        "false",
                        "Tick functions run 20 times per second. Avoid expensive commands.",
                        Choices: ["true", "false"]
                    )
                );
                break;
            case DataPackEntryKind.Tag:
                result.Add(
                    new(
                        "registry",
                        "Tag registry",
                        "item",
                        "Use a registry directory, such as item, block, function or worldgen/biome. Known legacy names are adapted to the selected version."
                    )
                );
                result.Add(
                    new(
                        "values",
                        "Tag values",
                        "minecraft:stone\nminecraft:cobblestone",
                        "One namespace:path or #namespace:tag per line.",
                        true
                    )
                );
                result.Add(
                    new("replace", "Replace existing tag", "false", Choices: ["true", "false"])
                );
                break;
            case DataPackEntryKind.CustomJson:
                result.Add(
                    new(
                        "directory",
                        "Registry directory",
                        "predicate",
                        "Relative to data/<namespace>, without a leading slash. For example: predicate, dimension, worldgen/configured_feature."
                    )
                );
                result.Add(
                    new(
                        "json",
                        "JSON document",
                        "{}",
                        "Full JSON for the selected game version. Syntax is checked; game-specific schemas require in-game testing.",
                        true
                    )
                );
                break;
            default:
                throw new ArgumentException("Unknown data pack entry type.");
        }
        return result.AsReadOnly();
    }

    public static DataPackEntry Create(DataPackEntryKind kind, string id = "example") =>
        new(
            kind,
            id,
            Fields(kind).ToDictionary(f => f.Key, f => f.DefaultValue, StringComparer.Ordinal)
        );

    public static KeyValuePair<string, string> Build(
        DataPackEntry entry,
        string gameVersion,
        string packNamespace
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Fields);
        if (
            entry.Fields.Count > 32
            || entry.Fields.Any(p => p.Value == null || p.Value.Length > 2 * 1024 * 1024)
        )
            throw new ArgumentException("An entry field exceeds the editor's supported size.");
        SafePath(entry.Id);
        var major = DataPackGenerator.GetDataPackFormat(gameVersion).Major;
        string V(string key) =>
            entry.Fields.TryGetValue(key, out var value)
                ? value
                : Fields(entry.Kind).First(f => f.Key == key).DefaultValue;
        int Int(string key, int min, int max) => Integer(V(key), min, max);
        bool Bool(string key) => Boolean(V(key));
        object Ingredient(string value)
        {
            Resource(value, true);
            if (value == "minecraft:air") throw new ArgumentException("Air cannot be used in these templates.");
            return major >= 57 ? value
                : value.StartsWith('#') ? new { tag = value[1..] }
                : (object)new { item = value };
        }
        string Folder(string singular, string plural) => major >= 48 ? singular : plural;
        var directory = entry.Kind switch
        {
            DataPackEntryKind.ShapedRecipe
            or DataPackEntryKind.ShapelessRecipe
            or DataPackEntryKind.CookingRecipe => Folder("recipe", "recipes"),
            DataPackEntryKind.LootTable => Folder("loot_table", "loot_tables"),
            DataPackEntryKind.Advancement => Folder("advancement", "advancements"),
            DataPackEntryKind.Function => Folder("function", "functions"),
            DataPackEntryKind.Tag => "tags/" + TagDirectory(V("registry"), major),
            DataPackEntryKind.CustomJson => V("directory"),
            _ => throw new ArgumentException("Unknown data pack entry type."),
        };
        SafePath(directory);
        packNamespace = entry.Namespace ?? packNamespace;
        SafePath(packNamespace);
        if (packNamespace.Contains('/'))
            throw new ArgumentException("Invalid data pack namespace.");
        var path =
            "data/"
            + packNamespace
            + "/"
            + directory
            + "/"
            + entry.Id
            + (entry.Kind == DataPackEntryKind.Function ? ".mcfunction" : ".json");
        // Raw source is authoritative. Guided fields need not describe arbitrary user-defined schemas.
        if (entry.SourceOverride is { } source)
            return new(
                path,
                entry.Kind == DataPackEntryKind.Function ? FunctionText(source) : JsonText(source)
            );
        JsonObject Result()
        {
            Resource(V("result"));
            return new JsonObject
            {
                [major >= 41 ? "id" : "item"] = V("result"),
                ["count"] = Int("count", 1, 64),
            };
        }
        JsonNode node;
        switch (entry.Kind)
        {
            case DataPackEntryKind.ShapelessRecipe:
                var ingredients = Lines(V("ingredients"));
                if (ingredients.Length is < 1 or > 9)
                    throw new ArgumentException("Use 1–9 ingredients.");
                node = JsonSerializer.SerializeToNode(
                    new
                    {
                        type = "minecraft:crafting_shapeless",
                        group = V("group"),
                        ingredients = ingredients.Select(Ingredient).ToArray(),
                        result = Result(),
                    }
                )!;
                break;
            case DataPackEntryKind.ShapedRecipe:
                var rows = V("pattern").Replace("\r", "").Split('\n');
                if (
                    rows.Length is < 1 or > 3
                    || rows.Any(r => r.Length is < 1 or > 3 || r.Length != rows[0].Length)
                    || rows.All(r => r.All(c => c == ' '))
                )
                    throw new ArgumentException(
                        "Use a non-empty rectangular pattern within a 3 by 3 grid."
                    );
                var keys = new Dictionary<string, object>();
                foreach (var binding in Lines(V("keys")))
                {
                    var pair = binding.Split('=', 2);
                    if (
                        pair.Length != 2
                        || pair[0].Length != 1
                        || pair[0][0] == ' '
                        || !keys.TryAdd(pair[0], Ingredient(pair[1].Trim()))
                    )
                        throw new ArgumentException(
                            "Each pattern key must be a unique symbol=ingredient pair."
                        );
                }
                if (
                    !rows.SelectMany(r => r)
                        .Where(c => c != ' ')
                        .Select(c => c.ToString())
                        .ToHashSet()
                        .SetEquals(keys.Keys)
                )
                    throw new ArgumentException(
                        "Pattern symbols and ingredient keys must match exactly."
                    );
                node = JsonSerializer.SerializeToNode(
                    new
                    {
                        type = "minecraft:crafting_shaped",
                        group = V("group"),
                        pattern = rows,
                        key = keys,
                        result = Result(),
                    }
                )!;
                break;
            case DataPackEntryKind.CookingRecipe:
                var type = V("type");
                if (
                    !new[] { "smelting", "blasting", "smoking", "campfire_cooking" }.Contains(type)
                    || major == 4
                        && gameVersion.StartsWith("1.13", StringComparison.Ordinal)
                        && type != "smelting"
                )
                    throw new ArgumentException(
                        "This cooking method is not supported by the selected game version."
                    );
                Resource(V("result"));
                if (
                    !double.TryParse(
                        V("experience"),
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var experience
                    )
                    || !double.IsFinite(experience)
                    || experience < 0
                    || experience > 100000
                )
                    throw new ArgumentException(
                        "Enter non-negative experience using a decimal point."
                    );
                node = JsonSerializer.SerializeToNode(
                    new
                    {
                        type = "minecraft:" + type,
                        group = V("group"),
                        ingredient = Ingredient(V("ingredient")),
                        result = major >= 41 ? (object)new { id = V("result") } : V("result"),
                        experience,
                        cookingtime = Int("time", 1, 1000000),
                    }
                )!;
                break;
            case DataPackEntryKind.LootTable:
                var entries = new List<object>();
                foreach (var line in Lines(V("entries")))
                {
                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length != 3)
                        throw new ArgumentException("Loot entries must use item,count,weight.");
                    Resource(parts[0]);
                    entries.Add(
                        new
                        {
                            type = "minecraft:item",
                            name = parts[0],
                            weight = Integer(parts[2], 1, 100000),
                            functions = new[]
                            {
                                new
                                {
                                    function = "minecraft:set_count",
                                    count = Integer(parts[1], 1, 64),
                                },
                            },
                        }
                    );
                }
                if (entries.Count > 1024)
                    throw new ArgumentException(
                        "A loot table supports at most 1024 guided entries. Use full JSON for other structures."
                    );
                if (entries.Count == 0)
                    throw new ArgumentException("Add at least one loot entry.");
                node = JsonSerializer.SerializeToNode(
                    new { pools = new[] { new { rolls = Int("rolls", 1, 1000), entries } } }
                )!;
                break;
            case DataPackEntryKind.Advancement:
                Resource(V("icon"));
                Resource(V("trigger"));
                if (!new[] { "task", "goal", "challenge" }.Contains(V("frame")))
                    throw new ArgumentException("Invalid advancement frame.");
                var conditions = ParseJson(V("conditions"));
                if (conditions is not JsonObject)
                    throw new ArgumentException("Criterion conditions must be a JSON object.");
                var rewards = new JsonObject { ["experience"] = Int("experience", 0, 1000000) };
                if (V("rewardFunction") != "")
                {
                    Resource(V("rewardFunction"));
                    rewards["function"] = V("rewardFunction");
                }
                var advancement = new JsonObject
                {
                    ["display"] = new JsonObject
                    {
                        ["icon"] = new JsonObject { [major >= 41 ? "id" : "item"] = V("icon") },
                        ["title"] = new JsonObject { ["text"] = V("title") },
                        ["description"] = new JsonObject { ["text"] = V("description") },
                        ["frame"] = V("frame"),
                        ["show_toast"] = Bool("toast"),
                        ["announce_to_chat"] = Bool("announce"),
                        ["hidden"] = Bool("hidden"),
                    },
                    ["criteria"] = new JsonObject
                    {
                        ["done"] = new JsonObject
                        {
                            ["trigger"] = V("trigger"),
                            ["conditions"] = conditions,
                        },
                    },
                    ["rewards"] = rewards,
                };
                if (V("parent") != "")
                {
                    Resource(V("parent"));
                    advancement["parent"] = V("parent");
                }
                node = advancement;
                break;
            case DataPackEntryKind.Function:
                return new(path, FunctionText(V("commands")));
            case DataPackEntryKind.Tag:
                var values = Lines(V("values"));
                foreach (var value in values)
                    Resource(value, true);
                node = JsonSerializer.SerializeToNode(new { replace = Bool("replace"), values })!;
                break;
            case DataPackEntryKind.CustomJson:
                return new(path, JsonText(V("json")));
            default:
                throw new ArgumentException("Unknown data pack entry type.");
        }
        return new(
            path,
            node.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n"
        );
    }

    internal static string TagDirectory(string name, int major)
    {
        SafePath(name);
        var pairs = new Dictionary<string, string>
        {
            ["item"] = "items",
            ["block"] = "blocks",
            ["fluid"] = "fluids",
            ["entity_type"] = "entity_types",
            ["function"] = "functions",
            ["game_event"] = "game_events",
        };
        var singular = pairs.FirstOrDefault(p => p.Value == name).Key ?? name;
        return major < 48 && pairs.TryGetValue(singular, out var plural) ? plural : singular;
    }

    internal static void SafePath(string path)
    {
        if (
            string.IsNullOrEmpty(path)
            || path.Length > 1024
            || path.Any(c =>
                !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/')
            )
        )
            throw new ArgumentException(
                "Use a relative resource path with lowercase letters, numbers, underscores, hyphens, dots and slashes."
            );
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (
                part is "" or "." or ".."
                || part.EndsWith('.')
                || Encoding.UTF8.GetByteCount(part) > 240
                || new[]
                {
                    "con",
                    "prn",
                    "aux",
                    "nul",
                    "com1",
                    "com2",
                    "com3",
                    "com4",
                    "com5",
                    "com6",
                    "com7",
                    "com8",
                    "com9",
                    "lpt1",
                    "lpt2",
                    "lpt3",
                    "lpt4",
                    "lpt5",
                    "lpt6",
                    "lpt7",
                    "lpt8",
                    "lpt9",
                }.Contains(stem)
            )
                throw new ArgumentException("Invalid or reserved resource path segment.");
        }
    }

    static void Resource(string value, bool allowTag = false)
    {
        if (allowTag && value.StartsWith('#'))
            value = value[1..];
        var parts = value.Split(':');
        if (parts.Length != 2)
            throw new ArgumentException(
                "Enter a namespaced item or resource ID, such as minecraft:stone."
            );
        SafePath(parts[0]);
        SafePath(parts[1]);
        if (parts[0].Contains('/'))
            throw new ArgumentException("Invalid resource namespace.");
    }

    internal static bool Boolean(string value) =>
        bool.TryParse(value, out var result)
            ? result
            : throw new ArgumentException("Choose true or false.");

    static int Integer(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n >= min
        && n <= max
            ? n
            : throw new ArgumentException("A numeric field is outside its allowed range.");

    static string[] Lines(string value) =>
        value
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static JsonNode ParseJson(string source)
    {
        if (source.Length > 2 * 1024 * 1024)
            throw new ArgumentException("A data pack file exceeds the editor's 2 MB limit.");
        try
        {
            return JsonNode.Parse(
                    source,
                    documentOptions: new JsonDocumentOptions { MaxDepth = 64 }
                ) ?? throw new ArgumentException("A JSON document cannot be null.");
        }
        catch (JsonException ex)
        {
            throw new DataPackJsonException(ex);
        }
    }

    static string JsonText(string source) =>
        ParseJson(source).ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n";

    static string FunctionText(string source)
    {
        if (source.Length > 2 * 1024 * 1024 || source.Contains('\0'))
            throw new ArgumentException("Invalid or oversized function source.");
        return source.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n') + "\n";
    }
}

public sealed class DataPackJsonException : ArgumentException
{
    public long Line { get; }
    public long Column { get; }

    public DataPackJsonException(JsonException inner)
        : base("Invalid JSON document.", inner)
    {
        Line = (inner.LineNumber ?? 0) + 1;
        Column = (inner.BytePositionInLine ?? 0) + 1;
    }
}
