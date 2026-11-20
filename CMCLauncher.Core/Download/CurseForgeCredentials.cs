using System.Text.Json.Nodes;

namespace CMCLauncher;

// CurseForge API 凭据管理：从程序集内嵌资源中读取构建时注入的 API key。
// 发布构建会内嵌官方 key；本地开发构建未注入时返回 null，相关功能不可用。
public static class CurseForgeCredentials
{
    // 延迟加载的内嵌 key（只读取一次）。
    static readonly Lazy<string?> Key = new(ReadEmbeddedKey);

    // 当前构建配置的 API key；未配置时为 null。
    public static string? ConfiguredApiKey => Key.Value;

    // 获取 API key，未配置时抛出异常（调用方保证仅在需要 CurseForge 功能时使用）。
    public static string RequireApiKey() =>
        ConfiguredApiKey
        ?? throw new InvalidOperationException(
            "CurseForge API key is not configured in this build."
        );

    // 从内嵌资源 "CMCLauncher.CurseForgeApi" 中解析 JSON 并提取 apiKey 字段。
    static string? ReadEmbeddedKey()
    {
        using var stream = typeof(CurseForgeCredentials).Assembly.GetManifestResourceStream(
            "CMCLauncher.CurseForgeApi"
        );
        if (stream == null)
            return null;
        try
        {
            var key = JsonNode.Parse(stream)?["apiKey"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            throw new InvalidDataException("Invalid embedded CurseForge API configuration.");
        }
    }
}
