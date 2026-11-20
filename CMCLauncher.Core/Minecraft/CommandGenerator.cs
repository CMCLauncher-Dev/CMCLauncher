using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// 命令表单字段：标签、示例、是否可选与输入提示。
public sealed record CommandField(
    string Label,
    string Example,
    bool Optional = false,
    string? Hint = null
);

// 命令表单：命令名、语法说明与字段列表。
public sealed record CommandForm(string Name, string Syntax, IReadOnlyList<CommandField> Fields);

// Minecraft 命令生成器：按游戏版本提供 give/tp/effect/summon/fill 等命令表单，
// 并对输入做严格校验后生成对应版本的命令文本。
public static class CommandGenerator
{
    public const string CustomCommand = "Custom command";
    // 自定义命令表单：命令名 + 可选参数。
    public static CommandForm CustomForm { get; } =
        new(
            CustomCommand,
            "/<command> [arguments]",
            [
                new("Command name", ""),
                new("Command arguments (optional)", "", true, """@a {"text":"Hello world"}"""),
            ]
        );

    // 各特性引入的版本门槛：命名物品、坐标传送、物品标签、扁平化、物品组件、SNBT 文本组件。
    static readonly Version NamedItems = new(1, 7, 2);
    static readonly Version CoordinateTeleport = new(1, 4, 2);
    static readonly Version ItemTags = new(1, 8, 0);
    static readonly Version Flattening = new(1, 13, 0);
    static readonly Version ItemComponents = new(1, 20, 5);
    static readonly Version SnbtText = new(1, 21, 5);

