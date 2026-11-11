using System.Diagnostics;
using System.Runtime.InteropServices;
using CMCLauncher.Platform;

namespace CMCLauncher;

public sealed partial class WindowsLauncherPlatform
{
    [StructLayout(LayoutKind.Sequential)]
    struct CpuTime
    {
        public uint Low;
        public uint High;
        public ulong Ticks => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out CpuTime idle, out CpuTime kernel, out CpuTime user);

    ulong previousIdle,
        previousTotal;

    public override Task<double?> CpuUsage(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return Task.FromResult<double?>(null);
        var total = kernel.Ticks + user.Ticks;
        double? value =
            previousTotal == 0 || total <= previousTotal
                ? null
                : Math.Clamp(
                    100.0 * (1 - (double)(idle.Ticks - previousIdle) / (total - previousTotal)),
                    0,
                    100
                );
        previousIdle = idle.Ticks;
        previousTotal = total;
        return Task.FromResult(value);
    }

    public override async Task<IReadOnlyList<GraphicsUsage>> Graphics(
        string cachePath,
        CancellationToken ct
    )
    {
        const string script = """
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $ErrorActionPreference='Stop'
            $cache=$null
            try { $cache=Get-Content -LiteralPath $env:CMC_GPU_CACHE -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
            if($null -eq $cache -or $cache.Boot -ne $env:CMC_GPU_BOOT -or $cache.Created -lt [DateTime]::UtcNow.AddHours(-24).ToString('o')) {
                $names=@(Get-CimInstance Win32_VideoController | Where-Object { $_.PNPDeviceID -like 'PCI\*' } | Select-Object -ExpandProperty Name)
                $registry=@(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\DirectX' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath | Select-Object Description,AdapterLuid })
                $cache=@{Names=$names;Registry=$registry;Boot=$env:CMC_GPU_BOOT;Created=[DateTime]::UtcNow.ToString('o')}
                if($names.Count -gt 0) {
                    try {
                        $directory=[IO.Path]::GetDirectoryName($env:CMC_GPU_CACHE)
                        [IO.Directory]::CreateDirectory($directory) | Out-Null
                        $temporary=$env:CMC_GPU_CACHE+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
                        $cache | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $temporary -Encoding UTF8
                        Move-Item -LiteralPath $temporary -Destination $env:CMC_GPU_CACHE -Force
                    } catch { }
                }
            }
            $names=@($cache.Names)
            $registry=@($cache.Registry)
            $usage=@{}
            try {
                $samples=(Get-Counter '\GPU Engine(*)\Utilization Percentage').CounterSamples
                foreach($engine in ($samples | Group-Object { $_.InstanceName -replace '^pid_[0-9]+_','' })) {
                    if($engine.Name -match 'luid_0x([0-9a-f]+)_0x([0-9a-f]+)') {
                        $id=([uint64][Convert]::ToUInt32($Matches[1],16)*4294967296)+[Convert]::ToUInt32($Matches[2],16)
                        foreach($device in $registry) {
                            if($null -ne $device.AdapterLuid -and [uint64]$device.AdapterLuid -eq $id) {
                                $load=[Math]::Min(100,[Math]::Max(0,($engine.Group | Measure-Object CookedValue -Sum).Sum))
                                $key=[string]$device.Description
                                if(!$usage.ContainsKey($key) -or $load -gt $usage[$key]) { $usage[$key]=$load }
                            }
                        }
                    }
                }
            } catch { }
            $result=@($names | Select-Object -Unique | ForEach-Object {
                $value=-1; if($usage.ContainsKey($_)) { $value=$usage[$_] }
                @{Name=$_;Usage=$value}
            })
            ConvertTo-Json -InputObject $result -Compress
            """;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                ),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            },
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["CMC_GPU_CACHE"] = cachePath;
        process.StartInfo.Environment["CMC_GPU_BOOT"] = (
            DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64)
        ).ToString("yyyyMMddHH");
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var json = await output;
            await error;
            if (process.ExitCode != 0)
                throw new IOException("GPU adapter query failed.");
            return System.Text.Json.JsonSerializer.Deserialize(
                    json,
                    WindowsTelemetryJson.Default.GraphicsUsageArray
                )
                ?? [];
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(GraphicsUsage[]))]
internal partial class WindowsTelemetryJson
    : System.Text.Json.Serialization.JsonSerializerContext { }
