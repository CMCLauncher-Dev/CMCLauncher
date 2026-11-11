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
    Process? game;
    public Store Store { get; }
    public LauncherCore Core { get; }
    public ModrinthService Modrinth { get; }
    public CurseForgeCommunityService CurseForge { get; }
    public CommunityService Community { get; }
    public CmsBlueprints Cms { get; private set; }
    public BlueprintService Blueprints { get; private set; }
    public FrpcManager Frpc { get; }
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
    public OperationState? Active { get; private set; }
    public bool Busy => task != null;
    public bool ExitWatchdogRunning { get; private set; }
    public bool GameRunning => game is { HasExited: false };
    public Instance? Selected
    {
        get => selected;
        set
        {
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
    public event Action? RestartRequested;

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
        Log("INFO", "Avalonia frontend started");
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

    public async Task Run(string name, Func<CancellationToken, Task> operation)
    {
        if (Busy)
            throw new InvalidOperationException(Ui("A task is already running."));
        using var cancellation = new CancellationTokenSource();
        task = cancellation;
        Changed(nameof(Busy));
        var item = new OperationState(Ui(name));
        Active = item;
        Operations.Insert(0, item);
        while (Operations.Count > 30)
            Operations.RemoveAt(Operations.Count - 1);
        Status = item.Title;
        Progress = null;
        try
        {
            await operation(cancellation.Token);
            item.State = Ui("Success");
            item.Stage = item.State;
            item.Progress = 100;
            Toast = item.Title + " · " + item.State;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            item.State = Ui("Cancelled");
            item.Stage = item.State;
            item.Progress = 0;
            Toast = item.Title + " · " + item.State;
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
            task = null;
            Active = null;
            Progress = null;
            Status = item.Stage;
            Changed(nameof(Busy));
        }
    }

    public void Cancel() => task?.Cancel();

    public void EnsureFilesIdle()
    {
        if (GameRunning)
            throw new IOException(Ui("Close the game before changing instance files."));
    }

    public async Task Save() => await Store.Save();

    public async Task AddInstance(Instance instance)
    {
        Store.Config.Instances.Add(instance);
        Selected = instance;
        await Save();
    }

    public async Task LaunchSelected(CancellationToken ct)
    {
        if (Selected is not { } instance || Account is not { } identity)
            throw new IOException(Ui("Select an instance and account."));
        if (GameRunning)
            throw new IOException(Ui("The game is already running."));
        MicrosoftAuth.EnsureAccountAllowed(Store.Config.Accounts, identity);
        var auth = new MicrosoftAuth(Core);
        if (identity.Type == "microsoft")
        {
            var refreshed = await auth.Refresh(identity, MicrosoftAuth.ConfiguredClientId, ct);
            Store.Config.Accounts[Store.Config.Accounts.IndexOf(identity)] = refreshed;
            Account = identity = refreshed;
        }
        else if (identity.Type == "yggdrasil")
        {
            using var external = new ExternalAuth(Store.Config.Settings);
            var refreshed = await external.Refresh(Store.Config.Accounts, identity, ct);
            Store.Config.Accounts[Store.Config.Accounts.IndexOf(identity)] = refreshed;
            Account = identity = refreshed;
        }
        await Save();
        game = await Core.Launch(instance, identity, CredentialVault.Read(identity), ct);
        instance.LastPlayed = DateTime.Now;
        await Save();
        var host = HostExecutable;
        var assembly = Path.GetFileNameWithoutExtension(host)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? typeof(Program).Assembly.Location
            : null;
        try
        {
            CMCLauncher.Platform.GameExitWatchdog.Start(game, host, assembly);
            ExitWatchdogRunning = true;
        }
        catch (Exception ex)
        {
            ExitWatchdogRunning = false;
            Log("WARN", "Could not start the independent game-exit monitor: " + ex.Message);
        }

        var launched = game;
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
            Log("INFO", "Game exited with code " + launched.ExitCode);
            dispatch(() =>
            {
                if (ReferenceEquals(game, launched))
                    game = null;
                launched.Dispose();
                Changed(nameof(GameRunning));
                RestartRequested?.Invoke();
            });
        });
        Changed(nameof(GameRunning));
    }

    public void Dispose()
    {
        task?.Cancel();
        Frpc.Dispose();
        Cms.Dispose();
        CurseForge.Dispose();
        blueprintHttp.Dispose();
        Core.Dispose();
    }
}
