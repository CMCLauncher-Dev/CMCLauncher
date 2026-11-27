using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace CMCLauncher.Avalonia.ViewModels;

public sealed class LauncherSession : ObservableState, IDisposable
{
    readonly Action<Action> dispatch;
    readonly object logGate = new();
    CancellationTokenSource? task;
    Instance? selected;
    Account? account;
    string status = "",
        toast = "";
    double? progress;
    public RunningGames RunningGames { get; } = new();
    public Store Store { get; }
    public LauncherCore Core { get; }
    public ModrinthService Modrinth { get; }
    public CurseForgeCommunityService CurseForge { get; }
    public CommunityService Community { get; }
    public CmsBlueprints Cms { get; private set; }
    public BlueprintService Blueprints { get; private set; }
    public FrpcManager Frpc { get; }
    public TerracottaManager Terracotta { get; }
    public FrpcConfigurations FrpcProfiles { get; }
    public LocalizationCatalog Locales { get; }
    public ThemeCatalog Themes { get; }
    public ObservableCollection<OperationState> Operations { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public string UserDirectory { get; }
    public static string HostExecutable =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image
        && File.Exists(image)
            ? image
            : Environment.ProcessPath!;
    HttpClient blueprintHttp;
    public HttpClient ImageHttp => blueprintHttp;
    readonly Dictionary<string, (OperationState Item, CancellationTokenSource Cancellation)> backgroundWork = new();
    public OperationState? Active { get; private set; }
    public bool Busy => task != null;
    public bool GameRunning => RunningGames.Any;
    static string LaunchKey(Instance instance) => "game-launch:" + instance.Id;
    public bool Launching { get; private set; }

    public OperationState? LaunchOperation(Instance? instance) =>
        instance == null ? null : backgroundWork.GetValueOrDefault(LaunchKey(instance)).Item;

    public Instance? Selected
    {
        get => selected;
        set
        {
            if (Launching)
                return;
            Set(ref selected, value);
            Store.Config.SelectedInstanceId = value?.Id;
        }
    }
    public Account? Account
    {
        get => account;
        set
        {
            Set(ref account, value);
            Store.Config.SelectedAccount = value?.Key;
        }
    }
    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }
    public string Toast
    {
        get => toast;
        private set => Set(ref toast, value);
    }
    public double? Progress
    {
        get => progress;
        private set => Set(ref progress, value);
    }
    public event Action? GameStarted;
    public event Action? GameExited;

    public LauncherSession(
        Action<Action> dispatch,
        string? root = null,
        HttpMessageHandler? handler = null
    )
    {
        this.dispatch = dispatch;
        UserDirectory = AppContext.BaseDirectory;
        Store = new Store(root);
        Locales = new LocalizationCatalog(Path.Combine(UserDirectory, "locales"));
        Themes = new ThemeCatalog(Path.Combine(UserDirectory, "themes"));
        // 加载可选的外部模组名称翻译映射表（mod_names.json），未提供时仅用内置映射。
        RemoteModNameTranslator.LoadExternal(Path.Combine(UserDirectory, "mod_names.json"));
        selected =
            Store.Config.Instances.FirstOrDefault(i => i.Id == Store.Config.SelectedInstanceId)
            ?? Store.Config.Instances.FirstOrDefault();
        account = Store.Config.Accounts.FirstOrDefault(a => a.Key == Store.Config.SelectedAccount);
        Core = new LauncherCore(Store, handler);
        Modrinth = new ModrinthService(Core, Store);
        CurseForge = new CurseForgeCommunityService(Core, Store, Modrinth);
        Community = new CommunityService(Modrinth, CurseForge);
        Cms = new CmsBlueprints(Path.Combine(Store.Root, "cache", "cms"), Store.Config.Settings);
        var imageHandler = new HttpClientHandler { AllowAutoRedirect = false };
        imageHandler.Proxy = NetworkProxy.Create(Store.Config.Settings);
        imageHandler.UseProxy = imageHandler.Proxy != null;
        blueprintHttp = new HttpClient(imageHandler) { Timeout = TimeSpan.FromSeconds(45) };
        Blueprints = new BlueprintService(blueprintHttp);
        Frpc = new FrpcManager(Store, Core);
        Terracotta = new TerracottaManager(Store, Core);
        Terracotta.Output += message => Log("TERRACOTTA", message);
        FrpcProfiles = new FrpcConfigurations(Path.Combine(Store.Root, "frpc", "configs"));
        Core.Status += Stage;
        Core.Log += message => Log("GAME", message);
        Core.DebugLog += message =>
        {
            if (Store.Config.Settings.DebugLogging)
                Log("DEBUG", message);
        };
        Core.DownloadProgress += value =>
            dispatch(() =>
            {
                if (Active != null)
                {
                    Active.Progress = value;
                    Progress = value;
                }
            });
        Core.DownloadBytes += value =>
        {
            var current = Active;
            if (current != null)
                dispatch(() => current.AddReceived(value));
        };
        Modrinth.Status += Stage;
        CurseForge.Status += Stage;
        Frpc.Output += message => Log("FRPC", message);
        Log("INFO", $"Avalonia frontend started (PID {Environment.ProcessId})");
    }

