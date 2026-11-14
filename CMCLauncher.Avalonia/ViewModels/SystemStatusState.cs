using CMCLauncher.Platform;

namespace CMCLauncher.Avalonia.ViewModels;

public sealed class SystemStatusState : IDisposable
{
    CancellationTokenSource? lifetime;
    public double? Cpu { get; private set; }
    public string Memory { get; private set; } = "";
    public Queue<double> MemoryHistory { get; } = new();
    public IReadOnlyList<GraphicsUsage> Graphics { get; private set; } = [];
    public Queue<double> History { get; } = new();
    public Queue<double> DiskHistory { get; } = new();
    public NetworkUsage? Network { get; private set; }
    public event Action? Updated;

    public void Start(Action<Action> dispatch, string cachePath, bool verification)
    {
        if (lifetime != null)
            return;
        var cancellation = new CancellationTokenSource();
        lifetime = cancellation;
        _ = Sample(dispatch, cachePath, verification, cancellation.Token);
    }

    async Task Sample(
        Action<Action> dispatch,
        string cachePath,
        bool verification,
        CancellationToken ct
    )
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                var cpu = await LauncherPlatform.Current.CpuUsage(ct);
                var memory = await Task.Run(() => LauncherPlatform.Current.PhysicalMemory(), ct);
                IReadOnlyList<GraphicsUsage>? graphics = null;
                if (!verification)
                    try
                    {
                        graphics = await LauncherPlatform.Current.Graphics(cachePath, ct);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested) { }
                dispatch(() =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    Cpu = cpu;
                    if (cpu.HasValue)
                        History.Enqueue(cpu.Value);
                    while (History.Count > 60)
                        History.Dequeue();
                    Memory = memory is { } pair
                        ? $"{pair.TotalMb - pair.AvailableMb:N0} / {pair.TotalMb:N0} MiB"
                        : "Unavailable";
                    if (memory is { TotalMb: > 0 } sample)
                    {
                        MemoryHistory.Enqueue(
                            Math.Clamp(
                                100.0 * (sample.TotalMb - sample.AvailableMb) / sample.TotalMb,
                                0,
                                100
                            )
                        );
                        while (MemoryHistory.Count > 60)
                            MemoryHistory.Dequeue();
                    }
                    Network = LauncherPlatform.Current.NetworkUsage();
                    TrySampleDisk();
                    if (graphics != null)
                        Graphics = graphics;
                    Updated?.Invoke();
                });
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        { /* Telemetry is optional; it must never interrupt the launcher. */
        }
    }

    // 记录 Minecraft 主盘所在物理磁盘的占用率历史，供趋势图使用。
    void TrySampleDisk()
    {
        try
        {
            var drive = DriveInfo
                .GetDrives()
                .Where(d => d.IsReady)
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .FirstOrDefault();
            if (drive is { TotalSize: > 0 })
            {
                DiskHistory.Enqueue(
                    Math.Clamp(
                        100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize,
                        0,
                        100
                    )
                );
                while (DiskHistory.Count > 60)
                    DiskHistory.Dequeue();
            }
        }
        catch
        { /* Disk sampling is optional. */
        }
    }

    public void Stop()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
    }

    public void Dispose() => Stop();
}