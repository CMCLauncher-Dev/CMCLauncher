using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public static partial class CommandGenerator
{
    static IEnumerable<CommandForm> ServerForms(Version version)
    {
        foreach (var command in new[] { "op", "deop", "pardon" })
            yield return new(command, $"/{command} <player>", [new("Target", "Steve")]);
        foreach (var command in new[] { "kick", "ban" })
            yield return new(
                command,
                $"/{command} <player> [reason]",
                [new("Target", "Steve"), new("Reason (optional)", "", true)]
            );
        yield return new(
            "ban-ip",
            "/ban-ip <IPv4|player> [reason]",
            [new("IPv4 address or player", "Steve"), new("Reason (optional)", "", true)]
        );
        yield return new("pardon-ip", "/pardon-ip <IPv4>", [new("IPv4 address", "192.0.2.10")]);
        yield return new(
            "banlist",
            "/banlist [players|ips]",
            [new("List type (optional)", "", true)]
        );
        yield return new(
            "whitelist",
            "/whitelist <on|off|list|reload|add|remove> [player]",
            [new("Whitelist action", "list"), new("Player (for add or remove)", "", true, EnabledForFirstField: ["add", "remove"])]
        );
        foreach (var command in new[] { "list", "save-on", "save-off", "stop" })
            yield return new(command, "/" + command, []);
        yield return version >= Flattening
            ? new("save-all", "/save-all [flush]", [new("Save option (optional)", "", true)])
            : new("save-all", "/save-all", []);
        if (version >= Flattening)
            yield return new("setidletimeout", "/setidletimeout <minutes>", [new("Minutes", "0")]);
    }

    static string GenerateServer(string name, string[] values)
    {
        static string Player(string value) =>
            Regex.IsMatch(value, @"^[A-Za-z0-9_]{1,16}$")
                ? value
                : throw new ArgumentException("Enter a Minecraft player name.");

        static bool IsIpv4(string value) =>
            Regex.IsMatch(value, @"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")
            && IPAddress.TryParse(value, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork;

        static string Optional(string value) => value.Length == 0 ? "" : " " + value;

        return name switch
        {
            "op" or "deop" or "pardon" => $"/{name} {Player(values[0])}",
            "ban" or "kick" => $"/{name} {Player(values[0])}" + Optional(values[1]),
            "ban-ip" => "/ban-ip "
                + (IsIpv4(values[0]) ? values[0] : Player(values[0]))
                + Optional(values[1]),
            "pardon-ip" => IsIpv4(values[0])
                ? "/pardon-ip " + values[0]
                : throw new ArgumentException("Enter a valid IPv4 address."),
            "banlist" when values[0] is "" or "players" or "ips" => "/banlist"
                + Optional(values[0]),
            "banlist" => throw new ArgumentException("Use players or ips for the ban list."),
            "whitelist" when values[0] is "add" or "remove" =>
                $"/whitelist {values[0]} {Player(values[1])}",
            "whitelist"
                when values[0] is "on" or "off" or "list" or "reload" && values[1].Length == 0 =>
                "/whitelist " + values[0],
            "whitelist" => throw new ArgumentException(
                "Enter a whitelist action and a player only for add or remove."
            ),
            "list" or "save-on" or "save-off" or "stop" => "/" + name,
            "save-all" when values.Length == 0 || values[0] is "" or "flush" => "/save-all"
                + Optional(values.FirstOrDefault() ?? ""),
            "save-all" => throw new ArgumentException("The only optional save argument is flush."),
            "setidletimeout" => "/setidletimeout " + Integer(values[0], 0, int.MaxValue),
            _ => throw new ArgumentException("Unsupported command."),
        };
    }
}
