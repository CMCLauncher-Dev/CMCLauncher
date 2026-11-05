using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public static class CredentialVault
{
    public static string Protect(JsonNode value) =>
        Convert.ToBase64String(
            LauncherPlatform.Current.TransformCredential(
                Encoding.UTF8.GetBytes(value.ToJsonString()),
                true
            )
        );

    public static JsonNode Read(Account account) =>
        account.Type == "offline"
            ? new JsonObject { ["accessToken"] = "0" }
            : JsonNode.Parse(
                Encoding.UTF8.GetString(
                    LauncherPlatform.Current.TransformCredential(
                        Convert.FromBase64String(
                            account.Secret ?? throw new Exception("账号凭据不存在，请重新登录")
                        ),
                        false
                    )
                )
            )!;
}
