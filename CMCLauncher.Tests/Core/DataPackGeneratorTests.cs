using System.IO.Compression;
using System.Text.Json;
using CMCLauncher.Core.Minecraft.DataPacks;

static class DataPackGeneratorTests
{
    public static void Run(string root)
    {
        DataPackAdvancedTests.Run();
        Directory.CreateDirectory(root);
        var description = "描述\n\"quoted\"";
        var directory = DataPackGenerator.CreateDataPack(
            root,
            "Example",
            "1.21.1",
            description,
            "example"
        );
        var path = Path.Combine(directory, "pack.mcmeta");
        var original = File.ReadAllBytes(path);
        using var json = JsonDocument.Parse(original);
        var pack = json.RootElement.GetProperty("pack");
        if (
            pack.GetProperty("pack_format").GetInt32() != 48
            || pack.GetProperty("description").GetString() != description
            || !Directory.Exists(Path.Combine(directory, "data", "example"))
        )
            throw new Exception("Data pack metadata or namespace directory is incorrect.");
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
            catch (IOException)
            {
                return;
            }
            throw new Exception("Invalid data pack request was accepted.");
        }
        Reject(() => DataPackGenerator.CreateDataPack(root, "Example", "1.21.1"));
        if (!File.ReadAllBytes(path).SequenceEqual(original))
            throw new Exception("Existing data pack was changed.");
        foreach (var name in new[] { "../escape", "nested/name", "CON", "test.", "bad:name" })
            Reject(() => DataPackGenerator.CreateDataPack(root, name, "1.21.1"));
        Reject(() => DataPackGenerator.CreateDataPack(root, "Invalid", "unknown"));
        Reject(() =>
            DataPackGenerator.CreateDataPack(root, "Invalid", "1.20.1", packNamespace: "../bad")
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellationObserved = false;
        try
        {
            DataPackGenerator.CreateDataPack(
                root,
                "Canceled",
                "1.20.1",
                cancellationToken: canceled.Token
            );
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }
        if (
            !cancellationObserved
            || Directory.Exists(Path.Combine(root, "Canceled"))
            || Directory.EnumerateDirectories(root, ".cmcl-datapack-*").Any()
        )
            throw new Exception("Canceled creation left files behind.");
        var modern = DataPackGenerator.CreateDataPack(root, "Modern", "1.21.11");
        using var modernJson = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(modern, "pack.mcmeta"))
        );
        var modernPack = modernJson.RootElement.GetProperty("pack");
        if (
            modernPack.GetProperty("min_format")[1].GetInt32() != 1
            || modernPack.GetProperty("max_format")[0].GetInt32() != 94
            || modernPack.TryGetProperty("pack_format", out _)
        )
            throw new Exception("Modern major/minor data pack metadata is incorrect.");
        Reject(() => DataPackGenerator.CreateDataPack(root, "Snapshot", "24w01a"));
        foreach (var name in new[] { "COM¹", "CON .txt", new string('测', 100) })
            Reject(() => DataPackGenerator.CreateDataPack(root, name, "1.21.1"));
        Reject(() =>
            DataPackGenerator.CreateDataPack(root, "Namespace", "1.21.1", packNamespace: "con")
        );
        var outcomes = Task.WhenAll(
                Enumerable
                    .Range(0, 2)
                    .Select(_ =>
                        Task.Run(() =>
                        {
                            try
                            {
                                DataPackGenerator.CreateDataPack(root, "Concurrent", "1.21.1");
                                return true;
                            }
                            catch (IOException)
                            {
                                return false;
                            }
                        })
                    )
            )
            .GetAwaiter()
            .GetResult();
        if (
            outcomes.Count(success => success) != 1
            || Directory.EnumerateDirectories(root, ".cmcl-datapack-*").Any()
        )
            throw new Exception(
                "Concurrent creation overwrote a pack or leaked staging directories."
            );
        foreach (var version in DataPackGenerator.SupportedGameVersions)
        {
            var options = new DataPackOptions("Generated", version, "A test", "example")
            {
                Templates = DataPackTemplates.All,
            };
            var files = DataPackGenerator.Preview(options);
            if (files.Count != 8 || !files.ContainsKey("pack.mcmeta"))
                throw new Exception("Unexpected starter file set: " + version);
            foreach (var (file, contents) in files)
                if (file.EndsWith(".json") || file == "pack.mcmeta")
                    using (JsonDocument.Parse(contents)) { }
            var format = DataPackGenerator.GetDataPackFormat(version).Major;
            var folder = format >= 48 ? "recipe" : "recipes";
            using var recipe = JsonDocument.Parse(
                files["data/example/" + folder + "/example.json"]
            );
            var recipeRoot = recipe.RootElement;
            if (
                recipeRoot.GetProperty("ingredients")[0].ValueKind
                    != (format >= 57 ? JsonValueKind.String : JsonValueKind.Object)
                || !recipeRoot
                    .GetProperty("result")
                    .TryGetProperty(format >= 41 ? "id" : "item", out _)
            )
                throw new Exception("Incorrect version-specific recipe schema: " + version);
            var functionFolder = format >= 48 ? "function" : "functions";
            using var load = JsonDocument.Parse(
                files["data/minecraft/tags/" + functionFolder + "/load.json"]
            );
            if (load.RootElement.GetProperty("values")[0].GetString() != "example:load")
                throw new Exception("Load function is not registered.");
        }
        var zipOptions = new DataPackOptions("Archive", "1.21.2", "ZIP", "example")
        {
            Templates = DataPackTemplates.All,
        };
        var zipPath = Path.Combine(root, "pack.zip");
        DataPackGenerator.ExportZip(zipPath, zipOptions);
        var zipBytes = File.ReadAllBytes(zipPath);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var previewFiles = DataPackGenerator.Preview(zipOptions);
            if (
                archive.Entries.Count != previewFiles.Count
                || archive.GetEntry("pack.mcmeta") == null
            )
                throw new Exception("ZIP contains an extra wrapping directory or missing files.");
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                if (reader.ReadToEnd() != previewFiles[entry.FullName])
                    throw new Exception("Preview and ZIP differ.");
            }
        }
        Reject(() => DataPackGenerator.ExportZip(zipPath, zipOptions));
        if (!zipBytes.SequenceEqual(File.ReadAllBytes(zipPath)))
            throw new Exception("Existing ZIP was overwritten.");
        var canceledZip = Path.Combine(root, "canceled.zip");
        try
        {
            DataPackGenerator.ExportZip(canceledZip, zipOptions, canceled.Token);
            throw new Exception("Cancellation ignored.");
        }
        catch (OperationCanceledException) { }
        if (
            File.Exists(canceledZip)
            || Directory.EnumerateFileSystemEntries(root, ".cmcl-datapack-*").Any()
        )
            throw new Exception("ZIP export leaked temporary files.");
        Reject(() => DataPackGenerator.Preview(zipOptions with { Ingredient = "../invalid" }));
        Reject(() => DataPackGenerator.Preview(zipOptions with { Ingredient = "minecraft:air" }));
        Reject(() =>
            DataPackGenerator.Preview(zipOptions with { Templates = (DataPackTemplates)32 })
        );
        var exportedDirectory = DataPackGenerator.CreateDataPack(root, zipOptions);
        foreach (var (file, contents) in DataPackGenerator.Preview(zipOptions))
            if (File.ReadAllText(Path.Combine(exportedDirectory, file)) != contents)
                throw new Exception("Preview and directory export differ.");
        var alternate = DataPackGenerator.CreateDataPack(root, "SelectedVersion", "1.20.1");
        using var selected = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(alternate, "pack.mcmeta"))
        );
        if (selected.RootElement.GetProperty("pack").GetProperty("pack_format").GetInt32() != 15)
            throw new Exception("The selected data pack format was not preserved.");
    }
}
