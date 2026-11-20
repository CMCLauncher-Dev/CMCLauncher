using System.Text.Json.Nodes;

namespace CMCLauncher;

// Terracotta 房间状态辅助：判断状态是否处于进行中/已有房间，
// 并把状态 JSON 翻译为人类可读的提示文案与成员列表。
public static class TerracottaStatus
{
    // 是否处于“进行中”状态（扫描/启动/连接/建立中）。
    public static bool IsPending(string? state) =>
        state is "host-scanning" or "host-starting" or "guest-connecting" or "guest-starting";

    // 是否已处于某个房间（进行中或已就绪）。
    public static bool HasRoom(string? state) =>
        IsPending(state) || state is "host-ok" or "guest-ok";

    // 把状态 JSON 翻译为人类可读的提示文案（含异常类型的细分说明）。
    public static string Description(JsonNode? state) =>
        state?["state"]?.GetValue<string>() switch
        {
            "waiting" => "Ready to create or join a room.",
            "host-scanning" => "Open your Minecraft world to LAN to create the room.",
            "host-starting" => "Creating room…",
            "host-ok" => "Room is ready. Share the room code with your friends.",
            "guest-connecting" => "Connecting to room…",
            "guest-starting" => "Establishing connection…",
            "guest-ok" => "Connected. Add the server address in Minecraft multiplayer.",
            "exception" => state["type"]?.GetValue<int>() switch
            {
                0 => "Could not reach the host. Check the room code and network.",
                1 => "Connection to the host was lost.",
                2 or 3 =>
                    "Terracotta networking stopped unexpectedly. Leave the room and try again.",
                4 => "The host closed the LAN world.",
                _ => "Terracotta could not establish the connection. Leave the room and try again.",
            },
            _ => "Terracotta is not running.",
        };

    // 提取当前房间成员名列表（过滤空名）。
    public static string[] Members(JsonNode? state) =>
        state?["profiles"] is JsonArray profiles
            ? profiles
                .Select(p => p?["name"]?.GetValue<string>())
                .OfType<string>()
                .Where(n => n.Length > 0)
                .ToArray()
            : [];
}
