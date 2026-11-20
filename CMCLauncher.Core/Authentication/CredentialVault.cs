using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// 账号凭据保险库：负责把账号令牌等敏感数据加密后落盘，以及读取时解密还原。
// 实际加解密由平台层（Windows 下为 DPAPI）通过 TransformCredential 完成。
public static class CredentialVault
{
    // 加密凭据：将 JSON 凭据序列化为 UTF-8 字节，经平台层加密后转为 Base64 字符串存储。
    public static string Protect(JsonNode value) =>
        Convert.ToBase64String(
            LauncherPlatform.Current.TransformCredential(
                Encoding.UTF8.GetBytes(value.ToJsonString()),
                true
            )
        );

    // 读取凭据：离线账号直接返回固定的 accessToken；
    // 在线账号则把存储的 Base64 密文解密还原为 JSON 凭据对象。
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
