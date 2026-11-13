using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

var root = Path.Combine(Path.GetTempPath(), "cmcl-mac-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var previous = LauncherPlatform.Current;
try
{
    void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    LauncherPlatform.Current = new TestPlatform("osx");
    var profile = JsonNode.Parse(
        """
        {"id":"fixture","mainClass":"net.minecraft.client.main.Main",
        "libraries":[{"name":"org.lwjgl:lwjgl-glfw:3.3.3"}], "minecraftArguments":"--username ${auth_player_name}"}
        """
    )!;
    var settings = new Settings { AutoMemory = false };
    var cp = new List<string>
    {
        Path.Combine(root, "Library.jar"),
        Path.Combine(root, "library.jar"),
    };
    List<string> Args() =>
        LauncherCore.BuildArguments(
            profile,
            root,
            root,
            root,
            cp,
            new Account
            {
                Type = "offline",
                Name = "Fixture",
                Uuid = Guid.Empty.ToString("N"),
            },
            new JsonObject(),
            settings
        );
    var builtArgs = Args();
    Check(
        builtArgs.Count(a => a == "-XstartOnFirstThread") == 1
            && builtArgs.IndexOf("-XstartOnFirstThread")
                < builtArgs.IndexOf("net.minecraft.client.main.Main"),
        "macOS GLFW profiles receive the first-thread JVM option before the main class"
    );
    Check(
        builtArgs[builtArgs.IndexOf("-cp") + 1].Split(Path.PathSeparator).Length == 2,
        "case-sensitive classpath entries are retained"
    );
    settings.CustomJvmArgs = "-XstartOnFirstThread";
    Check(
        Args().Count(a => a == "-XstartOnFirstThread") == 1,
        "custom first-thread option is not duplicated"
    );
    settings.CustomJvmArgs = "";
    profile["arguments"] = JsonNode.Parse("""{"jvm":["-XstartOnFirstThread"]}""");
    Check(
        Args().Count(a => a == "-XstartOnFirstThread") == 1,
        "Mojang first-thread option is not duplicated"
    );
    profile.AsObject().Remove("arguments");
    profile["libraries"] = JsonNode.Parse("""[{"name":"org.lwjgl.lwjgl:lwjgl:2.9.4"}]""");
    Check(
        !Args().Contains("-XstartOnFirstThread"),
        "legacy LWJGL 2 profiles retain their original thread behavior"
    );
    profile["libraries"] = JsonNode.Parse("""[{"name":"org.lwjgl:lwjgl-glfw:3.3.3"}]""");
    LauncherPlatform.Current = new TestPlatform("windows");
    Check(
        !Args().Contains("-XstartOnFirstThread"),
        "Windows profiles do not receive a macOS JVM option"
    );
    LauncherPlatform.Current = new TestPlatform("osx");
    Check(
        JavaManager.IsMacSystemStub("/usr/bin/java")
            && JavaManager.IsMacSystemStub("/bin/java")
            && !JavaManager.IsMacSystemStub(
                "/Library/Java/JavaVirtualMachines/jdk.jdk/Contents/Home/bin/java"
            ),
        "system Java placeholders are excluded without executing them"
    );
    var bundle = Path.Combine(root, "bundles", "jdk 中文.jdk");
    var home = Path.Combine(bundle, "Contents", "Home");
    Directory.CreateDirectory(Path.Combine(home, "bin"));
    File.WriteAllText(Path.Combine(home, "bin", "java"), "fixture");
    var brew = Path.Combine(root, "brew");
    var brewHome = Path.Combine(brew, "openjdk@21", "libexec", "openjdk.jdk", "Contents", "Home");
    Directory.CreateDirectory(Path.Combine(brewHome, "bin"));
    File.WriteAllText(Path.Combine(brewHome, "bin", "java"), "fixture");
    Directory.CreateDirectory(Path.Combine(root, "bundles", "incomplete.jdk"));
    var homes = MacOSLauncherPlatform
        .DiscoverJavaHomes([Path.Combine(root, "bundles"), Path.Combine(root, "missing")], [brew])
        .ToArray();
    Check(
        homes.Length == 2 && homes.Contains(home) && homes.Contains(brewHome),
        "standard and Homebrew JDK bundles are discovered without system symlinks"
    );
    Check(
        JavaManager.Normalize(bundle) == Path.Combine(home, "bin", "java"),
        "importing a .jdk bundle resolves Contents/Home/bin/java"
    );
    Directory.CreateDirectory(Path.Combine(home, "jre", "bin"));
    File.WriteAllText(Path.Combine(home, "jre", "bin", "java"), "embedded runtime fixture");
    Check(
        JavaManager.DownloadedJavaExecutable(bundle) == Path.Combine(home, "bin", "java"),
        "Java 8 archives select the outer JDK rather than rejecting their embedded JRE"
    );
    JavaManager.Register(settings, new JavaInstallation { Path = cp[0] });
    JavaManager.Register(settings, new JavaInstallation { Path = cp[1] });
    Check(
        settings.JavaInstallations.Count == 2,
        "Java registration preserves distinct case-sensitive paths"
    );
    JavaManager.Remove(settings, settings.JavaInstallations[0]);
    Check(
        settings.JavaInstallations.Single().Path == cp[1],
        "removing one Java path preserves a differently cased installation"
    );
    try
    {
        new MacOSLauncherPlatform().TransformCredential(
            Encoding.UTF8.GetBytes("keychain:../invalid"),
            false
        );
        throw new Exception("Malformed Keychain reference was accepted");
    }
    catch (IOException)
    {
        Console.WriteLine(
            "PASS malformed Keychain references are rejected before invoking security"
        );
    }
    if (OperatingSystem.IsMacOS())
    {
        var mac = new MacOSLauncherPlatform();
        LauncherPlatform.Current = mac;
        var memory = mac.PhysicalMemory();
        Check(
            memory is { TotalMb: > 0 }
                && memory.Value.AvailableMb >= 0
                && memory.Value.AvailableMb <= memory.Value.TotalMb,
            "native macOS memory probe returns bounded values"
        );
        var cpu = await mac.CpuUsage(default);
        Check(cpu is >= 0 and <= 100, "native macOS CPU probe returns a bounded value");
        var installed = mac.JavaHomes().FirstOrDefault();
        Check(
            installed != null && (await JavaManager.Inspect(installed!, default)).Major > 0,
            "native macOS JDK discovery and inspection succeed"
        );
        try
        {
            await JavaManager.Inspect("/usr/bin/java", default);
            throw new Exception("System Java placeholder was executed");
        }
        catch (IOException)
        {
            Console.WriteLine("PASS native system Java placeholder is rejected");
        }
    }
    Console.WriteLine("All macOS compatibility checks passed.");
    return 0;
}
finally
{
    LauncherPlatform.Current = previous;
    Directory.Delete(root, true);
}

sealed class TestPlatform(string os) : LauncherPlatform
{
    public override string MinecraftOsName => os;
}
