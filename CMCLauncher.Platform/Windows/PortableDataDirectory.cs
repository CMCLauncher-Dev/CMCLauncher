namespace CMCLauncher;

// Shared by the WinUI process and the standalone bootstrapper, which has no platform assembly dependency.
public static class PortableDataDirectory
{
    public static string Resolve(string preferred, string fallback)
    {
        try
        {
            Directory.CreateDirectory(preferred);
            var probe = Path.Combine(preferred, ".cmcl-write-" + Guid.NewGuid().ToString("N"));
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
            return preferred;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
