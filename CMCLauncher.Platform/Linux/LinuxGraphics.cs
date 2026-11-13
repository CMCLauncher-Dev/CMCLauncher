using System.Globalization;
using System.Text.RegularExpressions;
using CMCLauncher.Platform;

namespace CMCLauncher;

internal static class LinuxGraphics
{
    internal static IReadOnlyList<GraphicsUsage> ReadDrm(
        string root,
        bool includeNvidia,
        CancellationToken ct
    )
    {
        var result = new List<GraphicsUsage>();
        if (!Directory.Exists(root))
            return result;
        foreach (var card in Directory.EnumerateDirectories(root, "card*").Take(128))
        {
            ct.ThrowIfCancellationRequested();
            if (!Regex.IsMatch(Path.GetFileName(card), "^card[0-9]+$"))
                continue;
            try
            {
                var device = Path.Combine(card, "device");
                string Read(string name) => File.ReadAllText(Path.Combine(device, name)).Trim();
                var vendor = Read("vendor").ToLowerInvariant();
                var id = Read("device").ToLowerInvariant();
                if (
                    !Regex.IsMatch(vendor, "^0x[0-9a-f]{4}$")
                    || !Regex.IsMatch(id, "^0x[0-9a-f]{4}$")
                )
                    continue;
                if (vendor == "0x10de" && !includeNvidia)
                    continue;
                var brand = vendor switch
                {
                    "0x1002" => "AMD",
                    "0x8086" => "Intel",
                    "0x10de" => "NVIDIA",
                    _ => vendor,
                };
                var name = brand + " GPU · " + id + " · " + Path.GetFileName(card);
                double usage = -1;
                var busy = Path.Combine(device, "gpu_busy_percent");
                if (
                    File.Exists(busy)
                    && double.TryParse(
                        File.ReadAllText(busy).Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var sample
                    )
                    && double.IsFinite(sample)
                    && sample is >= 0 and <= 100
                )
                    usage = sample;
                result.Add(new(name, usage));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Hot-unplugged adapters and inaccessible driver counters are optional.
            }
        }
        return result;
    }
}
