using System.Text.RegularExpressions;

namespace CMCLauncher;

public static partial class JavaManager
{
    public sealed record JavaBrand(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<JavaBrand> DownloadBrands(int major)
    {
        yield return new("temurin", "Eclipse Temurin");
        if (MinecraftPlatform.OsName != "windows" || MinecraftPlatform.Architecture != "x86_64")
            yield break;
        if (major is 8 or 11 or 17 or 21 or 25)
        {
            yield return new("corretto", "Amazon Corretto");
            yield return new("zulu", "Azul Zulu");
            yield return new("liberica", "BellSoft Liberica");
        }
        if (major is 11 or 17 or 21 or 25)
            yield return new("microsoft", "Microsoft Build of OpenJDK");
        if (major is 17 or 21 or 25)
            yield return new("sapmachine", "SAP SapMachine");
    }

    static async Task<(string Url, string Hash, long? Size, string Algorithm)> ResolveAdditionalJava(
        LauncherCore core, int major, string brand, CancellationToken ct
    )
    {
        if (!DownloadBrands(major).Any(option => option.Id == brand))
            throw new PlatformNotSupportedException("Unsupported Java distribution or platform.");

        const string azul = "https://api.azul.com/metadata/v1/zulu/packages/";
        if (brand == "zulu")
        {
            var list = await core.Json(
                $"{azul}?java_version={major}&os=windows&arch=x64&archive_type=zip&java_package_type=jdk&javafx_bundled=false&release_status=ga&availability_types=CA&latest=true", ct
            );
            var asset = list.Items().FirstOrDefault(item =>
                Regex.IsMatch(item.S("name"), @"-ca-jdk.*-win_x64\.zip$")
            );
            if (!Guid.TryParse(asset?.S("package_uuid"), out var id))
                throw new IOException("No compatible Azul Zulu release found.");
            var details = await core.Json(azul + id, ct);
            return (details.S("download_url"), details.S("sha256_hash"),
                long.TryParse(details.S("size"), out var size) ? size : null, "sha256");
        }
        if (brand == "microsoft")
        {
            const string endpoint = "https://aka.ms/download-jdk/";
            using var request = new HttpRequestMessage(HttpMethod.Head,
                $"{endpoint}microsoft-jdk-{major}-windows-x64.zip");
            using var response = await core.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var uri = response.RequestMessage?.RequestUri;
            var file = uri == null ? "" : Path.GetFileName(uri.AbsolutePath);
            if (!Regex.IsMatch(file, $@"^microsoft-jdk-{major}(?:\.[0-9]+)+-windows-x64\.zip$"))
                throw new IOException("No compatible Microsoft OpenJDK release found.");
            var checksum = await core.Http.GetStringAsync(endpoint + file + ".sha256sum.txt", ct);
            return (uri!.AbsoluteUri, Regex.Match(checksum, @"^\s*([a-fA-F0-9]{64})\b").Groups[1].Value, null, "sha256");
        }
        if (brand == "liberica")
        {
            var assets = await core.Json(
                $"https://api.bell-sw.com/v1/liberica/releases?version-feature={major}&os=windows&arch=x86&bitness=64&package-type=zip&bundle-type=jdk", ct
            );
            var asset = assets.Items().Where(item => item["GA"]?.GetValue<bool>() == true)
                .OrderByDescending(item => item["latestInFeatureVersion"]?.GetValue<bool>() == true)
                .ThenByDescending(item => item["updateVersion"]?.GetValue<int>() ?? 0)
                .ThenByDescending(item => item["patchVersion"]?.GetValue<int>() ?? 0)
                .ThenByDescending(item => item["buildVersion"]?.GetValue<int>() ?? 0)
                .FirstOrDefault() ?? throw new IOException("No compatible BellSoft Liberica release found.");
            return (asset.S("downloadUrl"), asset.S("sha1"),
                long.TryParse(asset.S("size"), out var size) ? size : null, "sha1");
        }
        if (brand == "sapmachine")
        {
            var releases = await core.Json("https://api.github.com/repos/SAP/SapMachine/releases?per_page=30", ct);
            var asset = releases.Items()
                .Where(release => release["prerelease"]?.GetValue<bool>() == false)
                .SelectMany(release => release["assets"].Items())
                .FirstOrDefault(item => Regex.IsMatch(item.S("name"),
                    $@"^sapmachine-jdk-{major}(?:\.[0-9]+)*_windows-x64_bin\.zip$"))
                ?? throw new IOException("No compatible SAP SapMachine release found.");
            return (asset.S("browser_download_url"), asset.S("digest").Replace("sha256:", ""),
                long.TryParse(asset.S("size"), out var size) ? size : null, "sha256");
        }
        throw new PlatformNotSupportedException("Unsupported Java distribution.");
    }
}
