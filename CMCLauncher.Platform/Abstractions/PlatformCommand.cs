using System.Diagnostics;

namespace CMCLauncher.Platform;

// Passwords belong in stdin, never in command-line arguments or diagnostics.
public static class PlatformCommand
{
    // AppImage's private libraries belong to the launcher, not Java or desktop utilities.
    public static ProcessStartInfo SystemEnvironment(ProcessStartInfo info)
    {
        if (info.Environment.TryGetValue("CMCLAUNCHER_ORIGINAL_LD_LIBRARY_PATH", out var original))
        {
            if (string.IsNullOrEmpty(original))
                info.Environment.Remove("LD_LIBRARY_PATH");
            else
                info.Environment["LD_LIBRARY_PATH"] = original;
            info.Environment.Remove("CMCLAUNCHER_ORIGINAL_LD_LIBRARY_PATH");
        }
        return info;
    }

    public static string Execute(
        string executable,
        IEnumerable<string> arguments,
        string? input = null
    )
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        info.Environment["LC_ALL"] = "C";
        using var process =
            Process.Start(SystemEnvironment(info))
            ?? throw new IOException("Could not start platform service.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input != null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            throw new TimeoutException("Platform service timed out.");
        }
        if (process.ExitCode != 0)
            throw new IOException(
                "Platform service failed. Check that the system credential store is installed and unlocked."
            );
        _ = error.GetAwaiter().GetResult();
        return output.GetAwaiter().GetResult().TrimEnd('\r', '\n');
    }
}
