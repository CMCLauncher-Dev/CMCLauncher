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
