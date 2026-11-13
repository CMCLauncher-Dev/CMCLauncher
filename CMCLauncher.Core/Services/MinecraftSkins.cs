using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// Shared account avatars use the existing one-day skin cache; fetches are coalesced per UUID.
public sealed class MinecraftSkins(LauncherCore core, string root, Action<string>? log = null)
{
    readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> requests = new();

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
