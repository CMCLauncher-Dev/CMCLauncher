using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// Mod 包导出引用解析：把本地 Mod 文件解析为 Modrinth/CurseForge 的远程引用，
// 使导出的 Mod 包不内嵌这些文件，而是按哈希/ID 指向社区源。
// 解析失败（网络、API、数据异常）时返回 null，调用方会回退为内嵌。
public static class ModpackExportReferences
{
    // 解析单个文件为引用；任何异常都吞掉并返回 null（不中断导出）。
    public static async Task<JsonNode?> Resolve(
        LauncherCore core,
        string root,
        string relative,
        string format,
        CancellationToken ct
    )
    {
        try
        {
            return await ResolveCore(core, root, relative, format, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (CurseForgeApiException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    // 实际解析逻辑：读取 .craft-mods-native.json 中的跟踪记录，
    // Modrinth 按 SHA512 哈希查询版本文件，CurseForge 按 project/file ID 查询并校验 SHA1。
    static async Task<JsonNode?> ResolveCore(
        LauncherCore core,
        string root,
        string relative,
        string format,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var recordsPath = Path.Combine(root, ".craft-mods-native.json");
        var records = await ModrinthService.ReadModRecords(recordsPath, ct);
        var tracked =
            relative.StartsWith("mods/", StringComparison.Ordinal)
            && relative.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                ? records.Items().FirstOrDefault(r => "mods/" + r.S("file") == relative)
                : null;
        if (
            format == "modrinth"
            && (
                tracked == null
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    tracked.S("project"),
                    "^[A-Za-z0-9]{8}$"
                )
            )
        )
            return null;
        var full = Store.SafePath(root, relative);
        await using var stream = File.OpenRead(full);
        if (format == "modrinth")
        {
            var hash = Convert
                .ToHexString(await StreamHash.ComputeAsync(SHA512.Create(), stream, ct))
                .ToLowerInvariant();
            var version = await core.Json(
                "https://api.modrinth.com/v2/version_file/" + hash + "?algorithm=sha512",
                ct
            );
            var file = version["files"]
                .Items()
                .FirstOrDefault(f =>
                    f["hashes"].S("sha512").Equals(hash, StringComparison.OrdinalIgnoreCase)
                );
            if (file == null || file.S("url").Length == 0)
                return null;
            return new JsonObject
            {
                ["hashes"] = file["hashes"]!.DeepClone(),
                ["fileSize"] = stream.Length,
                ["downloads"] = new JsonArray(file.S("url")),
            };
        }
        var record = records
            .Items()
            .FirstOrDefault(r =>
                "mods/" + r.S("file") == relative
                && CurseForgeCommunityService.IsProject(r.S("project"))
                && CurseForgeCommunityService.IsProject(r.S("version"))
            );
        if (record == null)
            return null;
        if (
            !int.TryParse(record.S("project")[11..], out var project)
            || project <= 0
            || !int.TryParse(record.S("version")[11..], out var id)
            || id <= 0
        )
            return null;
        if (CurseForgeCredentials.ConfiguredApiKey == null)
            return null;
        using var api = new CurseForgeClient(
            () => CurseForgeCredentials.ConfiguredApiKey,
            applicationAccessApproved: CurseForgeCredentials.ConfiguredApiKey != null
        );
        var metadata = (await api.Query($"mods/{project}/files/{id}", ct))["data"]!;
        var sha1 = Convert.ToHexString(await StreamHash.ComputeAsync(SHA1.Create(), stream, ct));
        if (
            metadata.S("isAvailable") != "true"
            || metadata.Int("modId") != project
            || metadata.Int("id") != id
            || !metadata["hashes"]
                .Items()
                .Any(h =>
                    h.Int("algo") == 1
                    && h.S("value").Equals(sha1, StringComparison.OrdinalIgnoreCase)
                )
        )
            return null;
        return new JsonObject
        {
            ["projectID"] = project,
            ["fileID"] = id,
            ["required"] = true,
        };
    }
}
