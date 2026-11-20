using System.Globalization;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public enum ItemTagType
{
    String,
    Byte,
    Short,
    Int,
    Long,
    Float,
    Double,
    Compound,
    List,
    ByteArray,
    IntArray,
    LongArray,
}

public sealed class ItemEnchantment
{
    public string Id { get; set; } = "";
    public int Level { get; set; } = 1;
}

public sealed class ItemTag
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public ItemTagType Type { get; set; }
    public List<ItemTag> Children { get; } = [];
    public List<ItemEnchantment>? Enchantments { get; set; }
}

public static partial class CommandGenerator
{
    public static bool UsesItemComponents(string release) => Release(release) >= ItemComponents;

    static readonly Dictionary<string, (int Id, int Minor)> NumericEnchantments = new()
    {
        ["protection"] = (0, 8), ["fire_protection"] = (1, 8), ["feather_falling"] = (2, 8),
        ["blast_protection"] = (3, 8), ["projectile_protection"] = (4, 8), ["respiration"] = (5, 8),
        ["aqua_affinity"] = (6, 8), ["thorns"] = (7, 8), ["depth_strider"] = (8, 8),
        ["frost_walker"] = (9, 9), ["binding_curse"] = (10, 11),
        ["sharpness"] = (16, 8), ["smite"] = (17, 8), ["bane_of_arthropods"] = (18, 8),
        ["knockback"] = (19, 8), ["fire_aspect"] = (20, 8), ["looting"] = (21, 8),
        ["sweeping"] = (22, 11), ["efficiency"] = (32, 8), ["silk_touch"] = (33, 8),
        ["unbreaking"] = (34, 8), ["fortune"] = (35, 8), ["power"] = (48, 8),
        ["punch"] = (49, 8), ["flame"] = (50, 8), ["infinity"] = (51, 8),
        ["luck_of_the_sea"] = (61, 8), ["lure"] = (62, 8), ["mending"] = (70, 9),
        ["vanishing_curse"] = (71, 11),
    };

    public static IReadOnlyList<string>? LegacyEnchantmentChoices(string release)
    {
        var version = Release(release);
        if (version >= new Version(1, 14))
            return null;
        var choices = NumericEnchantments.Where(pair => pair.Value.Minor <= version.Minor)
            .Select(pair => "minecraft:" + pair.Key);
        if (version >= Flattening)
            choices = choices.Concat(new[] { "minecraft:loyalty", "minecraft:impaling", "minecraft:riptide", "minecraft:channeling" });
        return choices.ToArray();
    }

    public static int MaxEnchantmentLevel(string release) =>
        UsesItemComponents(release) ? 255 : short.MaxValue;

    public static bool IsItemEnchantmentField(string key, bool components) => components
        ? key.Trim() is "enchantments" or "minecraft:enchantments" or "stored_enchantments" or "minecraft:stored_enchantments"
        : key.Trim() is "ench" or "Enchantments" or "StoredEnchantments";

    static ItemTag MaterializeEnchantments(ItemTag source, Version version)
    {
        if (source.Enchantments is not { } choices
            || !IsItemEnchantmentField(source.Key, version >= ItemComponents))
            return source;

        var component = version >= ItemComponents;
        var stored = source.Key.Trim() is "StoredEnchantments" or "stored_enchantments" or "minecraft:stored_enchantments";
        var key = component ? (stored ? "minecraft:stored_enchantments" : "minecraft:enchantments")
            : stored ? "StoredEnchantments" : version >= Flattening ? "Enchantments" : "ench";
        var result = new ItemTag { Key = key, Type = component ? ItemTagType.Compound : ItemTagType.List };
        var levels = result;
        if (component && version < new Version(1, 21, 5))
        {
            levels = new ItemTag { Key = "levels", Type = ItemTagType.Compound };
            result.Children.Add(levels);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (choice.Id.Length == 0)
                throw new ArgumentException("Choose an enchantment for each row.");
            if (!Regex.IsMatch(choice.Id, @"\A[a-z0-9_.-]+:[a-z0-9_./-]+\z") || !seen.Add(choice.Id))
                throw new ArgumentException("Each enchantment must have a unique valid ID.");
            if (choice.Level < 1 || choice.Level > (component ? 255 : short.MaxValue))
                throw new ArgumentException("Enter a valid enchantment level.");
            var level = choice.Level.ToString(CultureInfo.InvariantCulture);
            if (component)
            {
                levels.Children.Add(new ItemTag { Key = choice.Id, Type = ItemTagType.Int, Value = level });
                continue;
            }
            var id = new ItemTag { Key = "id", Type = ItemTagType.String, Value = choice.Id };
            if (version < Flattening)
            {
                if (!choice.Id.StartsWith("minecraft:", StringComparison.Ordinal)
                    || !NumericEnchantments.TryGetValue(choice.Id["minecraft:".Length..], out var legacy)
                    || legacy.Minor > version.Minor)
                    throw new ArgumentException("This enchantment is unavailable in the selected version.");
                id.Type = ItemTagType.Short;
                id.Value = legacy.Id.ToString(CultureInfo.InvariantCulture);
            }
            var record = new ItemTag { Type = ItemTagType.Compound };
            record.Children.Add(id);
            record.Children.Add(new ItemTag { Key = "lvl", Type = ItemTagType.Short, Value = level });
            result.Children.Add(record);
        }
        return result;
    }