    public string Ui(string key) => Locales.Translate(Store.Config.Settings.LauncherLanguage, key);

    public void ConfigureNetwork()
    {
        if (Busy)
            throw new InvalidOperationException(Ui("A task is already running."));
        Core.ConfigureNetwork();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            Proxy = NetworkProxy.Create(Store.Config.Settings),
            UseProxy = NetworkProxy.Mode(Store.Config.Settings) != "direct",
        };
        var replacement = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var cms = new CmsBlueprints(
            Path.Combine(Store.Root, "cache", "cms"),
            Store.Config.Settings
        );
        Cms.Dispose();
        blueprintHttp.Dispose();
        blueprintHttp = replacement;
        Blueprints = new BlueprintService(blueprintHttp);
        Cms = cms;
    }

    void Stage(string value) =>
        dispatch(() =>
        {
            Status = Ui(value);
            if (Active != null)
                Active.Stage = Status;
        });

    public void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] " + LogRedactor.Redact(message);
        try
        {
            lock (logGate)
            {
                var folder = Path.Combine(Store.Root, "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, $"avalonia-{DateTime.Now:yyyyMMdd}.log"),
                    line + Environment.NewLine
                );
            }
        }
        catch { }
        dispatch(() =>
        {
            Logs.Add(line);
            while (Logs.Count > 1500)
                Logs.RemoveAt(0);
        });
    }

    public async Task<bool> Run(
        string name,
        Func<CancellationToken, Task> operation,
        bool showSuccessToast = true,
        string? backgroundKey = null
    )
    {
        if (backgroundKey == null ? Busy : backgroundWork.ContainsKey(backgroundKey))
            throw new InvalidOperationException(Ui("A task is already running."));
        using var cancellation = new CancellationTokenSource();
        var item = new OperationState(Ui(name)) { Stage = Ui("Processing…"), State = Ui("Running") };
        if (backgroundKey == null)
        {
            task = cancellation;
            Active = item;
            Status = item.Title;
            Progress = null;
            Changed(nameof(Busy));
        }
        else
            backgroundWork.Add(backgroundKey, (item, cancellation));
        Operations.Insert(0, item);
        while (Operations.Count > 30)
        {
            var oldestCompleted = Operations.LastOrDefault(work => !work.IsRunning);
            if (oldestCompleted == null)
                break;
            Operations.Remove(oldestCompleted);
        }
        try
        {
            await operation(cancellation.Token);
            item.State = Ui("Success");
            item.Stage = item.State;
            item.Progress = 100;
            if (showSuccessToast)
                Toast = item.Title + " · " + item.State;
            return true;
        }
        catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested || ex is MemoryOptimizationDeclinedException)
        {
            item.State = Ui("Cancelled");
            item.Stage = item.State;
            item.Progress = 0;
            Toast = item.Title + " · " + item.State;
            return false;
        }
        catch (Exception ex)
        {
            item.State = Ui("Failed");
            item.Stage = ex.Message;
            item.Progress = 0;
            Log("ERROR", ex.ToString());
            Toast = item.Title + " · " + item.State;
            throw;
        }
        finally
        {
            item.Finish();
            if (backgroundKey == null)
            {
                task = null;
                Active = null;
                Progress = null;
                Status = item.Stage;
                Changed(nameof(Busy));
            }
            else
                backgroundWork.Remove(backgroundKey);
        }
    }

    public void Cancel() => task?.Cancel();

    public bool CanCancel(OperationState item) => CancellationFor(item) is { IsCancellationRequested: false };

    public void Cancel(OperationState item) => CancellationFor(item)?.Cancel();

    CancellationTokenSource? CancellationFor(OperationState item) =>
        Active == item ? task : backgroundWork.Values.FirstOrDefault(entry => entry.Item == item).Cancellation;

    public void EnsureFilesIdle()
    {
        if (GameRunning || Launching)
            throw new IOException(Ui("Close the game before changing instance files."));
    }

    public void ReportStatus(string message) => Stage(message);

    public async Task RemoveAccount(Account identity)
    {
        Store.Config.Accounts.RemoveAll(account => account.Key == identity.Key);
        if (
            Account == null
            || !Store.Config.Accounts.Contains(Account)
            || !MicrosoftAuth.CanUseAccount(Store.Config.Accounts, Account)
        )
            Account = Store.Config.Accounts.FirstOrDefault(account =>
                MicrosoftAuth.CanUseAccount(Store.Config.Accounts, account)
            );
        await Save();
    }

    public async Task Save() => await Store.Save();

    public async Task AddInstance(Instance instance)
    {
        Store.Config.Instances.Add(instance);
        Selected = instance;
        await Save();
    }

    public async Task LaunchInstance(Instance instance)
    {
        var identity = Account ?? throw new IOException(Ui("Select an instance and account."));
        if (Launching)
            throw new IOException(Ui("Wait for the current game to finish starting."));
        string key = LaunchKey(instance);
        Launching = true;
        Changed(nameof(Launching));
        try
        {
            await Run("Launch game" + " · " + instance.Name, ct =>
            {
                var item = backgroundWork[key].Item;
                Changed(nameof(Launching));
                var progress = new Progress<(double Value, string Stage)>(update =>
                {
                    if (!item.IsRunning)
                        return;
                    item.Progress = Math.Max(item.Progress ?? 0, update.Value);
                    item.Stage = Ui(update.Stage);
                    Changed(nameof(Launching));
                });
                return LaunchInstanceCore(instance, identity, progress, ct);
            }, backgroundKey: key);
        }
        finally
        {
            Launching = false;
            Changed(nameof(Launching));
        }
    }

    async Task LaunchInstanceCore(Instance instance, Account identity,
        IProgress<(double Value, string Stage)> progress, CancellationToken ct)
    {
        identity = Store.Config.Accounts.FirstOrDefault(account => account.Key == identity.Key)
            ?? throw new IOException(Ui("Select an instance and account."));
        MicrosoftAuth.EnsureAccountAllowed(Store.Config.Accounts, identity);
        var auth = new MicrosoftAuth(Core);
        if (identity.Type == "microsoft")
        {
            var refreshed = await auth.Refresh(identity, MicrosoftAuth.ConfiguredClientId, ct);
            Store.Config.Accounts[Store.Config.Accounts.IndexOf(identity)] = refreshed;
            if (Account?.Key == identity.Key)
                Account = refreshed;
            identity = refreshed;
        }
        else if (identity.Type == "yggdrasil")
        {
            using var external = new ExternalAuth(Store.Config.Settings);
            var refreshed = await external.Refresh(Store.Config.Accounts, identity, ct);
            Store.Config.Accounts[Store.Config.Accounts.IndexOf(identity)] = refreshed;
            if (Account?.Key == identity.Key)
                Account = refreshed;
            identity = refreshed;
        }
        await Save();
        var launched = await Core.Launch(instance, identity, CredentialVault.Read(identity), ct, progress);
        var running = RunningGames.Add(instance, Store.GameDirectory(instance), launched);
        instance.LastPlayed = DateTime.Now;
        Log("INFO", $"Game JVM started (PID {launched.Id})");
        Action? acknowledgeExit = null;
        // Linux tracks the JVM directly. A separate launcher process must not
        // reopen the UI after a renderer failure or a visibility change.
        if (!OperatingSystem.IsLinux())
        {
            var host = HostExecutable;
            var assembly = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? typeof(Program).Assembly.Location : null;
            try
            {
                acknowledgeExit = CMCLauncher.Platform.GameExitWatchdog.Start(launched, host, assembly);
            }
            catch (Exception ex)
            {
                Log("WARN", "Could not start the independent game-exit monitor: " + ex.Message);
            }
        }

        _ = Task.Run(async () =>
        {
            // Observe the kernel exit independently of redirected output pipes.
            while (true)
            {
                launched.Refresh();
                if (launched.HasExited)
                    break;
                await Task.Delay(250);
            }
            try { await Core.CompleteOptionsLaunch(launched); }
            catch (Exception ex) { Log("ERROR", "Options backup finalization: " + ex); }
            Log("INFO", "Game exited with code " + launched.ExitCode);
            dispatch(() =>
            {
                RunningGames.Remove(running);
                launched.Dispose();
                Changed(nameof(GameRunning));
                try
                {
                    acknowledgeExit?.Invoke();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log("WARN", "Game-exit acknowledgment failed: " + ex.Message);
                }
                if (!GameRunning)
                    GameExited?.Invoke();
            });
        });
        Changed(nameof(GameRunning));
        GameStarted?.Invoke();
        await Save();
    }

    public void Dispose()
    {
        task?.Cancel();
        foreach (var entry in backgroundWork.Values.ToArray())
            entry.Cancellation.Cancel();
        Terracotta.Dispose();
        Frpc.Dispose();
        Cms.Dispose();
        CurseForge.Dispose();
        blueprintHttp.Dispose();
        Core.Dispose();
    }
}
