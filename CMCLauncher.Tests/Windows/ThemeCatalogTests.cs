using CMCLauncher;

static class ThemeCatalogTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        var warnings = new List<string>();
        var catalog = new ThemeCatalog(root, warnings.Add);
        Check(
            catalog.Presets.Count == 8
                && catalog.Presets[0].Id == "craft"
                && catalog.Presets[0].IsBuiltIn,
            "Eight built-in themes are available when external files are absent"
        );
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#F6F8F7"
                && catalog.Resolve("craft", "DarkBg", false, "#000000") == "#1B1C1B",
            "Embedded JSON preserves the existing light and dark palettes"
        );
        var builtInIds = catalog.Presets.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
        Directory.CreateDirectory(root);
        var external = Path.Combine(root, "ocean.json");
        await File.WriteAllTextAsync(
            external,
            """{"version":1,"id":"ocean","displayName":"Ocean","colors":{"LightBg":"#123456"},"dark":{"DarkBg":"#112233"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Presets.Any(p => p.Id == "ocean" && !p.IsBuiltIn),
            "Additional external themes are discoverable"
        );
        Check(
            catalog.Resolve("ocean", "LightBg", true, "#000000") == "#123456"
                && catalog.Resolve("ocean", "DarkBg", false, "#000000") == "#112233",
            "External palettes and mode overrides are applied"
        );
        Check(
            catalog.Resolve("ocean", "LightText", true, "#000000") == "#242825",
            "Partial external themes inherit missing colors from Fresh green"
        );
        await File.WriteAllTextAsync(external, "{invalid");
        catalog.Reload();
        Check(
            !catalog.Presets.Any(p => p.Id == "ocean") && warnings.Count > 0,
            "Invalid JSON is ignored and diagnosed"
        );
        var craft = Path.Combine(root, "craft.json");
        await File.WriteAllTextAsync(
            craft,
            """{"id":"craft","displayName":"Fresh green override","colors":{"LightBg":"#ABCDEF"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#ABCDEF"
                && catalog.Resolve("craft", "DarkBg", false, "#000000") == "#1B1C1B",
            "External edits override the built-in palette while preserving missing colors"
        );
        await File.WriteAllTextAsync(
            craft,
            """{"id":"craft","displayName":"Invalid","colors":{"LightBg":"#ffffff","DarkBg":"bad"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#F6F8F7",
            "Invalid color rejects an entire override atomically"
        );
        foreach (var id in new[] { "native", "custom" })
            await File.WriteAllTextAsync(
                Path.Combine(root, id + ".json"),
                $$$"""{"id":"{{{id}}}","displayName":"Hijack","colors":{"LightBg":"#FFFFFF"}}"""
            );
        await File.WriteAllTextAsync(
            external,
            """{"id":"wrong","displayName":"Mismatch","colors":{"LightBg":"#FFFFFF"}}"""
        );
        catalog.Reload();
        Check(
            builtInIds.SetEquals(catalog.Presets.Select(preset => preset.Id))
                && catalog.Resolve("native", "LightBg", true, "#010203") == "#010203",
            "JSON cannot register reserved native/custom IDs or a mismatched file ID"
        );
        await File.WriteAllTextAsync(
            external,
            """{"id":"ocean","displayName":"Null","colors":null}"""
        );
        catalog.Reload();
        Check(catalog.Presets.Count == 8, "Null color collections do not prevent startup");
        File.Delete(craft);
        catalog.Reload();
        Check(
            catalog.Resolve("removed-theme", "DarkBg", false, "#000000") == "#1B1C1B",
            "Removed themes fall back to the built-in palette"
        );
        await File.WriteAllTextAsync(external, new string(' ', 256_001));
        catalog.Reload();
        Check(catalog.Presets.Count == 8, "Oversized files are ignored");

        var bundledDirectory = BundledResources.DefaultDirectory(root);
        Directory.CreateDirectory(bundledDirectory);
        var bundledMint = Path.Combine(bundledDirectory, "mint.json");
        var mintJson =
            """{"version":1,"id":"mint","displayName":"Mint","colors":{"LightBg":"#0A0B0C"}}""";
        await File.WriteAllTextAsync(bundledMint, mintJson);
        var userMint = Path.Combine(root, "mint.json");
        await File.WriteAllTextAsync(
            userMint,
            """{"version":1,"id":"mint","displayName":"Mint","colors":{"LightBg":"#0D0E0F"}}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "zest.json"),
            """{"version":1,"id":"zest","displayName":"Zest","colors":{"LightBg":"#101112"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Presets.Select(p => p.Id).TakeLast(2).SequenceEqual(new[] { "mint", "zest" })
                && catalog.Presets.Single(p => p.Id == "mint").IsBuiltIn
                && !catalog.Presets.Single(p => p.Id == "zest").IsBuiltIn,
            "Default/ themes are built in and listed before user themes"
        );
        Check(
            catalog.Resolve("mint", "LightBg", true, "#000000") == "#0D0E0F",
            "A user theme overrides the bundled Default/ theme with the same ID"
        );
        File.Delete(userMint);
        catalog.Reload();
        Check(
            catalog.Resolve("mint", "LightBg", true, "#000000") == "#0A0B0C",
            "Removing the user copy falls back to the bundled Default/ theme"
        );

        // 与 Default/ 完全一致（仅换行不同）或与历史自带版本一致的外层副本视为未修改，启动时清理。
        await File.WriteAllTextAsync(userMint, "\uFEFF" + mintJson + "\r\n");
        await using (
            var embedded = typeof(ThemeCatalog).Assembly.GetManifestResourceStream(
                "CMCLauncher.Themes.craft.json"
            )!
        )
        await using (var copy = File.Create(Path.Combine(root, "craft.json")))
            await embedded.CopyToAsync(copy);
        var edited = Path.Combine(root, "zest.json");
        _ = new ThemeCatalog(root, warnings.Add);
        Check(
            !File.Exists(userMint)
                && !File.Exists(Path.Combine(root, "craft.json"))
                && File.Exists(edited)
                && File.Exists(bundledMint),
            "Unmodified bundled copies are removed while user themes and Default/ stay"
        );
    }
}
