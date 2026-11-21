using System.Buffers.Binary;
using System.Net.Http.Headers;

namespace CMCLauncher;

public static class SkinImport
{
    const int MaximumBytes = 1024 * 1024;
    static readonly HttpClient imports = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public const int PreviewSize = 256;

    public static byte[] RenderPreview(byte[] texture, int height, bool slim) =>
        MinecraftPfp.Render(texture, 64, height, PreviewSize, true, 8, null, true, false, slim, null, 0);

    public static bool IsStandardSkin(ReadOnlySpan<byte> png) =>
        png.Length is >= 33 and <= MaximumBytes
        && png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        && BinaryPrimitives.ReadUInt32BigEndian(png[8..12]) == 13
        && png[12..16].SequenceEqual("IHDR"u8)
        && BinaryPrimitives.ReadUInt32BigEndian(png[16..20]) == 64
        && BinaryPrimitives.ReadUInt32BigEndian(png[20..24]) is 32 or 64;

    public static async Task<byte[]> Read(Stream source, CancellationToken ct)
    {
        using var image = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (image.Length + count > MaximumBytes)
                throw new InvalidDataException("Skin file must be 1 MB or smaller.");
            image.Write(buffer, 0, count);
        }
        var bytes = image.ToArray();
        if (!IsStandardSkin(bytes))
            throw new InvalidDataException("Select a 64×64 or 64×32 PNG skin.");
        return bytes;
    }

    public static async Task<byte[]> Download(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            throw new ArgumentException("Enter a direct HTTPS link to a PNG skin.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await imports.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw new IOException("Could not download the skin. Use a direct HTTPS image link.");
        if (response.Content.Headers.ContentLength > MaximumBytes)
            throw new InvalidDataException("Skin file must be 1 MB or smaller.");
        await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
        return await Read(source, deadline.Token);
    }

    public static async Task Apply(
        LauncherCore core, Account account, byte[] png, bool slim, CancellationToken ct
    )
    {
        if (account.Type != "microsoft" || !MicrosoftAuth.IsVerifiedMinecraftPlayer(account))
            throw new InvalidOperationException("Select a verified Minecraft Java account.");
        if (!IsStandardSkin(png))
            throw new InvalidDataException("Select a 64×64 or 64×32 PNG skin.");
        if (slim && BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)) == 32)
            throw new InvalidDataException("Slim arms require a 64×64 skin.");
        var token = CredentialVault.Read(account).S("accessToken");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Refresh the Minecraft account before applying the skin.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.minecraftservices.com/minecraft/profile/skins");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var form = new MultipartFormDataContent();
        request.Content = form;
        form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "skin.png");
        using var response = await core.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Minecraft rejected the skin upload ({(int)response.StatusCode}).");
    }
}
