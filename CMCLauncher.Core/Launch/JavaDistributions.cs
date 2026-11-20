using System.Text.RegularExpressions;

namespace CMCLauncher;

// JavaManager 的部分类：负责解析各 Java 发行版（品牌）的下载地址与校验信息。
public static partial class JavaManager
{
    // Java 发行版品牌：Id 为内部标识，Name 为展示名称。
    public sealed record JavaBrand(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    // 按 Java 主版本号列出当前平台可下载的发行版品牌。
    // Temurin 全平台可用；其余品牌仅在 Windows x64 上提供，且按主版本过滤。
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

    // 解析指定品牌的最新 Java 发行版下载地址与校验信息（URL、哈希、大小、算法）。
    // 品牌不受支持或找不到兼容发行版时抛出异常。
    static async Task<(string Url, string Hash, long? Size, string Algorithm)> ResolveAdditionalJava(
        LauncherCore core, int major, string brand, CancellationToken ct
    )
    {
        if (!DownloadBrands(major).Any(option => option.Id == brand))
            throw new PlatformNotSupportedException("Unsupported Java distribution or platform.");

        const string azul = "https://api.azul.com/metadata/v1/zulu/packages/";
        if (brand == "zulu")
        {
            // 通过 Azul 元数据 API 查询最新的 GA 版 Zulu JDK（Windows x64 zip）。
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
            // 通过 aka.ms 重定向端点探测 Microsoft OpenJDK 的最新版本文件名，
            // 并下载对应的 .sha256sum.txt 获取校验和。
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
            // 通过 BellSoft API 查询 Liberica 发行版，优先选择 GA 且为特性版本内最新的构建。
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
            // 通过 GitHub Releases API 查询 SAP SapMachine 的最新正式版 Windows x64 包。
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
