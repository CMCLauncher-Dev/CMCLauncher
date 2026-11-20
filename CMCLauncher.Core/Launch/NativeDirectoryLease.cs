using System.Diagnostics;

namespace CMCLauncher;

// 每个游戏进程独占一个全新的 natives 目录（.craft-natives/<guid>），
// 旧 DLL 与其他正在运行的实例都无法改动它；进程退出后自动清理。人话傻叼着东西要杀掉——fuzhu
internal sealed class NativeDirectoryLease : IDisposable
{
    public string Path { get; }
    readonly Action<string> log;
    // 进程是否已启动（启动后由进程退出事件负责清理，Dispose 不再清理）。
    bool running;

    // 创建并返回本进程独占的 natives 目录。
    public NativeDirectoryLease(string game, Action<string> log)
    {
        this.log = log;
        Path = Store.SafePath(game, ".craft-natives/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    // 监听游戏进程退出，退出后异步清理 natives 目录。
    public void Watch(Process process)
    {
        process.Exited += (_, _) =>
            _ = Task.Run(async () =>
            {
                string? error = null;
                // Windows may release mapped DLLs shortly after signaling process exit.
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (TryClean(out error))
                        return;
                    await Task.Delay(250);
                }
                log("Could not remove game native directory: " + error);
            });
    }

    // 标记进程已启动（此后由退出事件负责清理）。
    public void Started() => running = true;

    // 若进程从未启动，则在此清理 natives 目录。
    public void Dispose()
    {
        if (!running && !TryClean(out var error))
            log("Could not remove game native directory: " + error);
    }

    // 尝试删除 natives 目录（仅当存在且非符号链接时），失败返回 false 并附错误信息。
    bool TryClean(out string? error)
    {
        error = null;
        try
        {
            if (
                Directory.Exists(Path)
                && (File.GetAttributes(Path) & FileAttributes.ReparsePoint) == 0
            )
                Directory.Delete(Path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
