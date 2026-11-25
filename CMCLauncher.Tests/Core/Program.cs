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
    GameRelease[] commandVersions =
    [
        new("1.21.5", "release"),
        new("25w14craftmine", "snapshot"),
        new("25w13a", "snapshot"),
        new("1.21.1", "release"),
        new("1.21", "release"),
    ];
    Check(
        CommandGenerator.VersionChoices(commandVersions, ["1.12.2"], false, false)
            .SequenceEqual(["1.21.5", "1.21.1", "1.21", "1.12.2"])
        && CommandGenerator.VersionChoices(commandVersions, [], true, false).Contains("25w13a")
        && !CommandGenerator.VersionChoices(commandVersions, [], true, false).Contains("25w14craftmine")
        && CommandGenerator.VersionChoices(commandVersions, [], false, true).Contains("25w14craftmine")
        && !CommandGenerator.VersionChoices(commandVersions, [], false, true).Contains("25w13a"),
        "command versions preserve release order, include older releases and independently filter snapshots and April Fools versions"
    );
    Check(
        CommandValueCatalog.OptionsForArgument("minecraft:gamemode")
            .SequenceEqual(["survival", "creative", "adventure", "spectator"])
        && CommandValueCatalog.OptionsForArgument("brigadier:bool").SequenceEqual(["true", "false"])
        && CommandValueCatalog.TargetSelectors("minecraft:entity", "1.21.1", true)
            .SequenceEqual(["@p", "@r", "@s", "@n"])
        && !CommandValueCatalog.TargetSelectors("minecraft:entity", "1.21.1", playersOnly: true)
            .Contains("@e"),
        "command controls provide fixed choices and respect target selector restrictions"
    );
    var commandLabels = new Dictionary<string, string>
    {
        ["block.minecraft.stone"] = "石头",
        ["gamerule.drowningDamage"] = "溺水伤害",
        ["gamerule.drowningDamage.description"] = "控制溺水时是否受到伤害",
    };
    var localizedRule = CommandValueCatalog.LocalizedChoice(
        "minecraft:drowning_damage", "gamerule", commandLabels, key => key
    );
    Check(
        CommandValueCatalog.LocalizedChoice("minecraft:stone", "item", commandLabels, key => key).Name
            == "石头"
        && localizedRule.Id == "minecraft:drowning_damage"
        && localizedRule.Name == "溺水伤害"
        && localizedRule.Description == "控制溺水时是否受到伤害",
        "command choice labels use localized Minecraft text without changing command IDs"
    );
    ItemTag EnchantmentTag(string key) => new()
    {
        Key = key,
        Enchantments = [new() { Id = "minecraft:sharpness", Level = 5 }],
    };
    Check(
        CommandGenerator.BuildItemTags("1.8.9", "", [EnchantmentTag("Enchantments")])
            == """{"ench":[{"id":16s,"lvl":5s}]}"""
        && CommandGenerator.BuildItemTags("1.20.4", "", [EnchantmentTag("Enchantments")])
            == """{"Enchantments":[{"id":"minecraft:sharpness","lvl":5s}]}"""
        && CommandGenerator.BuildItemTags("1.21.1", "", [EnchantmentTag("minecraft:enchantments")])
            == """[minecraft:enchantments={"levels":{"minecraft:sharpness":5}}]"""
        && CommandGenerator.BuildItemTags("1.21.5", "", [EnchantmentTag("minecraft:stored_enchantments")])
            == """[minecraft:stored_enchantments={"minecraft:sharpness":5}]""",
        "enchantment rows serialize for numeric NBT, namespaced NBT and both component formats"
    );
    var typedTag = new ItemTag { Key = "custom_data", Type = ItemTagType.Compound };
    var byteArray = new ItemTag { Key = "bytes", Type = ItemTagType.ByteArray };
    byteArray.Children.Add(new ItemTag { Type = ItemTagType.Byte, Value = "-2" });
    typedTag.Children.Add(byteArray);
    Check(
        CommandGenerator.BuildItemTags("1.21.5", "", [typedTag])
            == """[minecraft:custom_data={"bytes":[B;-2b]}]"""
        && CommandGenerator.BuildItemTags("1.12.2", "", [typedTag])
            == """{"custom_data":{"bytes":[B;-2b]}}""",
        "graphical item tags preserve compound and typed-array syntax on both sides of the component transition"
    );
    Check(
        CommandGenerator.Generate("1.21.5", "ban", ["Steve", "Repeated griefing"])
            == "/ban Steve Repeated griefing"
        && CommandGenerator.Generate("1.21.5", "whitelist", ["add", "Steve"])
            == "/whitelist add Steve"
        && CommandGenerator.Generate("1.21.5", "save-all", ["flush"]) == "/save-all flush"
        && CommandGenerator.Generate("1.12.2", "save-all", []) == "/save-all"
        && CommandGenerator.Generate("1.21.5", "stop", []) == "/stop",
        "dedicated server forms generate administration commands and version-specific save syntax"
    );
    var reportPath = Path.Combine(root, "commands.json");
    File.WriteAllText(reportPath, """
        {"type":"root","children":{
          "rule":{"type":"literal","children":{"value":{"type":"argument","parser":"brigadier:bool","executable":true}}},
          "alias":{"type":"literal","redirect":["rule"]},
          "run":{"type":"literal","redirect":[]},
          "position":{"type":"literal","children":{"point":{"type":"argument","parser":"minecraft:vec3","executable":true}}},
          "column":{"type":"literal","children":{"point":{"type":"argument","parser":"minecraft:column_pos","executable":true}}}
        }}
        """);
    var commandReport = CommandTree.Load(reportPath);
    Check(
        commandReport.Preview(["position", "point"], ["", "^ ^1 ^-2"]) == "/position ^ ^1 ^-2"
        && commandReport.Preview(["position", "point"], ["", "~1 64"]).Length == 0
        && commandReport.Preview(["column", "point"], ["", "~1 -2"]) == "/column ~1 -2"
        && commandReport.Preview(["column", "point"], ["", "~1 64 -2"]).Length == 0,
        "command trees validate three-axis coordinates and horizontal columns"
    );
    Check(
        commandReport.Choices(["alias"]).Single().Parser == "brigadier:bool"
            && commandReport.Preview(["alias", "value"], ["", "true"]) == "/alias true"
            && commandReport.Preview(["run", "rule", "value"], ["", "", "false"]) == "/run rule false"
            && commandReport.Preview(["rule", "value"], ["", "no"]).Length == 0
            && commandReport.Preview(["rule"], [""]).Length == 0,
        "imported command trees support literals, typed arguments, redirects and executable branches"
    );
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
    child.Environment["APPIMAGE"] = "/tmp/CMCLauncher.AppImage";
    child.Environment["APPDIR"] = "/tmp/appimage";
    child.Environment["ARGV0"] = "CMCLauncher.AppImage";
    child.Environment["OWD"] = "/home/player";
    child.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
    CMCLauncher.Platform.PlatformCommand.SystemEnvironment(child);
    Check(
        child.Environment["LD_LIBRARY_PATH"] == "/system/custom/lib"
            && !child.Environment.ContainsKey("CMCLAUNCHER_ORIGINAL_LD_LIBRARY_PATH")
            && new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD", "APPIMAGE_EXTRACT_AND_RUN" }
                .All(key => !child.Environment.ContainsKey(key)),
        "AppImage libraries and runtime variables do not leak into child processes"
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
    // 图标负缓存回归：瞬时读取失败后，磁盘上的图标恢复时应当再次读取，而不是被常驻空缓存挡住。
    var iconJar = Path.Combine(root, "icon-mod.jar");
    using (
        var zip = System.IO.Compression.ZipFile.Open(
            iconJar,
            System.IO.Compression.ZipArchiveMode.Create
        )
    )
    {
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            writer.Write(
                "{\"schemaVersion\":1,\"id\":\"icon_fixture\",\"name\":\"Icon fixture\",\"version\":\"1.0\",\"icon\":\"icon.png\"}"
            );
        var icon = Convert.FromHexString("89504E470D0A1A0A0000000D494844520000004000000040080600000000000000");
        using (var output = zip.CreateEntry("icon.png").Open())
            output.Write(icon, 0, icon.Length);
    }
    ContentInspectionCache.ClearMemory();
    var firstIcon = await ContentInspectionCache.GetAsync(iconJar, "mod", true, root);
    var iconDirectory = Path.Combine(root, "content-metadata");
    var iconFile = Directory.EnumerateFiles(iconDirectory, "*.icon").Single();
    Check(
        firstIcon.Icon is { Length: > 0 },
        "mod icon bytes are extracted and available after inspection"
    );
    ContentInspectionCache.ClearMemory();
    File.Delete(iconFile);
    var emptyIcon = await ContentInspectionCache.GetAsync(iconJar, "mod", true, root);
    Check(
        emptyIcon.Icon == null,
        "missing icon file surfaces a null icon without poisoning the resident cache"
    );
    await File.WriteAllBytesAsync(iconFile, firstIcon.Icon!);
    var recoveredIcon = await ContentInspectionCache.GetAsync(iconJar, "mod", true, root);
    Check(
        recoveredIcon.Icon?.Length > 0,
        "icon reads retry after transient failure instead of permanently caching the empty result"
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
    var skinHeader = Convert.FromHexString("89504E470D0A1A0A0000000D494844520000004000000040080600000000000000");
    var invalidSkin = (byte[])skinHeader.Clone();
    invalidSkin[19] = 128;
    Check(
        SkinImport.IsStandardSkin(skinHeader) && !SkinImport.IsStandardSkin(invalidSkin),
        "skin imports accept standard PNG dimensions and reject unsupported widths"
    );
    var previewLength = SkinImport.PreviewSize * SkinImport.PreviewSize * 4;
    Check(
        SkinImport.RenderPreview(new byte[64 * 64 * 4], 64, slim: true).Length == previewLength
        && SkinImport.RenderPreview(new byte[64 * 32 * 4], 32, slim: false).Length == previewLength,
        "skin dialog preview renders standard and legacy textures at a supported resolution"
    );
    var languageJar = Path.Combine(root, "mod-lang.jar");
    using (var archive = System.IO.Compression.ZipFile.Open(languageJar, System.IO.Compression.ZipArchiveMode.Create))
    {
        void Put(string path, string value)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(value);
        }
        Put("assets/first/lang/en_us.json", """{"item.first.test":"Hello %1$s"}""");
        Put("assets/first/lang/zh_cn.json", """{"item.first.test":"你好 %1$s"}""");
        Put("assets/second/lang/en_US.lang", "block.second.test=Second");
        Put("assets/second/lang/zh_CN.lang", "block.second.test=第二项");
        Put("assets/../lang/en_us.json", """{"unsafe":"skip"}""");
    }
    var languageEntries = ModTranslation.ReadLanguages(languageJar, "en_us");
    using (var translationHttp = new HttpClient())
        await ModTranslation.TranslateMod(translationHttp, root, root, "1.21.11",
            languageJar, languageEntries, "en_us", "zh_cn", CancellationToken.None);
    var translatedPack = Path.Combine(root, "resourcepacks", "cmc-mod-translation");
    Check(
        languageEntries.Count == 2
        && JsonNode.Parse(File.ReadAllText(Path.Combine(translatedPack, "assets", "first", "lang", "zh_cn.json")))
            ?["item.first.test"]?.GetValue<string>() == "你好 %1$s"
        && JsonNode.Parse(File.ReadAllText(Path.Combine(translatedPack, "assets", "second", "lang", "zh_cn.json")))
            ?["block.second.test"]?.GetValue<string>() == "第二项"
        && JsonNode.Parse(File.ReadAllText(Path.Combine(translatedPack, "pack.mcmeta")))
            ?["pack"]?["min_format"]?[0]?.GetValue<int>() == 75,
        "mod translation preserves asset namespaces, bundled translations and modern resource pack metadata"
    );
    ModTranslation.WriteResourcePack(root, root, "1.8.9", languageEntries, "zh_cn");
    Check(
        File.ReadAllText(Path.Combine(translatedPack, "assets", "second", "lang", "zh_CN.lang"))
            .Contains("block.second.test=Second", StringComparison.Ordinal),
        "legacy mod translations use the legacy language file format and casing"
    );
    var whiteSkin = new byte[64 * 64 * 4];
    for (var i = 0; i < whiteSkin.Length; i += 4)
        whiteSkin[i] = whiteSkin[i + 1] = whiteSkin[i + 2] = 255;
    var skinModel = SkinImport.RenderPreview(whiteSkin, 64, slim: false);
    Check(
        skinModel[3] == 0
        && Enumerable.Range(0, skinModel.Length / 4)
            .Where(pixel => skinModel[pixel * 4 + 3] == 255)
            .Select(pixel => skinModel[pixel * 4]).Distinct().Count() == 3
        && Enumerable.Range(0, SkinImport.PreviewSize).Any(x =>
            skinModel[((SkinImport.PreviewSize * 4 / 5) * SkinImport.PreviewSize + x) * 4 + 3] == 255),
        "3D skin preview draws lit front, side and top surfaces with a full-body silhouette"
    );
    var backSkin = new byte[64 * 64 * 4];
    for (var y = 8; y < 16; y++)
    for (var x = 24; x < 32; x++)
        backSkin[(y * 64 + x) * 4 + 2] = 255;
    var fromFront = SkinImport.RenderPreview(backSkin, 64, slim: false, yaw: 0);
    var fromBack = SkinImport.RenderPreview(backSkin, 64, slim: false, yaw: MathF.PI);
    Check(
        Enumerable.Range(0, fromFront.Length / 4).All(pixel => fromFront[pixel * 4 + 2] == 0)
        && Enumerable.Range(0, fromBack.Length / 4).Any(pixel => fromBack[pixel * 4 + 2] > 0)
        && Enum.GetValues<SkinPose>().Where(pose => pose != SkinPose.Standing).All(pose =>
            !SkinImport.RenderPreview(whiteSkin, 64, slim: false, pose: pose).SequenceEqual(skinModel)),
        "rotating a skin reveals back UVs and each action changes the rendered pose"
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
        await skins.RememberAppliedSkin(skinId.ToString("D"), skinHeader);
        Check(
            (await skins.Get(skinId.ToString("N"), cacheOnly: true))?.SequenceEqual(skinHeader) == true
                && (await File.ReadAllBytesAsync(
                    Path.Combine(skinRoot, "skin-cache", skinId.ToString("N") + ".png")
                )).SequenceEqual(skinHeader),
            "applying a skin replaces both the session and disk avatar cache"
        );
    }
    var catalog = new ThemeCatalog(Path.Combine(root, "themes"));
    Check(
        catalog.Presets.Count == 8 && catalog.Presets.All(theme => theme.IsBuiltIn),
        "all built-in themes remain available without a packaged theme directory"
    );
    var packagedThemes = Path.Combine(root, "packaged-themes");
    Directory.CreateDirectory(BundledResources.DefaultDirectory(packagedThemes));
    File.WriteAllText(
        Path.Combine(BundledResources.DefaultDirectory(packagedThemes), "packaged.json"),
        """{"id":"packaged","displayName":"Packaged","colors":{"LightBg":"#123456"}}"""
    );
    Check(
        new ThemeCatalog(Path.Combine(root, "user-themes"), packagedDirectory: packagedThemes)
            .IsBuiltIn("packaged"),
        "themes in the runtime Default directory are discovered with a separate user directory"
    );
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
    var legacyLwjglProfile = JsonNode.Parse("""
        {
          "libraries": [
            {"name":"org.lwjgl:lwjgl:3.2.1","rules":[{"action":"allow","os":{"name":"osx"}}],
              "downloads":{"artifact":{"path":"lwjgl-3.2.1.jar"}}},
            {"name":"org.lwjgl:lwjgl:3.2.2","rules":[{"action":"allow"},{"action":"disallow","os":{"name":"osx"}}],
              "downloads":{"artifact":{"path":"lwjgl-3.2.2.jar"}}},
            {"name":"org.lwjgl:lwjgl:3.2.1","rules":[{"action":"allow","os":{"name":"osx"}}],
              "natives":{"osx":"natives-macos"},"downloads":{"classifiers":{"natives-macos":{"path":"lwjgl-macos.jar"}}}},
            {"name":"org.lwjgl:lwjgl:3.2.2","rules":[{"action":"allow"},{"action":"disallow","os":{"name":"osx"}}],
              "natives":{"linux":"natives-linux"},"downloads":{"classifiers":{"natives-linux":{"path":"lwjgl-linux.jar"}}}}
          ]
        }
        """)!;
    var previousLibraryPlatform = LauncherPlatform.Current;
    try
    {
        LauncherPlatform.Current = new LibraryPlatform("linux");
        foreach (var version in new[] { legacyLwjglProfile, LauncherCore.Merge(legacyLwjglProfile, new JsonObject()) })
        {
            var selectedLibraries = LauncherCore.EffectiveLibraries(version);
            Check(
                selectedLibraries.Count == 2
                    && selectedLibraries.All(lib => lib["name"]?.ToString() == "org.lwjgl:lwjgl:3.2.2")
                    && selectedLibraries.Any(lib => lib["downloads"]?["artifact"]?["path"]?.ToString() == "lwjgl-3.2.2.jar")
                    && selectedLibraries.Any(lib => lib["downloads"]?["classifiers"]?["natives-linux"]?["path"]?.ToString() == "lwjgl-linux.jar"),
                "Minecraft 1.18 retains the Linux LWJGL classpath and native archive after platform filtering"
            );
        }
    }
    finally
    {
        LauncherPlatform.Current = previousLibraryPlatform;
    }
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
sealed class LibraryPlatform(string osName) : LauncherPlatform
{
    public override string MinecraftOsName => osName;
}
