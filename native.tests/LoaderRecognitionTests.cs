using System.Text.Json.Nodes;
using CMCLauncher;

static class LoaderRecognitionTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        JsonNode Profile(string main, string[] libraries, params string[] arguments) =>
            new JsonObject
            {
                ["mainClass"] = main,
                ["clientVersion"] = "1.21.1",
                ["libraries"] = new JsonArray(
                    libraries.Select(n => (JsonNode)new JsonObject { ["name"] = n }).ToArray()
                ),
                ["arguments"] = new JsonObject
                {
                    ["game"] = new JsonArray(arguments.Select(n => JsonValue.Create(n)).ToArray()),
                },
            };
        var neo = Profile(
            "cpw.mods.bootstraplauncher.BootstrapLauncher",
            [
                "net.neoforged.fancymodloader:loader:4.0.45",
                "net.minecraftforge:srgutils:0.4.15",
                "net.fabricmc:fabric-loader:0.16.0",
            ],
            "--fml.neoForgeVersion",
            "21.1.200",
            "--launchTarget",
            "forgeclient"
        );
        Check(
            LauncherCore.IdentifyLoader(neo) == ("neoforge", "21.1.200"),
            "NeoForge launch metadata outranks compatibility libraries"
        );
        var fabric = Profile(
            "net.fabricmc.loader.impl.launch.knot.KnotClient",
            ["net.fabricmc:fabric-loader:0.16.0", "net.neoforged:bus:8.0.5"]
        );
        Check(
            LauncherCore.IdentifyLoader(fabric) == ("fabric", "0.16.0"),
            "Fabric main class wins over unrelated NeoForge libraries"
        );
        Check(
            LauncherCore.IdentifyLoader(
                Profile(
                    "org.quiltmc.loader.impl.launch.knot.KnotClient",
                    ["net.fabricmc:fabric-loader:0.16.0", "org.quiltmc:quilt-loader:0.27.0"]
                )
            ) == ("quilt", "0.27.0"),
            "Quilt is not mistaken for Fabric"
        );
        Check(
            LauncherCore
                .IdentifyLoader(
                    Profile(
                        "cpw.mods.bootstraplauncher.BootstrapLauncher",
                        ["net.neoforged:forge:1.20.1-47.1.0"]
                    )
                )
                ?.Loader == "neoforge",
            "early NeoForge coordinates recognized"
        );
        Check(
            LauncherCore
                .IdentifyLoader(
                    Profile(
                        "net.minecraft.launchwrapper.Launch",
                        ["net.minecraftforge:forge:1.12.2-14.23.5.2860"]
                    )
                )
                ?.Loader == "forge",
            "legacy Forge recognized"
        );
        Check(
            LauncherCore
                .IdentifyLoader(
                    Profile(
                        "net.minecraft.client.main.Main",
                        ["net.minecraftforge:srgutils:0.4.15"]
                    )
                )
                ?.Loader == "vanilla",
            "helper libraries alone do not imply a loader"
        );
        Check(
            LauncherCore.IdentifyLoader(new JsonObject { ["mainClass"] = "custom.Main" }) == null,
            "unknown profiles preserve existing metadata"
        );
        var store = new Store(root);
        var minecraft = Path.Combine(root, "minecraft");
        var parent = Profile("net.minecraft.client.main.Main", []);
        await Store.AtomicJson(Store.SafePath(minecraft, "versions/base/base.json"), parent);
        neo["inheritsFrom"] = "base";
        await Store.AtomicJson(
            Store.SafePath(minecraft, "versions/Better MTR/Better MTR.json"),
            neo
        );
        var instance = new Instance
        {
            Name = "Better MTR",
            GameVersion = "Better MTR",
            Loader = "fabric",
            LoaderVersion = "old",
            VersionId = "Better MTR",
            ImportRoot = minecraft,
            Directory = minecraft,
            Description = "Keep description",
        };
        store.Config.Instances.Add(instance);
        var missing = new Instance
        {
            Name = "Missing",
            Loader = "quilt",
            VersionId = "missing",
            ImportRoot = minecraft,
        };
        store.Config.Instances.Add(missing);
        await Store.AtomicJson(
            Store.SafePath(minecraft, "versions/cycle/cycle.json"),
            new JsonObject { ["inheritsFrom"] = "cycle" }
        );
        var cyclic = new Instance
        {
            Loader = "forge",
            VersionId = "cycle",
            ImportRoot = minecraft,
        };
        store.Config.Instances.Add(cyclic);
        await Store.AtomicJson(Store.SafePath(minecraft, "versions/bad/bad.json"), new JsonArray());
        var bad = new Instance
        {
            Loader = "quilt",
            VersionId = "bad",
            ImportRoot = minecraft,
        };
        store.Config.Instances.Add(bad);
        using var core = new LauncherCore(store);
        await core.RefreshImportedMetadata();
        Check(
            instance.Loader == "neoforge"
                && instance.LoaderVersion == "21.1.200"
                && instance.GameVersion == "1.21.1",
            "stale imported loader and game version repaired from inherited local profile"
        );
        Check(
            instance.Name == "Better MTR" && instance.Description == "Keep description",
            "repair preserves user-facing instance metadata"
        );
        Check(
            missing.Loader == "quilt" && cyclic.Loader == "forge" && bad.Loader == "quilt",
            "missing, cyclic and malformed profiles do not interrupt refresh or reset records"
        );
        Check(
            new Store(root).Config.Instances.Single(i => i.Id == instance.Id).Loader == "neoforge",
            "corrected metadata persisted"
        );
        instance.Loader = "fabric";
        await core.VersionFor(instance, CancellationToken.None);
        Check(
            instance.Loader == "neoforge",
            "launch-time resolution also refreshes loader metadata"
        );
        var first = new Instance
        {
            Name = "Pack A",
            GameVersion = "1.21.1",
            Loader = "neoforge",
            LoaderVersion = "21.1.262",
            VersionId = "neoforge-21.1.262",
            StorageRoot = minecraft,
            Directory = Store.SafePath(minecraft, "versions/Pack A"),
        };
        var second = new Instance
        {
            Name = "Pack B",
            GameVersion = "1.21.1",
            Loader = "neoforge",
            LoaderVersion = "21.1.265",
            VersionId = "neoforge-21.1.265",
            StorageRoot = minecraft,
            Directory = Store.SafePath(minecraft, "versions/Pack B"),
        };
        store.Config.Instances.AddRange([first, second]);
        await core.WriteIsolatedProfile(
            first,
            Profile(
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                ["net.neoforged:neoforge:21.1.262"]
            )
        );
        await core.WriteIsolatedProfile(
            second,
            Profile(
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                ["net.neoforged:neoforge:21.1.265"]
            )
        );
        foreach (var item in new[] { first, second, first, second })
        {
            var resolved = await core.VersionFor(item, default);
            Check(
                LauncherCore.IdentifyLoader(resolved)?.Version == item.LoaderVersion
                    && resolved.S("id") == item.Name,
                "switching isolated instances resolves each saved profile without the installer profile"
            );
        }
        Check(
            new Store(root).Config.Instances.Single(i => i.Id == first.Id).VersionId == "Pack A",
            "recovered isolated profile reference persists"
        );
        first.VersionId = "missing-installer";
        first.LoaderVersion = "21.1.999";
        try
        {
            await core.VersionFor(first, default);
            throw new Exception("A different loader build must not be silently selected");
        }
        catch (FileNotFoundException) { }
    }
}