    public static string BuildItemTags(string release, string name, IReadOnlyList<ItemTag> tags)
    {
        if (tags.Count == 0)
            return "";
        var version = Release(release);
        tags = tags.Select(tag => MaterializeEnchantments(tag, version)).ToArray();
        if (version >= ItemComponents)
        {
            var entries = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                var key = tag.Key.Trim();
                if (!Regex.IsMatch(key, @"^!?([a-z0-9_.-]+:)?[a-z0-9_./-]+$"))
                    throw new ArgumentException("Enter a valid item component ID.");
                var removal = key.StartsWith('!');
                var id = removal ? key[1..] : key;
                id = id.Contains(':') ? id : "minecraft:" + id;
                if (!keys.Add(id))
                    throw new ArgumentException("Duplicate NBT key or item component.");
                entries.Add(removal ? "!" + id : id + "=" + SerializeTag(tag));
            }
            if (name.Length != 0)
            {
                if (!keys.Add("minecraft:custom_name"))
                    throw new ArgumentException("Duplicate NBT key or item component.");
                entries.Insert(
                    0,
                    "minecraft:custom_name="
                        + (
                            version >= SnbtText
                                ? "{text:" + Quote(name) + "}"
                                : Quote(
                                    System.Text.Json.JsonSerializer.Serialize(new { text = name })
                                )
                        )
                );
            }
            return "[" + string.Join(",", entries) + "]";
        }
        var fields = TagFields(tags);
        if (name.Length != 0)
        {
            if (tags.Any(tag => tag.Key.Trim() == "display"))
                throw new ArgumentException(
                    "Set display in custom data or enter an item name, not both."
                );
            var label =
                version >= Flattening
                    ? System.Text.Json.JsonSerializer.Serialize(new { text = name })
                    : name;
            fields.Insert(0, "display:{Name:" + Quote(label) + "}");
        }
        return "{" + string.Join(",", fields) + "}";
    }

    public static string BuildNbtCompound(IReadOnlyList<ItemTag> tags) =>
        "{" + string.Join(",", TagFields(tags)) + "}";

    public static string BuildNbtValue(ItemTag tag) => SerializeTag(tag);

    public static bool IsValidNbtValue(ItemTagType type, string value)
    {
        if (IsContainer(type))
            return false;
        try
        {
            BuildNbtValue(new ItemTag { Type = type, Value = value });
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static List<string> TagFields(IReadOnlyList<ItemTag> nodes)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<string>();
        foreach (var node in nodes)
        {
            var key = node.Key.Trim();
            if (key.Length == 0 || key.Any(char.IsControl) || !keys.Add(key))
                throw new ArgumentException("Enter a unique NBT key.");
            fields.Add(Quote(key) + ":" + SerializeTag(node));
        }
        return fields;
    }

    public static bool IsContainer(ItemTagType type) => type >= ItemTagType.Compound;

    public static ItemTagType? RequiredElementType(ItemTagType type) =>
        type switch
        {
            ItemTagType.ByteArray => ItemTagType.Byte,
            ItemTagType.IntArray => ItemTagType.Int,
            ItemTagType.LongArray => ItemTagType.Long,
            _ => null,
        };

    static string SerializeTag(ItemTag tag)
    {
        var type = tag.Type;
        var value = tag.Value.Trim();
        if (tag.Value.Any(char.IsControl))
            throw new ArgumentException("NBT values cannot contain control characters.");
        if (type == ItemTagType.String)
            return Quote(tag.Value);
        if (type == ItemTagType.Compound)
            return "{" + string.Join(",", TagFields(tag.Children)) + "}";
        if (IsContainer(type))
        {
            var required = RequiredElementType(type) ?? tag.Children.FirstOrDefault()?.Type;
            if (tag.Children.Any(child => child.Type != required))
                throw new ArgumentException(
                    "NBT lists and arrays must contain a single element type."
                );
            var prefix = type switch
            {
                ItemTagType.ByteArray => "B;",
                ItemTagType.IntArray => "I;",
                ItemTagType.LongArray => "L;",
                _ => "",
            };
            return "[" + prefix + string.Join(",", tag.Children.Select(SerializeTag)) + "]";
        }
        const NumberStyles integer = NumberStyles.AllowLeadingSign;
        var culture = CultureInfo.InvariantCulture;
        return type switch
        {
            ItemTagType.Byte when sbyte.TryParse(value, integer, culture, out var number) =>
                number.ToString(culture) + "b",
            ItemTagType.Short when short.TryParse(value, integer, culture, out var number) =>
                number.ToString(culture) + "s",
            ItemTagType.Int when int.TryParse(value, integer, culture, out var number) =>
                number.ToString(culture),
            ItemTagType.Long when long.TryParse(value, integer, culture, out var number) =>
                number.ToString(culture) + "L",
            ItemTagType.Float
                when float.TryParse(value, NumberStyles.Float, culture, out var number)
                    && float.IsFinite(number) => number.ToString("R", culture) + "f",
            ItemTagType.Double
                when double.TryParse(value, NumberStyles.Float, culture, out var number)
                    && double.IsFinite(number) => number.ToString("R", culture) + "d",
            _ => throw new ArgumentException("Enter a number valid for the selected NBT type."),
        };
    }
}
