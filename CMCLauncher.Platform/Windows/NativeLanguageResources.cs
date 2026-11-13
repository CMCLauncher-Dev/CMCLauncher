using System.Security.Cryptography;
using System.Text;

namespace CMCLauncher;

public static class NativeLanguageResources
{
    // Keep staged files between runs: deleting them on exit races with other launcher processes.
    // Publish each file atomically so an interrupted or concurrent startup can safely repair it.
    public static bool Stage(string source, string destination)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination))
                        .ToUpperInvariant()
                )
            )
        );
        using var gate = new Mutex(false, @"Local\CMCLauncher.NativeLanguages." + key);
        var acquired = false;
        try
        {
            try
            {
                acquired = gate.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
                throw new IOException(
                    "Native language resources are busy in another launcher process."
                );
            return StageFiles(source, destination);
        }
        finally
        {
            if (acquired)
                gate.ReleaseMutex();
        }
    }

    static bool StageFiles(string source, string destination)
    {
        var files = Directory.GetFiles(source, "*.mui", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return false;
        Directory.CreateDirectory(destination);
        foreach (var file in files)
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (
                File.Exists(target)
                && File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(target))
            )
                continue;
            var temporary = Path.Combine(
                destination,
                ".cmcl-mui-" + Guid.NewGuid().ToString("N") + ".tmp"
            );
            try
            {
                File.Copy(file, temporary);
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        return true;
    }
}
