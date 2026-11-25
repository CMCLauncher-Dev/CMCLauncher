using CMCLauncher;

var root = Path.Combine(Path.GetTempPath(), "cmcl-linux-graphics-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    void Device(string card, string vendor, string id, string? busy)
    {
        var path = Path.Combine(root, card, "device");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "vendor"), vendor);
        File.WriteAllText(Path.Combine(path, "device"), id);
        if (busy != null)
            File.WriteAllText(Path.Combine(path, "gpu_busy_percent"), busy);
    }
    void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    Check(
        LinuxLauncherPlatform.SecretToolFailure("No such secret collection at path: /", true)
            == "The Linux Secret Service could not find the default keyring."
        && LinuxLauncherPlatform.SecretToolFailure("org.freedesktop.DBus.Error.ServiceUnknown", true)
            == "The Linux Secret Service is unavailable in this desktop session."
        && LinuxLauncherPlatform.SecretToolFailure("The default collection is locked.", true)
            == "The Linux keyring could not be unlocked."
        && LinuxLauncherPlatform.SecretToolFailure("", false)
            == "The saved Linux credential is missing. Sign in again."
        && LinuxLauncherPlatform.SecretToolFailure("unknown error with private input", true)
            == "Could not save the login to the Linux keyring.",
        "Secret Service diagnostics distinguish common failures without echoing stderr"
    );
    byte[] fakeCredential = [1, 2, 3, 4];
    var encodedCredential = Convert.ToBase64String(fakeCredential);
    string? walletEntry = null;
    string FailedSecretTool(string[] args, string? input) =>
        throw new IOException("Could not save the login to the Linux keyring.");
    string Wallet(string[] args, string? input)
    {
        Check(
            args.Length == 5 && args[2] == "--folder" && args[3] == "CMCLauncher"
                && args[4] == "kdewallet" && Guid.TryParseExact(args[1], "N", out _),
            "KDE Wallet receives an opaque entry ID and no credentials in its arguments"
        );
        if (args[0] == "--write-password")
        {
            if (input != encodedCredential)
                throw new Exception("Credential was not sent through stdin.");
            walletEntry = input;
            return "";
        }
        return walletEntry ?? "";
    }
    var walletReference = LinuxLauncherPlatform.TransformCredential(
        fakeCredential, true, true, FailedSecretTool, Wallet
    );
    Check(
        System.Text.Encoding.UTF8.GetString(walletReference).StartsWith("kwallet:", StringComparison.Ordinal)
            && LinuxLauncherPlatform.TransformCredential(walletReference, false, false, FailedSecretTool, Wallet)
                .SequenceEqual(fakeCredential),
        "KDE Wallet fallback writes, verifies and reads the saved credential"
    );
    var existingReference = System.Text.Encoding.UTF8.GetBytes("secret-service:" + Guid.NewGuid().ToString("N"));
    Check(
        LinuxLauncherPlatform.TransformCredential(existingReference, false, true, (_, _) => encodedCredential,
            (_, _) => throw new Exception("Secret Service references must still use Secret Service.")
        ).SequenceEqual(fakeCredential),
        "existing Secret Service entries remain readable on KDE"
    );
    try
    {
        _ = LinuxLauncherPlatform.TransformCredential(fakeCredential, true, true, FailedSecretTool, (_, _) => "");
        throw new Exception("A wallet write without a readable value was accepted.");
    }
    catch (IOException ex) when (ex.Message == "Could not verify the login saved in KDE Wallet.") { }
    var nonexistentProcess = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
    Check(
        CMCLauncher.Platform.GameExitWatchdog.Handle(
            ["--watch-game-exit", nonexistentProcess, "0", nonexistentProcess, "0",
                Path.Combine(root, "must-not-restart"), "", root, Path.Combine(root, "watchdog.ack")]
        ),
        "a watchdog with stale process identities exits without restarting the launcher"
    );
    Device("card0", "0x1002", "0x73df", "42");
    Device("card1", "0x8086", "0x46a6", null);
    Device("card2", "0x10de", "0x2684", "101");
    Device("card0-HDMI-A-1", "0x1002", "0x73df", "55");
    Device("card3", "invalid", "0x1234", "1");
    var devices = LinuxGraphics.ReadDrm(root, true, default);
    Check(devices.Count == 3, "DRM adapters exclude connectors and invalid PCI identifiers");
    Check(
        devices.Single(d => d.Name.StartsWith("AMD")).Usage == 42,
        "AMD driver utilization is reported"
    );
    Check(
        devices.Single(d => d.Name.StartsWith("Intel")).Usage == -1,
        "Missing Intel counters remain unavailable"
    );
    Check(
        devices.Single(d => d.Name.StartsWith("NVIDIA")).Usage == -1,
        "Invalid driver samples do not become fabricated percentages"
    );
    Check(
        LinuxGraphics.ReadDrm(root, false, default).Count == 2,
        "NVIDIA driver output avoids duplicate DRM adapters"
    );
    Check(
        LinuxGraphics.ReadDrm(Path.Combine(root, "missing"), true, default).Count == 0,
        "Hosts without DRM remain supported"
    );
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        LinuxGraphics.ReadDrm(root, true, cancellation.Token);
        throw new Exception("Cancelled DRM scan continued");
    }
    catch (OperationCanceledException) { }
    Console.WriteLine("PASS DRM scan cancellation");
}
finally
{
    Directory.Delete(root, true);
}
