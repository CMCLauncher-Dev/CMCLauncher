using System.Text.Json;
using CMCLauncher.Core.Minecraft.DataPacks;

static class DataPackAdvancedTests
{
    public static void Run()
    {
        void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new Exception("Invalid advanced data pack entry was accepted.");
        }
        DataPackEntry Change(DataPackEntry entry, params (string Key, string Value)[] changes)
        {
            var fields = entry.Fields.ToDictionary(p => p.Key, p => p.Value);
            foreach (var (key, value) in changes)
                fields[key] = value;
            return entry with { Fields = fields };
        }
        foreach (var version in DataPackGenerator.SupportedGameVersions)
        {
            var entries = Enum.GetValues<DataPackEntryKind>()
                .Select(kind =>
                    DataPackEntryEditor.Create(kind, kind.ToString().ToLowerInvariant())
                )
                .ToArray();
            var files = DataPackGenerator.Preview(
                new("Custom pack", version, Namespace: "custom") { Entries = entries }
            );
            if (files.Count != 9)
                throw new Exception("Advanced entry set is incomplete for " + version);
            foreach (
                var file in files.Where(p => p.Key.EndsWith(".json") || p.Key == "pack.mcmeta")
            )
                using (JsonDocument.Parse(file.Value)) { }
            var defaults = DataPackGenerator.Preview(
                new("Defaults", version) { Entries = DataPackEntryEditor.StarterEntries() }
            );
            if (defaults.Count != 8)
                throw new Exception("Advanced starters differ from the previous wizard.");
        }
        var shaped = Change(
            DataPackEntryEditor.Create(DataPackEntryKind.ShapedRecipe, "tools/hammer"),
            ("pattern", "AA\n B"),
            ("keys", "A=#minecraft:planks\nB=minecraft:stick"),
            ("count", "3")
        );
        using (
            var doc = JsonDocument.Parse(
                DataPackEntryEditor.Build(shaped, "1.21.2", "custom").Value
            )
        )
        {
            if (
                doc.RootElement.GetProperty("key").GetProperty("A").GetString()
                    != "#minecraft:planks"
                || doc.RootElement.GetProperty("result").GetProperty("count").GetInt32() != 3
            )
                throw new Exception("Shaped recipe parameters were not preserved.");
        }
        using (
            var doc = JsonDocument.Parse(
                DataPackEntryEditor.Build(shaped, "1.20.1", "custom").Value
            )
        )
            if (
                doc.RootElement.GetProperty("key").GetProperty("A").GetProperty("tag").GetString()
                != "minecraft:planks"
            )
                throw new Exception("Legacy tag ingredient was not adapted.");
        Reject(() =>
            DataPackEntryEditor.Build(
                Change(shaped, ("keys", "A=minecraft:stone")),
                "1.21.2",
                "custom"
            )
        );
        Reject(() =>
            DataPackEntryEditor.Build(Change(shaped, ("pattern", "AAAA")), "1.21.2", "custom")
        );
        Reject(() =>
            DataPackEntryEditor.Build(Change(shaped, ("count", "65")), "1.21.2", "custom")
        );
        var cooking = Change(
            DataPackEntryEditor.Create(DataPackEntryKind.CookingRecipe),
            ("type", "blasting"),
            ("time", "100")
        );
        Reject(() => DataPackEntryEditor.Build(cooking, "1.13", "custom"));
        DataPackEntryEditor.Build(cooking, "1.14", "custom");
        var loot = Change(
            DataPackEntryEditor.Create(DataPackEntryKind.LootTable),
            ("rolls", "5"),
            ("entries", "minecraft:apple,3,8")
        );
        using (
            var doc = JsonDocument.Parse(DataPackEntryEditor.Build(loot, "1.21.2", "custom").Value)
        )
        {
            var pool = doc.RootElement.GetProperty("pools")[0];
            if (
                pool.GetProperty("rolls").GetInt32() != 5
                || pool.GetProperty("entries")[0].GetProperty("weight").GetInt32() != 8
            )
                throw new Exception("Weighted loot parameters were not preserved.");
        }
        var advancement = Change(
            DataPackEntryEditor.Create(DataPackEntryKind.Advancement),
            ("title", "Title \"字\""),
            ("parent", "minecraft:story/root"),
            ("experience", "10"),
            ("rewardFunction", "custom:reward"),
            ("toast", "false")
        );
        using (
            var doc = JsonDocument.Parse(
                DataPackEntryEditor.Build(advancement, "1.21.2", "custom").Value
            )
        )
            if (
                doc.RootElement.GetProperty("rewards").GetProperty("function").GetString()
                    != "custom:reward"
                || doc.RootElement.GetProperty("display").GetProperty("show_toast").GetBoolean()
            )
                throw new Exception("Advancement rewards or display settings were lost.");
        var function = Change(
            DataPackEntryEditor.Create(DataPackEntryKind.Function, "init"),
            ("commands", "say Test\r\nsay Again"),
            ("load", "true"),
            ("tick", "true")
        ) with
        {
            Namespace = "other",
        };
        var another = Change(function with { Id = "second" }, ("tick", "false"));
        var filesWithHooks = DataPackGenerator.Preview(
            new("Hooks", "1.21.2") { Entries = [function, another] }
        );
        using (
            var doc = JsonDocument.Parse(filesWithHooks["data/minecraft/tags/function/load.json"])
        )
            if (
                !doc
                    .RootElement.GetProperty("values")
                    .EnumerateArray()
                    .Select(v => v.GetString())
                    .SequenceEqual(new[] { "other:init", "other:second" })
            )
                throw new Exception("Function hooks were not merged across entry namespaces.");
        if (filesWithHooks["data/other/function/init.mcfunction"].Contains('\r'))
            throw new Exception("Function line endings were not normalized.");
        Reject(() =>
            DataPackGenerator.Preview(new("Duplicate", "1.21.2") { Entries = [function, function] })
        );
        var custom = DataPackEntryEditor.Create(DataPackEntryKind.CustomJson, "sub/test") with
        {
            SourceOverride = "[{\"condition\":\"minecraft:random_chance\",\"chance\":0.5}]",
        };
        var raw = DataPackEntryEditor.Build(custom, "1.21.2", "custom");
        if (
            raw.Key != "data/custom/predicate/sub/test.json"
            || JsonDocument.Parse(raw.Value).RootElement.ValueKind != JsonValueKind.Array
        )
            throw new Exception("Custom JSON arrays or paths were not preserved.");
        Reject(() =>
            DataPackEntryEditor.Build(
                custom with
                {
                    SourceOverride = "{broken",
                },
                "1.21.2",
                "custom"
            )
        );
        try
        {
            DataPackEntryEditor.Build(
                custom with
                {
                    SourceOverride = "{\n broken",
                },
                "1.21.2",
                "custom"
            );
        }
        catch (DataPackJsonException ex)
        {
            if (ex.Line != 2)
                throw new Exception("JSON error line is incorrect.");
        }
        foreach (
            var id in new[]
            {
                "../escape",
                "/root",
                "nested//file",
                "con",
                "a/../../file",
                "MixedCase",
            }
        )
            Reject(() => DataPackEntryEditor.Build(custom with { Id = id }, "1.21.2", "custom"));
        Reject(() =>
            DataPackEntryEditor.Build(
                Change(custom, ("directory", "../../other")),
                "1.21.2",
                "custom"
            )
        );
        Reject(() =>
            DataPackEntryEditor.Build(
                function with
                {
                    SourceOverride = "say\0bad",
                },
                "1.21.2",
                "custom"
            )
        );
        DataPackEntryEditor.Build(Change(DataPackEntryEditor.Create(DataPackEntryKind.Tag), ("values", "minecraft:air")), "1.21.2", "custom");
        Reject(() => DataPackEntryEditor.Build(Change(DataPackEntryEditor.Create(DataPackEntryKind.ShapelessRecipe), ("ingredients", "minecraft:air")), "1.21.2", "custom"));
        // A full source override is authoritative even when guided fields cannot express its schema.
        DataPackEntryEditor.Build(
            Change(
                shaped with
                {
                    SourceOverride = "{\"type\":\"mod:custom\"}",
                },
                ("count", "invalid")
            ),
            "1.21.2",
            "custom"
        );
    }
}
