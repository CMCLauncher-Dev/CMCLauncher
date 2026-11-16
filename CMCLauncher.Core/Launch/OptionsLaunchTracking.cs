using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CMCLauncher;

public sealed partial class LauncherCore
{
    public OptionsBackups OptionsBackups { get; }

    sealed record OptionsLaunch(Instance Instance, string BackupId);

    readonly ConditionalWeakTable<Process, OptionsLaunch> optionsLaunches = new();

    public Task CompleteOptionsLaunch(Process process)
    {
        if (!optionsLaunches.TryGetValue(process, out var run))
            return Task.CompletedTask;
        var code = process.ExitCode;
        return Task.Run(() => OptionsBackups.Complete(run.Instance, run.BackupId, code));
    }
}
