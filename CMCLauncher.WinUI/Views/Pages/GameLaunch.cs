using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void StartInstanceDebugLog(Instance instance)
    {
        debugWindow?.CloseSafe();
        debugWindow = null;
        if (!instance.DebugMode)
            return;
        debugWindow = new GameDebugWindow(lightTheme, PrimaryColor, instance.Name, Ui);
        debugWindow.Activate();
    }

    // 修改任意窗口标题；游戏主窗口出现后按设置重命名。
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetWindowText(IntPtr hWnd, string text);

    Process? game;
    string? runningInstanceId;
    bool launching;
    string? launcherExitBehavior;
    GameDebugWindow? debugWindow;
    TextBlock? launchStatusText;
    ProgressBar? launchCardProgress;
    Button? launchCardCancel;

    // 先验证账号并补全依赖，出现游戏窗口后才把启动任务标记完成。
    async Task Launch(Instance instance)
    {
        if (task != null)
            throw new Exception("已有任务进行中，请稍候或取消当前任务");
        // 启动前分析 mods 目录的缺失依赖与版本冲突；有告警先请用户确认。
        var diagnostics = await Task.Run(() =>
            ModDependencyChecker.Check(store.GameDirectory(instance))
        );
        if (diagnostics.Count > 0)
        {
            var body = new StackPanel { Spacing = 6, MaxWidth = 380 };
            foreach (var issue in diagnostics)
                body.Children.Add(
                    new TextBlock
                    {
                        Text = UiFormat(issue.MessageKey, issue.Args),
                        TextWrapping = TextWrapping.Wrap,
                    }
                );
            var anchor = Content as FrameworkElement ?? launchStatusText;
            if (anchor != null)
            {
                var proceed = await ConfirmPop(
                    anchor,
                    Ui("Mod dependency issues found"),
                    body,
                    Ui("Launch anyway")
                );
                if (!proceed)
                {
                    AddLog("Canceled launch after dependency warnings.");
                    return;
                }
            }
        }
        launching = true;
        launchProgressValue = 0;
        RefreshLaunchCard("正在准备启动…");
        StartInstanceDebugLog(instance);
        var progress = new Progress<(double Value, string Stage)>(update =>
        {
            UpdateLaunchProgress(update.Value, update.Stage);
            debugWindow?.SetProgress(update.Value, Ui(update.Stage));
        });
        try
        {
            await Run(
                async ct =>
                {
                    var launchStarted = DateTimeOffset.UtcNow;
                    var account =
                        store.Config.Accounts.FirstOrDefault(a =>
                            a.Key == store.Config.SelectedAccount
                        )
                        ?? throw new Exception("请先添加账号");
                    MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
                    UpdateLaunchProgress(0, "正在验证账号…");
                    if (account.Type == "yggdrasil")
                    {
                        // Refresh the ownership account before sending any third-party token.
                        var owner = store.Config.Accounts.First(
                            MicrosoftAuth.IsVerifiedMinecraftPlayer
                        );
                        var verified = await auth.Refresh(
                            owner,
                            MicrosoftAuth.ConfiguredClientId,
                            ct
                        );
                        store.Config.Accounts[store.Config.Accounts.IndexOf(owner)] = verified;
                        using var external = new ExternalAuth(store.Config.Settings);
                        var refreshed = await external.Refresh(store.Config.Accounts, account, ct);
                        store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed;
                        account = refreshed;
                        await store.Save();
                    }
                    if (account.Type == "microsoft")
                    {
                        var refreshed = await auth.Refresh(
                            account,
                            MicrosoftAuth.ConfiguredClientId,
                            ct
                        );
                        store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed;
                        account = refreshed;
                        await store.Save();
                    }
                    var launchedGame = await core.Launch(
                        instance,
                        account,
                        CredentialVault.Read(account),
                        ct,
                        progress
                    );
                    var visibilityOnLaunch = store.Config.Settings.LauncherVisibility;
                    var acknowledgeExit = visibilityOnLaunch is "close" or "hide_close"
                        ? (Action)(() => { })
                        : GameExitRestart.Watch(
                            launchedGame,
                            LauncherUpdateInstaller.Executable,
                            Path.Combine(store.Root, "logs", "game-exit-restart.log")
                        );
                    game = launchedGame;
                    runningInstanceId = instance.Id;
                    // 界面正常处理退出时通知监视进程；只有界面失去响应才由它恢复启动器。
                    var monitoringDone = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    async Task ObserveExit()
                    {
                        try
                        {
                            // Observe kernel exit without waiting for inherited output pipes to close.
                            while (!launchedGame.HasExited)
                                await Task.Delay(250);
                            await monitoringDone.Task;
                            AddLog(instance.Name + ": game exited; applying launcher visibility setting");
                        }
                        catch (Exception ex)
                        {
                            AddLog("Game exit monitoring failed: " + ex.Message);
                        }
                        finally
                        {
                            await monitoringDone.Task;
                            if (ReferenceEquals(game, launchedGame))
                            {
                                game = null;
                                runningInstanceId = null;
                            }
                            try
                            {
                                acknowledgeExit();
                            }
                            catch (Exception ex)
                            {
                                AddLog("Game-exit acknowledgment failed: " + ex.Message);
                            }
                            debugWindow?.SetProgress(100, Ui("Game exited"));
                            RefreshLaunchCard();
                            try
                            {
                                if (!ct.IsCancellationRequested)
                                    await HandleGameExit(instance, launchedGame, launchStarted);
                            }
                            finally
                            {
                                launchedGame.Dispose();
                                ApplyLauncherVisibilityOnExit();
                            }
                        }
                    }
                    _ = ObserveExit();
                    try
                    {
                        instance.LastPlayed = DateTime.Now;
                        await store.Save();
                        UpdateLaunchProgress(99, "等待游戏窗口出现…");
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            launchedGame.Refresh();
                            if (launchedGame.HasExited)
                                throw new Exception(
                                    $"游戏在窗口出现前退出，退出码 {launchedGame.ExitCode}。请查看运行日志。"
                                );
                            if (launchedGame.MainWindowHandle != IntPtr.Zero)
                                break;
                            await Task.Delay(250, ct);
                        }
                        UpdateLaunchProgress(100, "游戏窗口已出现");
                        debugWindow?.SetProgress(100, Ui("Game is running"));
                        if (!string.IsNullOrWhiteSpace(store.Config.Settings.GameWindowTitle))
                            SetWindowText(
                                launchedGame.MainWindowHandle,
                                store.Config.Settings.GameWindowTitle
                            );
                        await Task.Delay(350, ct);
                        if (launchedGame.HasExited)
                            throw new Exception(
                                $"游戏在启动期间退出，退出码 {launchedGame.ExitCode}。请查看运行日志。"
                            );
                    }
                    catch (OperationCanceledException)
                    {
                        if (!launchedGame.HasExited)
                            launchedGame.Kill(true);
                        await launchedGame.WaitForExitAsync();
                        throw;
                    }
                    finally
                    {
                        monitoringDone.TrySetResult();
                    }
                },
                "启动游戏 · " + instance.Name
            );
            Report("游戏正在运行");
            ApplyLaunchVisibility();
        }
        catch (OperationCanceledException)
        {
            debugWindow?.SetProgress(0, Ui("Cancelled"));
            throw;
        }
        catch (Exception ex)
        {
            debugWindow?.SetProgress(0, ex.Message);
            throw;
        }
        finally
        {
            launching = false;
            RefreshLaunchCard();
        }
    }

    // 根据「启动器可见性」设置决定游戏启动后对窗口的处置。
    void ApplyLaunchVisibility()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            return;
        switch (store.Config.Settings.LauncherVisibility)
        {
            case "hide_close":
                launcherExitBehavior = "close";
                AppWindow.Hide();
                break;
            case "hide_reopen":
                launcherExitBehavior = "reopen";
                AppWindow.Hide();
                break;
            case "minimize":
                presenter.Minimize();
                break;
            case "close":
                Close();
                break;
        }
    }

    // 「隐藏并重新打开」：游戏退出后在弹崩溃报告前先行恢复窗口，
    // 使崩溃对话框有可见的宿主窗口，否则在隐藏窗口上显示会异常或卡住。
    async Task RestoreLauncherWindowAsync()
    {
        if (launcherExitBehavior != "reopen")
            return;
        launcherExitBehavior = null;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                    AppWindow.Show();
            }
            catch (Exception ex)
            {
                AddLog("Restore launcher window failed: " + ex.Message);
            }
            finally
            {
                completion.TrySetResult();
            }
        });
        await completion.Task;
    }

    // 游戏退出后，依据先前选择的可见性偏好恢复/关闭启动器窗口。
    void ApplyLauncherVisibilityOnExit()
    {
        // 一次性消费：仅当退出前确实设置了退出行为才处理，随后复位，防止重复触发。
        if (launcherExitBehavior is not { } behavior)
            return;
        launcherExitBehavior = null;

        DispatcherQueue.TryEnqueue(() => ApplyWindowBehavior(behavior));
    }

    // 将字符串行为码映射为具体的窗口副作用。
    void ApplyWindowBehavior(string behavior)
    {
        // 分派表：把「行为标识 -> 副作用」的对应关系收敛到一处，方便后续扩展新行为。
        var dispatchTable = new Dictionary<string, Action?>
        {
            ["close"] = Close,
            ["reopen"] = () =>
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                    AppWindow.Show();
            },
        };

        // 未知行为码视为空操作 —— 保守地忽略，避免误关窗或误显示。
        dispatchTable.GetValueOrDefault(behavior)?.Invoke();
    }
//猜你看不懂-—-fuzhu
    // 并发依赖的进度回调可能乱序，显示值只前进，不往回跳。
    void UpdateLaunchProgress(double value, string stage)
    {
        if (!launching)
            return;
        launchProgressValue = Math.Max(launchProgressValue, Math.Clamp(value, 0, 100));
        if (activeWork != null)
        {
            activeWork.Progress = launchProgressValue;
            activeWork.Stage = stage;
            RefreshTaskMetrics();
        }
        RefreshLaunchCard(stage);
    }
}