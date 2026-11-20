namespace CMCLauncher;

public static class CommandValueCatalog
{
    static readonly string[] NbtKeys =
    [
        "Age", "ArmorItems", "Attributes", "BlockEntityTag", "CanDestroy", "CanPlaceOn",
        "Count", "CustomModelData", "CustomName", "Damage", "Enchantments", "StoredEnchantments", "ench", "Glowing",
        "HandItems", "Health", "HideFlags", "Invisible", "Invulnerable", "Item", "Items",
        "Lore", "Motion", "Name", "NoAI", "NoGravity", "Passengers", "Pos", "Rotation",
        "Silent", "Tags", "Unbreakable", "UUID", "display", "id", "tag", "lvl", "Slot",
        "amount", "count", "duration", "amplifier", "levels", "modifiers", "operation",
        "slot", "type", "text", "color", "italic", "bold", "show_in_tooltip", "components"
    ];

    public static IReadOnlyList<string> NbtValueOptions(ItemTagType type, string key, string current)
    {
        IEnumerable<string> defaults = type switch
        {
            ItemTagType.String when key is "color" or "Color" =>
                new[] { "" }.Concat(OptionsForArgument("minecraft:color").Where(color => color != "reset")),
            ItemTagType.String when key is "operation" => ["", "add_value", "add_multiplied_base", "add_multiplied_total"],
            ItemTagType.String when key is "slot" or "Slot" => ["", "any", "mainhand", "offhand", "head", "chest", "legs", "feet"],
            ItemTagType.String => [""],
            ItemTagType.Byte => ["0", "1", "-1"],
            ItemTagType.Short or ItemTagType.Int or ItemTagType.Long => ["0", "1", "2", "10", "-1"],
            ItemTagType.Float or ItemTagType.Double => ["0", "0.5", "1", "2", "-1"],
            _ => [],
        };
        return (CommandGenerator.IsValidNbtValue(type, current) ? defaults.Prepend(current) : defaults)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> OptionsForField(string field) => field switch
    {
        "Whitelist action" => ["on", "off", "list", "reload", "add", "remove"],
        "List type (optional)" => ["players", "ips"],
        "Save option (optional)" => ["flush"],
        _ => [],
    };

    public static IReadOnlyList<string> OptionsForArgument(string parser) =>
        parser switch
        {
            "brigadier:bool" => ["true", "false"],
            "minecraft:gamemode" => ["survival", "creative", "adventure", "spectator"],
            "minecraft:entity_anchor" => ["feet", "eyes"],
            "minecraft:template_mirror" => ["none", "left_right", "front_back"],
            "minecraft:template_rotation" =>
                ["none", "clockwise_90", "clockwise_180", "counterclockwise_90"],
            "minecraft:heightmap" => ["world_surface", "ocean_floor", "motion_blocking", "motion_blocking_no_leaves"],
            "minecraft:operation" => ["=", "+=", "-=", "*=", "/=", "%=", "<", ">", "><"],
            "minecraft:swizzle" => ["x", "y", "z", "xy", "xz", "yz", "xyz"],
            "minecraft:color" =>
            [
                "black", "dark_blue", "dark_green", "dark_aqua", "dark_red", "dark_purple",
                "gold", "gray", "dark_gray", "blue", "green", "aqua", "red", "light_purple",
                "yellow", "white", "reset",
            ],
            "minecraft:scoreboard_slot" => new[] { "list", "sidebar", "below_name" }
                .Concat(OptionsForArgument("minecraft:color")
                    .Where(color => color != "reset").Select(color => "sidebar.team." + color))
                .ToArray(),
            "minecraft:item_slot" => ItemSlots(false),
            "minecraft:item_slots" => ItemSlots(true),
            "minecraft:dimension" => ["minecraft:overworld", "minecraft:the_nether", "minecraft:the_end"],
            _ => [],
        };

    static IReadOnlyList<string> ItemSlots(bool allowGroups)
    {
        string[] named = ["weapon.mainhand", "weapon.offhand", "armor.head", "armor.chest", "armor.legs", "armor.feet", "horse.saddle"];
        (string Name, int Count)[] indexed =
        [
            ("container", 54), ("hotbar", 9), ("inventory", 27), ("enderchest", 27),
            ("villager", 8), ("horse", 15),
        ];
        var slots = named.Concat(indexed.SelectMany(group =>
            Enumerable.Range(0, group.Count).Select(index => $"{group.Name}.{index}")
        ));
        return (allowGroups
            ? slots.Concat(indexed.Select(group => group.Name + ".*")).Concat(["armor.*", "weapon.*"])
            : slots).ToArray();
    }

    public static IReadOnlyList<string> TargetSelectors(
        string parser, string version, bool singleTarget = false, bool playersOnly = false
    )
    {
        if (parser is not ("minecraft:entity" or "minecraft:game_profile" or "minecraft:score_holder"))
            return [];
        playersOnly |= parser == "minecraft:game_profile";
        var selectors = new List<string> { "@p", "@r", "@s" };
        if (!singleTarget)
        {
            selectors.Add("@a");
            if (!playersOnly)
                selectors.Add("@e");
        }
        if (!playersOnly && Version.TryParse(version, out var release) && release >= new Version(1, 21))
            selectors.Add("@n");
        return selectors;
    }

    public static string? RegistryForField(string field) => field switch
    {
        "Item ID" => "item",
        "Effect ID" => "mob_effect",
        "Block ID" => "block",
        "Entity ID" => "entity_type",
        _ => null,
    };

    public static string? RegistryForArgument(string parser, string? registry, string? argument = null) => parser switch
    {
        "minecraft:item_stack" or "minecraft:item_predicate" => "item",
        "minecraft:mob_effect" => "mob_effect",
        "minecraft:block_state" or "minecraft:block_predicate" => "block",
        "minecraft:entity_summon" => "entity_type",
        "minecraft:particle" => "particle_type",
        "minecraft:item_enchantment" => "enchantment",
        "minecraft:loot_table" => "loot_table",
        "minecraft:dialog" => "dialog",
        "minecraft:resource_location" when argument == "advancement" => "advancement",
        "minecraft:resource_location" when argument == "recipe" => "recipe",
        "minecraft:resource_location" when argument == "sound" => "sound_event",
        "minecraft:resource_location" when argument == "template" => "structure",
        _ when registry != null && registry.StartsWith("minecraft:", StringComparison.Ordinal)
            && ValidRegistry(registry["minecraft:".Length..]) => registry["minecraft:".Length..],
        _ => null,
    };

    public sealed record Choice(string Id, string Name, string Description = "")
    {
        public override string ToString() => (Name == Id || Id.Length == 0 ? Name : $"{Name} · {Id}")
            + (Description.Length == 0 ? "" : Environment.NewLine + Description);
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Version, string Language),
        IReadOnlyDictionary<string, string>> Languages = new();

    public static async Task<IReadOnlyDictionary<string, string>> LoadLanguage(
        LauncherCore core, string version, string language, CancellationToken ct
    )
    {
        var key = (Version: version.ToLowerInvariant(), Language: language.ToLowerInvariant());
        if (Languages.TryGetValue(key, out var cached))
            return cached;
        var data = await core.Json(
            $"https://raw.githubusercontent.com/misode/mcmeta/{Uri.EscapeDataString(key.Version)}-assets-json/assets/minecraft/lang/{Uri.EscapeDataString(key.Language)}.json",
            ct
        );
        var names = data.AsObject().Where(pair => pair.Value is System.Text.Json.Nodes.JsonValue)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
        return Languages.GetOrAdd(key, names);
    }

    public static Choice LocalizedChoice(
        string id, string registry, IReadOnlyDictionary<string, string> names, Func<string, string> translate
    )
    {
        if (registry == "nbt_value")
        {
            var nameKey = id.Length == 0 ? "Empty text" : "NBT value." + id;
            var display = translate(nameKey);
            return new(id, display == nameKey && id.Length > 0 ? id : display);
        }
        var token = id.StartsWith('#') ? id[1..] : id;
        var parts = token.Split(':', 2);
        var path = parts[^1];
        var resource = (parts.Length == 2 ? parts[0] : "minecraft") + "." + path.Replace('/', '.');
        string[] keys = registry switch
        {
            "item" or "recipe" => ["item." + resource, "block." + resource],
            "advancement" => ["advancements." + path.Replace('/', '.') + ".title"],
            "sound_event" => ["subtitles." + path],
            "block" => ["block." + resource],
            "mob_effect" => ["effect." + resource],
            "entity_type" => ["entity." + resource],
            "worldgen/biome" => ["biome." + resource],
            "enchantment" => ["enchantment." + resource],
            "attribute" => ["attribute.name." + path],
            "stat_type" or "custom_stat" => ["stat." + resource],
            "painting_variant" => ["painting." + resource + ".title"],
            "gamerule" => ["gamerule." + path, "gamerule." + resource, "gamerule." + SnakeToCamel(path)],
            _ => [],
        };
        var labelKey = registry switch
        {
            "nbt" => "NBT." + path,
            "data_component_type" => "Item component." + path,
            _ => path,
        };
        var fallback = translate(labelKey);
        if (fallback == labelKey && registry == "data_component_type")
            fallback = translate(path);
        if (fallback == labelKey || fallback == path)
            fallback = id;
        var name = keys.Select(key => names.GetValueOrDefault(key)).FirstOrDefault(value => value != null)
            ?? fallback;
        var description = registry == "advancement"
            ? names.GetValueOrDefault("advancements." + path.Replace('/', '.') + ".description") ?? ""
            : registry == "gamerule"
            ? keys.Select(key => names.GetValueOrDefault(key + ".description")).FirstOrDefault(value => value != null) ?? ""
            : "";
        return new(id, name, description);
    }

    public static async Task<IReadOnlyList<string>> LoadAdvancementCriteria(
        LauncherCore core, string version, string advancement, CancellationToken ct
    )
    {
        var parts = advancement.Split(':', 2);
        var nameSpace = parts.Length == 2 ? parts[0] : "minecraft";
        var path = parts[^1];
        if (!System.Text.RegularExpressions.Regex.IsMatch(nameSpace, @"\A[a-z0-9_.-]+\z")
            || path.Split('/').Any(part => part is "." or ".."
                || !System.Text.RegularExpressions.Regex.IsMatch(part, @"\A[a-z0-9_.-]+\z")))
            return [];
        var folder = Version.TryParse(version, out var release) && release < new Version(1, 21)
            ? "advancements" : "advancement";
        var versionTag = Uri.EscapeDataString(version.ToLowerInvariant());
        var definition = await core.Json(
            $"https://raw.githubusercontent.com/misode/mcmeta/{versionTag}-data-json/data/{nameSpace}/{folder}/{path}.json",
            ct
        );
        return definition["criteria"] is System.Text.Json.Nodes.JsonObject criteria
            ? criteria.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray()
            : [];
    }

    static string SnakeToCamel(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "_([a-z])", match => match.Groups[1].Value.ToUpperInvariant());

    static bool ValidRegistry(string registry) =>
        System.Text.RegularExpressions.Regex.IsMatch(registry, @"\A[a-z0-9_]+(?:/[a-z0-9_]+)*\z");

    public static async Task<IReadOnlyList<string>> Load(
        LauncherCore core, string version, string registry, CancellationToken ct
    )
    {
        if (registry == "nbt")
            return NbtKeys;
        if (!ValidRegistry(registry))
            throw new ArgumentOutOfRangeException(nameof(registry));
        var data = await core.Json(
            $"https://raw.githubusercontent.com/misode/mcmeta/{Uri.EscapeDataString(version.ToLowerInvariant())}-registries/{registry}/data.json",
            ct
        );
        return data.Items().Select(node => node?.GetValue<string>() ?? "")
            .Where(id => id.Length > 0)
            .Select(id => id.Contains(':') ? id : "minecraft:" + id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