    // 解析并校验 Java 版稳定版本号（如 1.12.2、1.21.5）。
    static Version Release(string text)
    {
        if (
            !Regex.IsMatch(text.Trim(), @"^[0-9]{1,3}\.[0-9]{1,3}(?:\.[0-9]{1,3})?$")
            || !Version.TryParse(text.Trim(), out var version)
            || version < new Version(1, 0)
        )
            throw new ArgumentException(
                "Enter a stable Java Edition version, such as 1.12.2 or 1.21.5."
            );
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    // 按版本返回可用的命令表单列表（含自定义命令），
    // 各命令的语法与字段随版本特性（扁平化、物品组件等）变化。
    public static IReadOnlyList<CommandForm> Forms(string release)
    {
        var version = Release(release);
        var modern = version >= Flattening;
        var forms = new List<CommandForm>();
        var give = new List<CommandField>
        {
            new("Target", "Steve"),
            new("Item ID", version < NamedItems ? "1" : "minecraft:stone"),
            new("Count", "1"),
        };
        if (version >= ItemTags)
        {
            give.Add(new("Item name (optional)", "", true));
            give.Add(new("Custom item data (JSON, optional)", "", true));
            give.Add(
                version >= ItemComponents
                    ? new(
                        "Advanced item components (optional)",
                        "",
                        true,
                        """[minecraft:custom_data={rank:3b},minecraft:unbreakable={}]"""
                    )
                    : new(
                        "Advanced item SNBT (optional)",
                        "",
                        true,
                        """{Unbreakable:1b,RepairCost:2}"""
                    )
            );
        }
        forms.Add(
            new(
                "give",
                version >= ItemComponents ? "/give <target> <item>[<components>] <count>"
                    : modern ? "/give <target> <item>{NBT} <count>"
                    : version >= ItemTags ? "/give <target> <item> <count> 0 [NBT]"
                    : "/give <target> <item> <count>",
                give
            )
        );
        forms.Add(
            new(
                "tp",
                version >= CoordinateTeleport ? "/tp <target> <x y z>" : "/tp <target> <player>",
                [
                    new("Target", "Steve"),
                    version >= CoordinateTeleport
                        ? new("Coordinates", "0 64 0")
                        : new("Destination player", "Alex"),
                ]
            )
        );
        if (version >= new Version(1, 5, 0))
            forms.Add(
                new(
                    "effect",
                    modern
                        ? "/effect give <target> <effect> <seconds> <amplifier>"
                        : "/effect <target> <numeric ID> <seconds> <amplifier>",
                    [
                        new("Target", "Steve"),
                        new("Effect ID", modern ? "minecraft:speed" : "1"),
                        new("Duration (seconds)", "30"),
                        new("Amplifier", "0"),
                    ]
                )
            );
        if (version >= NamedItems)
            forms.Add(
                new(
                    "summon",
                    "/summon <entity> <x y z>",
                    [
                        new("Entity ID", modern ? "minecraft:pig" : "Pig"),
                        new("Coordinates", "0 64 0"),
                    ]
                )
            );
        if (version >= ItemTags)
            forms.Add(
                new(
                    "fill",
                    "/fill <from> <to> <block>",
                    [
                        new("Start coordinates", "0 64 0"),
                        new("End coordinates", "3 64 3"),
                        new("Block ID", "minecraft:stone"),
                    ]
                )
            );
        forms.Add(CustomForm);
        return forms;
    }

    // 按版本与表单生成命令文本：
    // 先校验必填字段、长度与控制字符，再按命令类型分别生成
    // （give 支持物品名/自定义数据/高级组件三种模式，fill 校验区域体积上限）。
    public static string Generate(string release, string command, IReadOnlyList<string> input)
    {
        if (command == CustomCommand)
            return GenerateCustom(input);
        var version = Release(release);
        var form = Forms(release).FirstOrDefault(form => form.Name == command);
        if (
            form == null
            || input.Count > form.Fields.Count
            || form.Fields.Skip(input.Count).Any(field => !field.Optional)
        )
            throw new ArgumentException(
                "This command is unavailable for the selected Minecraft version."
            );
        var values = form
            .Fields.Select((_, index) => index < input.Count ? input[index].Trim() : "")
            .ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            if (form.Fields[index].Hint != null)
                continue;
            if (
                (!form.Fields[index].Optional && values[index].Length == 0)
                || values[index].Length > 512
                || values[index].Any(char.IsControl)
            )
                throw new ArgumentException("Fill in the required fields without line breaks.");
        }
        var modern = version >= Flattening;
        return command switch
        {
            "give" => Give(),
            "tp" => $"/tp {Target(values[0], version)} "
                + (
                    version >= CoordinateTeleport
                        ? Coordinates(values[1], version)
                        : Target(values[1], version)
                ),
            "effect" => $"/effect{(modern ? " give" : "")} {Target(values[0], version)} "
                + (modern ? Id(values[1]) : Integer(values[1], 1, 255))
                + " "
                + Integer(values[2], 1, modern ? 1_000_000 : 86_400)
                + " "
                + Integer(values[3], 0, modern ? 255 : 127),
            "summon" =>
                $"/summon {Id(values[0], legacy: !modern)} {Coordinates(values[1], version)}",
            "fill" => Fill(),
            _ => throw new ArgumentException("Unsupported command."),
        };

        string Give()
        {
            var target = Target(values[0], version, playersOnly: true);
            var item = version < NamedItems ? Integer(values[1], 1, 32767) : Id(values[1]);
            var count = Integer(values[2], 1, 64);
            var name = values.Length > 3 ? values[3] : "";
            var customData = values.Length > 4 ? values[4] : "";
            var advanced = values.Length > 5 ? values[5] : "";
            if (advanced.Length != 0)
            {
                if (name.Length != 0 || customData.Length != 0)
                    throw new ArgumentException(
                        "Use either the basic item fields or the advanced item editor."
                    );
                var suffix = AdvancedItemData(advanced, version >= ItemComponents);
                return modern
                    ? $"/give {target} {item}{suffix} {count}"
                    : $"/give {target} {item} {count} 0 {suffix}";
            }
            var tags = customData.Length == 0 ? [] : CustomTags(customData);
            if (version >= ItemComponents)
            {
                var components = new List<string>();
                if (name.Length != 0)
                    components.Add(
                        "minecraft:custom_name="
                            + (
                                version >= SnbtText
                                    ? "{text:" + Quote(name) + "}"
                                    : Quote(JsonSerializer.Serialize(new { text = name }))
                            )
                    );
                if (tags.Count != 0)
                    components.Add("minecraft:custom_data={" + string.Join(",", tags) + "}");
                if (components.Count != 0)
                    item += "[" + string.Join(",", components) + "]";
            }
            else if (name.Length != 0 || tags.Count != 0)
            {
                if (name.Length != 0)
                {
                    if (tags.Any(tag => tag.StartsWith("\"display\":", StringComparison.Ordinal)))
                        throw new ArgumentException(
                            "Set display in custom data or enter an item name, not both."
                        );
                    var nameTag = modern ? JsonSerializer.Serialize(new { text = name }) : name;
                    tags.Insert(0, "display:{Name:" + Quote(nameTag) + "}");
                }
                var nbt = "{" + string.Join(",", tags) + "}";
                if (!modern)
                    return $"/give {target} {item} {count} 0 {nbt}";
                item += nbt;
            }
            return $"/give {target} {item} {count}";
        }

        string Fill()
        {
            var from = Coordinates(values[0], version, block: true);
            var to = Coordinates(values[1], version, block: true);
            var first = from.Split(' ');
            var last = to.Split(' ');
            static char Basis(string part) => part[0] is '~' or '^' ? part[0] : '=';
            static double Number(string part) =>
                double.TryParse(
                    part.TrimStart('~', '^'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number
                )
                    ? number
                    : 0;
            if (
                first
                    .Zip(last)
                    .All(pair =>
                        Basis(pair.First) == Basis(pair.Second) && Basis(pair.First) != '^'
                    )
                && first
                    .Zip(last)
                    .Aggregate(
                        1d,
                        (volume, pair) =>
                            volume * (Math.Abs(Number(pair.First) - Number(pair.Second)) + 1)
                    ) > 32768
            )
                throw new ArgumentException(
                    "The fill region exceeds the default limit of 32,768 blocks."
                );
            return $"/fill {from} {to} {Id(values[2])}";
        }
    }

    // 生成自定义命令：校验命令名格式与参数（单行、无控制字符）。
    static string GenerateCustom(IReadOnlyList<string> input)
    {
        if (input.Count is < 1 or > 2)
            throw new ArgumentException("Enter a command name and optional arguments.");
        var root = input[0].Trim();
        if (root.StartsWith('/'))
            root = root[1..];
        if (!Regex.IsMatch(root, @"^(?:[A-Za-z0-9_.-]+:)?[A-Za-z0-9_.?/-]+$"))
            throw new ArgumentException("Enter only the command name in the first field.");
        var arguments = input.Count == 2 ? input[1].Trim() : "";
        if (arguments.Any(char.IsControl))
            throw new ArgumentException("Command arguments must be on a single line.");
        return arguments.Length == 0 ? "/" + root : "/" + root + " " + arguments;
    }

    // 校验并规范化高级物品数据：
    // 组件模式要求方括号包裹的组件列表，旧版要求花括号包裹的 SNBT 复合体；
    // 逐字符扫描检查括号配对、字符串引号闭合与控制字符，并把换行折叠为空格。
    static string AdvancedItemData(string text, bool components)
    {
        var error = components
            ? "Enter one balanced item component list in square brackets."
            : "Enter one balanced SNBT compound in braces.";
        if (text.Length > 16000 || text[0] != (components ? '[' : '{'))
            throw new ArgumentException(error);

        var open = new Stack<char>();
        var result = new StringBuilder(text.Length);
        char quote = '\0';
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (char.IsControl(character))
                    throw new ArgumentException(error);
                result.Append(character);
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '"' or '\'')
                quote = character;
            else if (character is '[' or '{')
            {
                open.Push(character == '[' ? ']' : '}');
                if (open.Count > 64)
                    throw new ArgumentException(error);
            }
            else if (character is ']' or '}')
            {
                if (
                    !open.TryPop(out var expected)
                    || character != expected
                    || (open.Count == 0 && index != text.Length - 1)
                )
                    throw new ArgumentException(error);
            }
            else if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                throw new ArgumentException(error);
            result.Append(character is '\r' or '\n' or '\t' ? ' ' : character);
        }
        if (open.Count != 0 || quote != '\0')
            throw new ArgumentException(error);
        return result.ToString();
    }

    // 把 JSON 对象转换为 NBT 标签列表（如 "display:{Name:...}"）：
    // 递归处理对象/数组/字符串/布尔/数字，并校验键唯一性与值合法性。
    static List<string> CustomTags(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(
                text,
                new JsonDocumentOptions { MaxDepth = 16 }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return Properties(document.RootElement);
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                "Custom item data must be a JSON object with valid NBT values."
            );
        }

        static List<string> Properties(JsonElement element)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<string>();
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name) || property.Name.Any(char.IsControl))
                    throw new JsonException();
                fields.Add(Quote(property.Name) + ":" + Tag(property.Value).Text);
            }
            return fields;
        }

        static (char Type, string Text) Tag(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return ('c', "{" + string.Join(",", Properties(element)) + "}");
                case JsonValueKind.Array:
                    var tags = element.EnumerateArray().Select(Tag).ToArray();
                    if (tags.Select(value => value.Type).Distinct().Skip(1).Any())
                        throw new JsonException();
                    return ('a', "[" + string.Join(",", tags.Select(value => value.Text)) + "]");
                case JsonValueKind.String:
                    var value = element.GetString()!;
                    if (value.Any(char.IsControl))
                        throw new JsonException();
                    return ('s', Quote(value));
                case JsonValueKind.True:
                    return ('b', "1b");
                case JsonValueKind.False:
                    return ('b', "0b");
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var integer))
                        return ('i', integer.ToString(CultureInfo.InvariantCulture));
                    if (element.TryGetInt64(out var longInteger))
                        return ('l', longInteger.ToString(CultureInfo.InvariantCulture) + "L");
                    if (element.TryGetDouble(out var number) && double.IsFinite(number))
                        return ('d', number.ToString("R", CultureInfo.InvariantCulture) + "d");
                    break;
            }
            throw new JsonException();
        }
    }

    // 按 NBT 规则转义并加引号。
    static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // 校验目标选择器：玩家名（1-16 位字母数字下划线）或该版本支持的 @p/@a/@r/@s/@e。
    static string Target(string value, Version version, bool playersOnly = false)
    {
        if (Regex.IsMatch(value, @"^[A-Za-z0-9_]{1,16}$"))
            return value;
        if (
            (value is "@p" or "@a" or "@r") && version >= new Version(1, 4, 2)
            || value == "@s" && version >= new Version(1, 12, 0)
            || !playersOnly && value == "@e" && version >= ItemTags
        )
            return value;
        throw new ArgumentException("Use a player name or a selector supported by this version.");
    }

    // 校验 Minecraft ID：legacy 模式仅允许旧版命名（字母数字下划线），
    // 现代模式允许带命名空间的小写 ID。
    static string Id(string value, bool legacy = false)
    {
        if (
            !Regex.IsMatch(
                value,
                legacy ? @"^[A-Za-z0-9_]+$" : @"^(?:[a-z0-9_.-]+:)?[a-z0-9_./-]+$"
            )
        )
            throw new ArgumentException("Enter a valid Minecraft ID for this version.");
        return value;
    }

    // 校验并规范化指定范围内的整数。
    static string Integer(string value, int minimum, int maximum)
    {
        if (
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < minimum
            || number > maximum
        )
            throw new ArgumentException($"Enter an integer from {minimum} to {maximum}.");
        return number.ToString(CultureInfo.InvariantCulture);
    }

    // 校验三个坐标分量：支持绝对值、~ 相对值与 ^ 局部值（局部值仅现代版本且须全部使用），
    // block 模式要求整数（用于 /fill 的方块坐标）。
    static string Coordinates(string value, Version version, bool block = false)
    {
        var parts = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var local = parts.Any(part => part.StartsWith('^'));
        if (
            parts.Length != 3
            || (local && (version < Flattening || parts.Any(part => !part.StartsWith('^'))))
            || parts.Any(part =>
                !Regex.IsMatch(
                    part,
                    block && !local
                        ? @"^(?:~(?:-?[0-9]+)?|-?[0-9]+)$"
                        : @"^(?:[~^](?:-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))?|-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))$"
                )
            )
        )
            throw new ArgumentException("Enter three valid coordinates.");
        foreach (var part in parts)
        {
            var number = part.TrimStart('~', '^');
            if (
                number.Length != 0
                && (
                    block && !local
                        ? !int.TryParse(
                            number,
                            NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture,
                            out _
                        )
                        : !double.TryParse(
                            number,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var coordinate
                        ) || !double.IsFinite(coordinate)
                )
            )
                throw new ArgumentException("Enter three valid coordinates.");
        }
        return string.Join(" ", parts);
    }
}
