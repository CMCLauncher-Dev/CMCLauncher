using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// Shared account avatars use the existing one-day skin cache; fetches are coalesced per UUID.
public sealed class MinecraftSkins(LauncherCore core, string root, Action<string>? log = null)
{
    readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> requests = new();

    // 获取账号皮肤 PNG（按 UUID 合并并发请求，一天缓存）；
    // cacheOnly 为 true 时只读缓存、不发起网络请求。
    public Task<byte[]?> Get(string uuid, bool cacheOnly = false)
    {
        if (!Guid.TryParse(uuid, out var id))
            return Task.FromResult<byte[]?>(null);
        var key = id.ToString("N");
        if (requests.Count > 128)
            requests.Clear();
        return requests
            .GetOrAdd(key + (cacheOnly ? ":cache" : ""), _ => new(() => Fetch(key, cacheOnly)))
            .Value;
    }

    public async Task<byte[]> FindByName(string username, CancellationToken ct)
    {
        var name = username.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z0-9_]{1,16}\z"))
            throw new ArgumentException("Enter a Minecraft Java username.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await core.Http.GetAsync(
            "https://api.minecraftservices.com/minecraft/profile/lookup/name/" + Uri.EscapeDataString(name),
            timeout.Token
        );
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent)
            throw new InvalidOperationException("Minecraft Java player not found.");
        response.EnsureSuccessStatusCode();
        var profile = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (!Guid.TryParse(profile.S("id"), out var id))
            throw new InvalidOperationException("Minecraft Java player not found.");
        return await Get(id.ToString("N")).WaitAsync(timeout.Token)
            ?? throw new IOException("Could not load this player's skin. Try again or upload a PNG skin.");
    }

    public async Task RememberAppliedSkin(string uuid, byte[] png)
    {
        if (!Guid.TryParse(uuid, out var id))
            return;
        var key = id.ToString("N");
        var updated = new Lazy<Task<byte[]?>>(() => Task.FromResult<byte[]?>(png));
        requests.TryGetValue(key, out var previous);
        requests[key] = updated;
        requests[key + ":cache"] = updated;
        try
        {
            if (previous?.IsValueCreated == true)
                await previous.Value;
            var directory = Path.Combine(root, "skin-cache");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, key + ".png"), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("Skin cache update failed: " + ex.Message);
        }
    }

    // 实际拉取皮肤：优先读本地缓存（一天内有效），否则经 Mojang sessionserver
    // 解析 textures 属性并校验皮肤 URL（仅允许 textures.minecraft.net、https、默认端口），
    // 下载限 1 MB 并写入缓存；失败时回退到旧缓存。
    async Task<byte[]?> Fetch(string uuid, bool cacheOnly)
    {
        var cache = Path.Combine(root, "skin-cache", uuid + ".png");
        try
        {
            if (
                File.Exists(cache)
                && new FileInfo(cache).Length <= 1024 * 1024
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1)
            )
                return await File.ReadAllBytesAsync(cache);
            if (cacheOnly)
                return File.Exists(cache) && new FileInfo(cache).Length <= 1024 * 1024
                    ? await File.ReadAllBytesAsync(cache)
                    : null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var profile = await core.Json(
                "https://sessionserver.mojang.com/session/minecraft/profile/" + uuid,
                timeout.Token
            );
            var property = profile["properties"]
                .Items()
                .FirstOrDefault(p => p.S("name") == "textures");
            var textures = JsonNode.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(property.S("value")))
            );
            if (
                !Uri.TryCreate(
                    textures?["textures"]?["SKIN"].S("url"),
                    UriKind.Absolute,
                    out var uri
                )
                || uri.Host != "textures.minecraft.net"
                || uri.Scheme is not ("http" or "https")
                || uri.UserInfo.Length > 0
                || !uri.IsDefaultPort
            )
                return null;
            var secure = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
            using var response = await core.Http.GetAsync(
                secure,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024)
                return null;
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024)
                    return null;
                buffer.Write(bytes, 0, count);
            }
            var result = buffer.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            await File.WriteAllBytesAsync(cache, result, timeout.Token);
            return result;
        }
        catch (Exception ex)
        {
            log?.Invoke("Skin fetch failed for " + uuid + ": " + ex.Message);
            try
            {
                return File.Exists(cache) && new FileInfo(cache).Length <= 1024 * 1024
                    ? await File.ReadAllBytesAsync(cache)
                    : null;
            }
            catch (Exception cacheEx)
            {
                log?.Invoke("Skin cache read failed for " + uuid + ": " + cacheEx.Message);
                return null;
            }
        }
    }
}
