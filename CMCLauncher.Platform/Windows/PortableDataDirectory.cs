namespace CMCLauncher;

// Shared by the WinUI process and the standalone bootstrapper, which has no platform assembly dependency.
public static class PortableDataDirectory
{
    public static string Resolve(string preferred, string fallback)
    {
        try
        {
            VerifyWritable(preferred);
            return preferred;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VerifyWritable(fallback);
            return fallback;
        }
    }

    static void VerifyWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, ".cmcl-write-" + Guid.NewGuid().ToString("N"));
        using (
            new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose
            )
        ) { }
    }
}
