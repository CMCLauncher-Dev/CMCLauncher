using System.IO.Compression;
using System.Text.Json.Nodes;
using CMCLauncher;

static class DesktopUpdateTests
{
    public static async Task Run(string root)
    {
        var assets = new JsonArray();
        foreach (
            var name in new[]
            {
                "CMCLauncher-Avalonia-win-x64.zip",
                "CMCLauncher-win-x64.zip",
                "CMCLauncher-Avalonia-linux-x64.tar.gz",
            }
        )
            assets.Add(
                new JsonObject
                {
                    ["name"] = name,
                    ["browser_download_url"] =
                        "https://github.com/com-in/CMCLauncher/releases/download/CMCL-v2.0.0-release/"
                        + name,
                    ["size"] = 10,
                }
            );
        var json = new JsonArray(
            new JsonObject
            {
                ["draft"] = false,
                ["prerelease"] = false,
                ["tag_name"] = "CMCL-v2.0.0-release",
                ["assets"] = assets,
            }
        ).ToJsonString();
        if (
            LauncherUpdates.ParseReleases(json, false)[0].Asset?.Name != "CMCLauncher-win-x64.zip"
            || LauncherUpdates.ParseReleases(json, false, "win-x64")[0].Asset?.Name
                != "CMCLauncher-Avalonia-win-x64.zip"
            || LauncherUpdates.ParseReleases(json, false, "linux-x64")[0].Asset?.Name
                != "CMCLauncher-Avalonia-linux-x64.tar.gz"
        )
            throw new Exception("Frontend update assets were mixed.");
        var package = Path.Combine(root, "desktop.zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            foreach (
                var name in new[]
                {
                    DesktopUpdateInstaller.ExecutableName,
                    "CMCLauncher.Avalonia.dll",
                    "themes/custom.json",
                    "settings.json",
                }
            )
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write("fixture"u8);
            }
        }
        var stage = Path.Combine(root, "desktop-stage");
        await DesktopUpdateInstaller.Stage(package, stage, default);
        var target = Path.Combine(root, "desktop-target");
        Directory.CreateDirectory(Path.Combine(target, "themes"));
        await File.WriteAllTextAsync(Path.Combine(target, "settings.json"), "user settings");
        await File.WriteAllTextAsync(Path.Combine(target, "themes", "custom.json"), "user theme");
        DesktopUpdateInstaller.Apply(stage, target);
        if (
            await File.ReadAllTextAsync(Path.Combine(target, "settings.json")) != "user settings"
            || await File.ReadAllTextAsync(Path.Combine(target, "themes", "custom.json"))
                != "user theme"
            || await File.ReadAllTextAsync(Path.Combine(target, "CMCLauncher.Avalonia.dll"))
                != "fixture"
        )
            throw new Exception("Desktop update modified user data.");
        var brokenStage = Path.Combine(root, "desktop-rollback-stage");
        Directory.CreateDirectory(brokenStage);
        await File.WriteAllTextAsync(
            Path.Combine(brokenStage, "CMCLauncher.Avalonia.dll"),
            "new runtime"
        );
        await File.WriteAllTextAsync(Path.Combine(brokenStage, "conflict.bin"), "conflict");
        Directory.CreateDirectory(Path.Combine(target, "conflict.bin"));
        try
        {
            DesktopUpdateInstaller.Apply(brokenStage, target);
            throw new Exception("Update conflict accepted.");
        }
        catch (IOException) { }
        if (
            await File.ReadAllTextAsync(Path.Combine(target, "CMCLauncher.Avalonia.dll"))
            != "fixture"
        )
            throw new Exception("Desktop update rollback did not restore runtime files.");
        if (
            !DesktopUpdateInstaller.Preserve("themes/custom.json")
            || !DesktopUpdateInstaller.Preserve("settings.json")
            || DesktopUpdateInstaller.Preserve("CMCLauncher.Avalonia.dll")
        )
            throw new Exception("Desktop update preservation");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
            zip.GetEntry("CMCLauncher.Avalonia.dll")!.Delete();
        try
        {
            await DesktopUpdateInstaller.Stage(
                package,
                Path.Combine(root, "desktop-incomplete"),
                default
            );
        }
        catch (IOException)
        {
            Console.WriteLine("PASS frontend update isolation, staging and data preservation");
            return;
        }
        throw new Exception("Incomplete desktop update accepted.");
    }
}
