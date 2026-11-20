using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CMCLauncher;

// LauncherCore 的部分类：跟踪“启动前备份了游戏选项（options.txt）”的进程，
// 在游戏进程退出时根据退出码决定是否保留或回滚该备份。
public sealed partial class LauncherCore
{
    public OptionsBackups OptionsBackups { get; }

    // 一次“带选项备份的启动”记录：关联实例与备份 Id。
    sealed record OptionsLaunch(Instance Instance, string BackupId);

    // 进程 → 启动记录的弱引用表（进程被回收后条目自动清理）。
    readonly ConditionalWeakTable<Process, OptionsLaunch> optionsLaunches = new();

    // 游戏进程退出时调用：若该进程对应一次带备份的启动，
    // 则按退出码完成备份（成功保留 / 失败回滚）。
    public Task CompleteOptionsLaunch(Process process)
    {
        if (!optionsLaunches.TryGetValue(process, out var run))
            return Task.CompletedTask;
        var code = process.ExitCode;
        return Task.Run(() => OptionsBackups.Complete(run.Instance, run.BackupId, code));
    }
}
