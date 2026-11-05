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
