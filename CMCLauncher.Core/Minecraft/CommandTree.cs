using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed class CommandTree
{
    public sealed record Branch(string Name, bool Argument, string Parser, string? Registry = null, bool SingleTarget = false, bool PlayersOnly = false);
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[]
    {
        "All commands", "Common commands", "Server administration", "World and blocks",
        "Entities and players", "Other commands",
    });

    public static int CategoryOf(string command) =>
        command[(command.LastIndexOf(':') + 1)..] switch
        {
            "give" or "tp" or "teleport" or "gamemode" or "effect" or "help" => 1,
            "op" or "deop" or "kick" or "ban" or "ban-ip" or "banlist" or "pardon"
                or "pardon-ip" or "whitelist" or "list" or "save-all" or "save-on"
                or "save-off" or "stop" or "setidletimeout" or "reload" or "transfer" => 2,
            "fill" or "fillbiome" or "setblock" or "clone" or "time" or "weather"
                or "gamerule" or "difficulty" or "worldborder" or "locate" or "forceload"
                or "setworldspawn" or "place" => 3,
            "summon" or "kill" or "attribute" or "damage" or "ride" or "spectate"
                or "clear" or "enchant" or "item" or "experience" or "xp" or "spawnpoint"
                or "tag" or "team" or "advancement" => 4,
            _ => 5,
        };

    public static bool MatchesCommand(string name, string label, int category, string? query)
    {
        if (category > 0 && CategoryOf(name) != category)
            return false;
        var search = query?.Trim().TrimStart('/') ?? "";
        return search.Length == 0
            || name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || label.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    sealed record Node(
        Branch Branch,
        bool Executable,
        string[]? Redirect,
        Dictionary<string, Node> Children
    );

    readonly Node root;

    CommandTree(Node root) => this.root = root;

    public static CommandTree Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024)
            throw new IOException("The command report exceeds 32 MB.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        return new CommandTree(Read(document.RootElement, "", true));
    }

    public static async Task<CommandTree?> LoadVanilla(
        HttpClient http,
        string cacheDirectory,
        string version,
        CancellationToken ct
    )
    {
        version = version.Trim().ToLowerInvariant();
        if (
            !Regex.IsMatch(version, @"^[a-z0-9][a-z0-9._-]{0,63}$")
            || version.Contains("..", StringComparison.Ordinal)
            || version.EndsWith('.')
        )
            return null;
        var path = Store.SafePath(cacheDirectory, version + "-commands.json");
        if (File.Exists(path))
        {
            try
            {
                return Load(path);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            { }
        }
        using var response = await http.GetAsync(
            $"https://raw.githubusercontent.com/misode/mcmeta/{version}-summary/commands/data.json",
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(cacheDirectory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Open(temp, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[32768];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) != 0)
                {
                    if (output.Length + read > 32L * 1024 * 1024)
                        throw new IOException("The command report exceeds 32 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            var tree = Load(temp);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
            return tree;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    static Node Read(
        JsonElement element,
        string name,
        bool root = false,
        bool nestedCommand = false
    )
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || (
                root
                    ? type.GetString() != "root"
                    : type.GetString() is not ("literal" or "argument")
            )
            || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        )
            throw new InvalidDataException("Invalid Minecraft command report.");
        var argument = type.GetString() == "argument";
        var parser =
            element.TryGetProperty("parser", out var parserNode)
            && parserNode.ValueKind == JsonValueKind.String
                ? parserNode.GetString()!
                : "";
        var executable =
            element.TryGetProperty("executable", out var flag)
            && flag.ValueKind == JsonValueKind.True;
        var redirect =
            element.TryGetProperty("redirect", out var target)
            && target.ValueKind == JsonValueKind.Array
                ? target
                    .EnumerateArray()
                    .Select(part =>
                        part.ValueKind == JsonValueKind.String
                            ? part.GetString()!
                            : throw new InvalidDataException("Invalid command redirect.")
                    )
                    .ToArray()
            : nestedCommand
            && name == "run"
            && !argument
            && !executable
            && !element.TryGetProperty("children", out _)
                ? []
            : null;
        var children = new Dictionary<string, Node>(StringComparer.Ordinal);
        if (
            element.TryGetProperty("children", out var entries)
            && entries.ValueKind == JsonValueKind.Object
        )
            foreach (var property in entries.EnumerateObject())
                if (
                    !children.TryAdd(
                        property.Name,
                        Read(
                            property.Value,
                            property.Name,
                            nestedCommand: nestedCommand
                                || (
                                    root
                                    && name.Length == 0
                                    && property.Name is "execute" or "return"
                                )
                        )
                    )
                )
                    throw new InvalidDataException("Duplicate command branch.");
        var registry = element.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("registry", out var registryNode)
            && registryNode.ValueKind == JsonValueKind.String
            ? registryNode.GetString()
            : null;
        bool HasProperty(string property, string value) =>
            properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(property, out var entry)
            && entry.ValueKind == JsonValueKind.String
            && entry.GetString() == value;
        return new(
            new(name, argument, parser, registry, HasProperty("amount", "single"), HasProperty("type", "players")),
            executable, redirect, children
        );
    }

    Node Follow(Node node)
    {
        var seen = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        while (node.Redirect is { } redirect && seen.Add(node))
        {
            var redirected = root;
            foreach (var step in redirect)
                if (!redirected.Children.TryGetValue(step, out redirected!))
                    return node;
            node = redirected;
        }
        return node;
    }

    Node Find(IEnumerable<string> path)
    {
        var node = root;
        foreach (var part in path)
            node = Follow(node).Children.TryGetValue(part, out var child)
                ? child
                : throw new ArgumentException("Invalid command branch.");
        return node;
    }

    public IReadOnlyList<Branch> Choices(IEnumerable<string> path) =>
        Follow(Find(path)).Children.Values.Select(node => node.Branch).ToArray();

    public bool IsExecutable(IEnumerable<string> path)
    {
        var node = Find(path);
        return node.Executable || Follow(node).Executable;
    }

    public string Preview(IReadOnlyList<string> path, IReadOnlyList<string> arguments)
    {
        if (path.Count == 0 || path.Count != arguments.Count)
            return "";
        var node = root;
        var tokens = new List<string>();
        for (var index = 0; index < path.Count; index++)
        {
            node = Follow(node).Children[path[index]];
            var value = node.Branch.Argument ? arguments[index].Trim() : node.Branch.Name;
            if (value.Length == 0 || value.Any(char.IsControl))
                return "";
            if (node.Branch.Parser == "brigadier:bool" && value is not ("true" or "false"))
                return "";
            if (node.Branch.Parser is ("minecraft:block_pos" or "minecraft:vec3" or "minecraft:column_pos" or "minecraft:vec2")
                && !CommandGenerator.ValidTreeCoordinates(node.Branch.Parser, value))
                return "";
            tokens.Add(value);
        }
        return (node.Executable || Follow(node).Executable) ? "/" + string.Join(" ", tokens) : "";
    }
}
