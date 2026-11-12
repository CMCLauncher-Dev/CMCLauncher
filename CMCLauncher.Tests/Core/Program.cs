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
