namespace CMCLauncher;

// 日志脱敏：在写入日志前抹去其中的凭据（API key、Bearer token、
// 各类 token/密码字段），避免敏感信息泄露到日志文件。
public static class LogRedactor
{
    // 把文本中的敏感凭据替换为 [REDACTED]；凭据资源读取失败时不影响日志流程。
    public static string Redact(string value)
    {
        try
        {
            if (CurseForgeCredentials.ConfiguredApiKey is { Length: > 0 } key)
                value = value.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        }
        catch
        { /* An invalid credential resource must not break logging. */
        }
        value = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?i)\bBearer\s+[^\s""'&,}]+",
            "Bearer [REDACTED]"
        );
        foreach (
            var key in new[]
            {
                "accessToken",
                "access_token",
                "client_secret",
                "refresh_token",
                "id_token",
                "device_code",
                "authorization",
                "password",
                "apiKey",
                "api_key",
                "x-api-key",
            }
        )
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                $"(?i)({key}[\"']?\\s*[=: ]+\\s*[\"']?)([^&\\s\"',}}]+)",
                "$1[REDACTED]"
            );
        return value;
    }
}
