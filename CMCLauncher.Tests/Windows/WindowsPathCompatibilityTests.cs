using CMCLauncher;

static class WindowsPathCompatibilityTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var preferred = Path.Combine(root, "portable 中文 space");
        var fallback = Path.Combine(root, "fallback");
        Check(
            PortableDataDirectory.Resolve(preferred, fallback) == preferred,
            "Writable portable directory was not retained"
        );
        Check(!Directory.EnumerateFileSystemEntries(preferred).Any(), "Write probe leaked files");
        var blocked = Path.Combine(root, "file-instead-of-directory");
        File.WriteAllText(blocked, "preserve");
        Check(
            PortableDataDirectory.Resolve(blocked, fallback) == fallback,
            "Invalid portable directory did not fall back"
        );
        Check(
            !Directory.EnumerateFileSystemEntries(fallback).Any()
                && File.ReadAllText(blocked) == "preserve",
            "Fallback damaged existing data or leaked files"
        );
        try
        {
            PortableDataDirectory.Resolve(blocked, blocked);
            throw new Exception("An unusable fallback was accepted");
        }
        catch (IOException) { }

        var source = Path.Combine(root, "source");
        var staged = Path.Combine(root, "staged");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(source, "one.dll.mui"), "current-one");
        File.WriteAllText(Path.Combine(source, "two.dll.mui"), "current-two");
        File.WriteAllText(Path.Combine(staged, "one.dll.mui"), "stale");
        File.WriteAllText(Path.Combine(staged, "user-file.txt"), "preserve");
        Check(
            NativeLanguageResources.Stage(source, staged),
            "Incomplete native resources were not repaired"
        );
        var first = Path.Combine(staged, "one.dll.mui");
        Check(
            File.ReadAllText(first) == "current-one"
                && File.ReadAllText(Path.Combine(staged, "two.dll.mui")) == "current-two",
            "Incomplete or stale language file survived staging"
        );
        var timestamp = File.GetLastWriteTimeUtc(first);
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => NativeLanguageResources.Stage(source, staged)))
        );
        Check(
            File.GetLastWriteTimeUtc(first) == timestamp
                && File.ReadAllText(Path.Combine(staged, "user-file.txt")) == "preserve",
            "Repeated staging rewrote valid files or unrelated content"
        );
        // Simulate interrupted cold-start staging, then repair it from concurrent launches.
        File.Delete(Path.Combine(staged, "two.dll.mui"));
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => NativeLanguageResources.Stage(source, staged)))
        );
        Check(
            Directory.GetFiles(staged, "*.tmp").Length == 0
                && File.ReadAllText(Path.Combine(staged, "two.dll.mui")) == "current-two",
            "Concurrent staging left temporary or incomplete resources"
        );
    }
}
