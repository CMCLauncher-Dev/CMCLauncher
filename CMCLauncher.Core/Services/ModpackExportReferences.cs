using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public static class ModpackExportReferences
{
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
        var records = File.Exists(recordsPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(recordsPath, ct))
            : null;
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
                .ToHexString(await SHA512.HashDataAsync(stream, ct))
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
        var sha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct));
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
