using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CMCLauncher;

// Runs without a Windows adapter or desktop SDK, also on Linux/macOS CI workers.
var root = Path.Combine(Path.GetTempPath(), "cmc-core-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    OptionsBackupTests.Run(Path.Combine(root, "options-backups"));
    Check(
        CommandGenerator.Generate("24w10a", CommandGenerator.CustomCommand, ["/minecraft:execute", "as @a run say hello"])
            == "/minecraft:execute as @a run say hello"
        && CommandGenerator.Generate("", CommandGenerator.CustomCommand, ["reload"]) == "/reload",
        "generic commands preserve namespaced verbs and arguments without assuming a release"
    );
    Check(
        CommandGenerator.Generate("1.12.2", "give", ["Steve", "stone", "1", "", "", """{Unbreakable:1b,RepairCost:2}"""])
            == "/give Steve stone 1 0 {Unbreakable:1b,RepairCost:2}"
        && CommandGenerator.Generate("1.21.5", "give", ["Steve", "stone", "1", "", "", "[minecraft:custom_data={bytes:[B;1b,2b]},\nminecraft:custom_name={text:'[Sword]'}]"])
            == "/give Steve stone[minecraft:custom_data={bytes:[B;1b,2b]}, minecraft:custom_name={text:'[Sword]'}] 1",
        "advanced item editing preserves typed SNBT arrays and namespaced components"
    );
    Check(
        CommandGenerator.Generate("1.0", "give", ["Steve", "1", "2"]) == "/give Steve 1 2"
        && CommandGenerator.Generate("1.8", "give", ["Steve", "minecraft:stone", "1", "Demo"])
            == "/give Steve minecraft:stone 1 0 {display:{Name:\"Demo\"}}"
        && CommandGenerator.Generate("1.20.4", "give", ["Steve", "stone", "1", "Demo"])
            == "/give Steve stone{display:{Name:\"{\\\"text\\\":\\\"Demo\\\"}\"}} 1"
        && CommandGenerator.Generate("1.20.5", "give", ["Steve", "stone", "1", "Demo"])
            == "/give Steve stone[minecraft:custom_name=\"{\\\"text\\\":\\\"Demo\\\"}\"] 1"
        && CommandGenerator.Generate("1.21.5", "give", ["Steve", "stone", "1", "Demo"])
            == "/give Steve stone[minecraft:custom_name={text:\"Demo\"}] 1"
        && CommandGenerator.Generate("1.12.2", "effect", ["Steve", "1", "30", "0"])
            == "/effect Steve 1 30 0"
        && CommandGenerator.Generate("1.13", "effect", ["Steve", "speed", "30", "0"])
            == "/effect give Steve speed 30 0"
        && CommandGenerator.Forms("1.6.4").All(form => form.Name is not ("summon" or "fill")),
        "command generation follows legacy tags, item components and inline SNBT text transitions"
    );
    Check(
        CommandGenerator.Generate("1.0", "tp", ["Steve", "Alex"]) == "/tp Steve Alex"
        && CommandGenerator.Generate("1.21.5", "give", ["Steve", "stone", "1", "", """{"marker":true,"level":4}"""])
            == """/give Steve stone[minecraft:custom_data={"marker":1b,"level":4}] 1"""
        && CommandGenerator.Generate("1.12.2", "give", ["Steve", "stone", "1", "", """{"marker":true}"""])
            == """/give Steve stone 1 0 {"marker":1b}""",
        "old teleport syntax and custom NBT compounds match the target release"
    );
    Check(true, "options snapshots, verified recovery, retention, rollback, restart, logging boundaries and integrity");
    DataPackGeneratorTests.Run(Path.Combine(root, "data-packs"));
    Check(true, "data pack versions, safe paths, metadata and concurrent creation");
    var child = new System.Diagnostics.ProcessStartInfo("java");
    child.Environment["LD_LIBRARY_PATH"] = "/tmp/appimage/usr/lib";
    child.Environment["CMCLAUNCHER_ORIGINAL_LD_LIBRARY_PATH"] = "/system/custom/lib";
    CMCLauncher.Platform.PlatformCommand.SystemEnvironment(child);
    Check(
        child.Environment["LD_LIBRARY_PATH"] == "/system/custom/lib"
            && !child.Environment.ContainsKey("CMCLAUNCHER_ORIGINAL_LD_LIBRARY_PATH"),
        "AppImage libraries do not leak into child processes"
    );
    var assembly = typeof(LauncherCore).Assembly;
    Check(assembly.GetName().Name == "CMCLauncher.Core", "standalone core assembly");
    Check(
        !assembly
            .GetReferencedAssemblies()
            .Any(a =>
                a.Name!.StartsWith("Microsoft.UI")
                || a.Name.StartsWith("Microsoft.Windows")
                || a.Name == "CMCLauncher.Platform.Windows"
            ),
        "core has no Windows assembly dependency"
    );
    Check(
        !assembly
            .GetTypes()
            .SelectMany(t =>
                t.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Static
                        | BindingFlags.Instance
                )
            )
            .Any(m => m.IsDefined(typeof(DllImportAttribute))),
        "core has no P/Invoke"
    );
    var contracts = typeof(CMCLauncher.Platform.PlatformEnvironment).Assembly;
    Check(
        !contracts
            .GetReferencedAssemblies()
            .Any(a =>
                a.Name!.StartsWith("CMCLauncher.Core")
                || a.Name.StartsWith("Microsoft.UI")
                || a.Name.StartsWith("Microsoft.Windows")
            ),
        "platform contracts are independent of Core and Windows UI"
    );
    var portable = LauncherPlatform.Current;
    Check(
        portable.GetType() == typeof(LauncherPlatform),
        "portable host does not load Windows integration"
    );
    try
    {
        CredentialVault.Protect(new JsonObject { ["token"] = "fixture" });
        throw new Exception("Plaintext credential fallback");
    }
    catch (PlatformNotSupportedException)
    {
        Console.WriteLine("PASS secure credential adapter is required");
    }
    Check(
        CredentialVault.Read(new Account { Type = "offline" })["accessToken"]!.ToString() == "0",
        "offline identity needs no credential store"
    );
    Check(GameMemory.Resolve(new Settings()) > 0, "portable memory fallback");
    LauncherPlatform.Current = new MemoryPlatform();
    Check(
        GameMemory.Resolve(new Settings { AutoMemory = true }) == GameMemory.Calculate(8192, 4096),
        "memory capability injection"
    );
    LauncherPlatform.Current = portable;
    Check(GameMemory.Calculate(8192, 6144) == 4710, "HMCL low memory tier");
    Check(GameMemory.Calculate(32768, 16384) == 9728, "HMCL middle memory tier");
    Check(GameMemory.Calculate(65536, 60000) == 18502, "HMCL high memory tier");
    Check(GameMemory.Calculate(262144, 260000) == 24576, "HMCL 24 GiB ceiling");
    Check(GameMemory.Calculate(32768, 16000, false) == 768, "32-bit heap ceiling");
    Check(
        GameMemory.Calculate(8192, 0) == 512 && GameMemory.Calculate(8192, -1) == 2048,
        "low memory and unavailable probe fallback"
    );
    var segments = GameMemory.Distribution(32768, 18432, 8192);
    Check(
        segments == (.4375, .25, .3125),
        "memory segments use total physical memory as denominator"
    );
    var overflow = GameMemory.Distribution(8192, 1024, 4096);
    Check(
        overflow == (.875, .125, 0),
        "over-allocation clips at available RAM without rescaling used memory"
    );
    Check(
        LauncherNavigation
            .Groups.Select(g => g.Key)
            .SequenceEqual(new[] { "downloads", "settings", "more" }),
        "shared desktop menu groups preserve WinUI order"
    );
    Check(
        LauncherNavigation
            .Groups[0]
            .Entries.Select(e => e.AvaloniaRoute)
            .SequenceEqual(
                new[]
                {
                    "downloads",
                    "mod",
                    "modpack",
                    "datapack",
                    "resourcepack",
                    "shader",
                    "world",
                    "blueprints",
                }
            ),
        "shared resource navigation keeps maps before blueprints"
    );
    Check(
        LauncherNavigation
            .Groups.SelectMany(g => g.Entries)
            .All(e =>
                !string.IsNullOrWhiteSpace(e.Glyph) && !string.IsNullOrWhiteSpace(e.IconPath)
            ),
        "both desktop frontends have icons for every menu entry"
    );
    var cachedJar = Path.Combine(root, "cached-mod.jar");
    using (
        var zip = System.IO.Compression.ZipFile.Open(
            cachedJar,
            System.IO.Compression.ZipArchiveMode.Create
        )
    )
    {
        using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        writer.Write(
            "{\"schemaVersion\":1,\"id\":\"cached_fixture\",\"name\":\"Cached fixture\",\"version\":\"1.0\"}"
        );
    }
    ContentInspectionCache.ClearMemory();
    var beforeCache = ContentInspectionCache.InspectionCount;
    var cachedReads = await Task.WhenAll(
        Enumerable
            .Range(0, 4)
            .Select(_ => ContentInspectionCache.GetAsync(cachedJar, "mod", true, root))
    );
    Check(
        cachedReads.All(result => result.Item.Name == "Cached fixture")
            && ContentInspectionCache.InspectionCount == beforeCache + 1,
        "portable content inspection coalesces duplicate metadata reads"
    );
    ContentInspectionCache.ClearMemory();
    var diskRead = await ContentInspectionCache.GetAsync(cachedJar, "mod", false, root);
    Check(
        diskRead.Item.Name == "Cached fixture"
            && !diskRead.Item.Enabled
            && ContentInspectionCache.InspectionCount == beforeCache + 1,
        "portable content metadata cache survives memory reset and preserves enabled state"
    );
    var dependencyGame = Path.Combine(root, "dependency-game");
    var dependencyMods = Directory.CreateDirectory(Path.Combine(dependencyGame, "mods"));
    void DependencyJar(string name, string metadata)
    {
        using var zip = System.IO.Compression.ZipFile.Open(
            Path.Combine(dependencyMods.FullName, name + ".jar"),
            System.IO.Compression.ZipArchiveMode.Create
        );
        using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        writer.Write(metadata);
    }
    DependencyJar("provider", """{"id":"fixture_provider","version":"2.0.0"}""");
    DependencyJar("compatible", """{"id":"fixture_compatible","name":"Compatible","version":"1.0","depends":{"fixture_provider":["1.0.0","2.0.0"]}}""");
    DependencyJar("conflict", """{"id":"fixture_conflict","name":"Conflict","version":"1.0","depends":{"fixture_provider":["1.0.0","3.0.0"]}}""");
    var dependencyIssues = ModDependencyChecker.Check(dependencyGame);
    Check(
        dependencyIssues.Count == 1 && dependencyIssues[0].Kind == "conflict"
            && dependencyIssues[0].Args[0] == "Conflict",
        "Fabric dependency arrays accept any matching range and report incompatible ranges without crashing"
    );
    var skinRoot = Path.Combine(root, "skin-data");
    var skinId = Guid.NewGuid();
    Directory.CreateDirectory(Path.Combine(skinRoot, "skin-cache"));
    await File.WriteAllBytesAsync(
        Path.Combine(skinRoot, "skin-cache", skinId.ToString("N") + ".png"),
        new byte[] { 137, 80, 78, 71 }
    );
    using (var skinCore = new LauncherCore(new Store(skinRoot)))
    {
        var skins = new MinecraftSkins(skinCore, skinRoot);
        var skin = await skins.Get(skinId.ToString("D"), cacheOnly: true);
        Check(
            skin is { Length: 4 }
                && ReferenceEquals(
                    skins.Get(skinId.ToString("N"), true),
                    skins.Get(skinId.ToString("D"), true)
                ),
            "shared account skins use disk cache and coalesce normalized UUIDs"
        );
        Check(
            await skins.Get("../../not-a-profile", cacheOnly: true) == null,
            "account skin cache rejects invalid UUID paths"
        );
    }
    var catalog = new ThemeCatalog(Path.Combine(root, "themes"));
    Check(catalog.IsBuiltIn("craft"), "embedded theme belongs to core");
    var languages = new LocalizationCatalog(Path.Combine(root, "locales"));
    Check(
        languages.Languages.Any(l => l.Code == "en_us")
            && languages.Languages.Any(l => l.Code == "zh_cn"),
        "embedded locale packs belong to core"
    );
    var rules = JsonNode.Parse(
        "[{\"action\":\"allow\",\"os\":{\"name\":\"" + MinecraftPlatform.OsName + "\"}}]"
    );
    Check(LauncherCore.Allowed(rules), "Mojang rules use the current OS");
    Check(
        !LauncherCore.Allowed(
            JsonNode.Parse("[{\"action\":\"allow\",\"os\":{\"name\":\"unmatched-os\"}}]")
        ),
        "foreign OS libraries are excluded"
    );
    var javaRoot = Path.Combine(root, "jdk");
    Directory.CreateDirectory(Path.Combine(javaRoot, "bin"));
    Check(
        JavaManager.Normalize(javaRoot)
            == Path.Combine(javaRoot, "bin", JavaManager.JavaExecutable),
        "Java executable follows the host OS"
    );
    if (!OperatingSystem.IsWindows())
    {
        try
        {
            MinecraftPlatform.RequireWindowsX64("fixture");
            throw new Exception("Wrong platform accepted");
        }
        catch (PlatformNotSupportedException)
        {
            Console.WriteLine("PASS unsupported binary distribution is rejected");
        }
    }
    await InstallerDependencyTests.Run(Path.Combine(root, "installer-dependencies"));
    await RuntimeArchiveTests.Run(root);
    await HomeCardLayoutTests.Run(root);
    await TerracottaTests.Run(root);
    await DesktopUpdateTests.Run(root);
    await ModpackExportTests.Run(root);
    Console.WriteLine(
        "PASS modpack export metadata, selected content, traversal and cancellation protection"
    );
    await BlueprintTests.Run(root);
    Console.WriteLine("PASS blueprint HTTP/metadata/download regression suite");
    await CmsTests.Run(root);
    Console.WriteLine("PASS CMS cache/rate-limit/parser/download regression suite");
    Console.WriteLine("All portable core checks passed on " + RuntimeInformation.OSDescription);
    return 0;
}
finally
{
    Directory.Delete(root, true);
}

sealed class MemoryPlatform : LauncherPlatform
{
    public override (long TotalMb, long AvailableMb)? PhysicalMemory() => (8192, 4096);
}
