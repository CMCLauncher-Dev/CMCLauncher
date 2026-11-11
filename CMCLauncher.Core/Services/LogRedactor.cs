namespace CMCLauncher;

public static class LogRedactor
{
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
