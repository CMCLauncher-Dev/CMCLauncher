using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    readonly VerificationHandler verificationTransport = new();

    // The verification window uses an isolated store and rejects every unplanned network request.
    async Task VerifyUi()
    {
        Directory.CreateDirectory(VerificationDirectory!);
        var checks = new List<string>();
        try
        {
            gameCatalog = [new("1.21.1", "release")];
            session.Store.Config.Settings.LauncherLanguage = "en_us";
            var instance = new Instance
            {
                Id = "ui-fixture",
                Name = "UI fixture",
                GameVersion = "1.21.1",
                Loader = "neoforge",
                Directory = Path.Combine(VerificationDirectory!, "fixture"),
            };
            Directory.CreateDirectory(instance.Directory);
            Directory.CreateDirectory(Path.Combine(instance.Directory, "schematics"));
            Directory.CreateDirectory(Path.Combine(instance.Directory, "mods"));
            Directory.CreateDirectory(Path.Combine(instance.Directory, "saves", "Fixture"));
            await File.WriteAllBytesAsync(
                Path.Combine(instance.Directory, "saves", "Fixture", "level.dat"),
                [1, 2, 3]
            );
            session.Store.Config.Instances.Clear();
            session.Store.Config.Instances.Add(instance);
            session.Selected = instance;
            foreach (
                var page in new[]
                {
                    "home",
                    "instances",
                    "downloads",
                    "mod",
                    "modpack",
                    "resourcepack",
                    "shader",
                    "datapack",
                    "world",
                    "blueprints",
                    "settings",
                    "appearance",
                    "java",
                    "accounts",
                    "logs",
                    "toolbox",
                    "terracotta",
                    "frpc",
                    "pfp",
                    "stats",
                    "tasks",
                    "about",
                }
            )
            {
                var errors = session.Logs.Count(line => line.Contains("ERROR"));
                await Navigate(page);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (
                    pageContent.Content is not Control
                    || session.Logs.Count(line => line.Contains("ERROR")) != errors
                )
                    throw new IOException("Page failed: " + page);
                checks.Add("PASS " + page);
            }
            await Navigate("mod");
            foreach (var input in pageContent.GetVisualDescendants().OfType<TextBox>())
                input.Text = "typing must not search";
            await Task.Delay(150);
            if (verificationTransport.Requests != 0)
                throw new IOException("Navigation or input unexpectedly issued a request.");
            checks.Add("PASS input and navigation do not search");
            if (session.Terracotta.Installed != null || session.Terracotta.Connected)
                throw new IOException(
                    "Toolbox navigation unexpectedly installed or started Terracotta."
                );
            checks.Add("PASS toolbox does not install or start Terracotta");
            var search = pageContent
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == "Search");
            search.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            search.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)
            );
            await Task.Delay(250);
            if (verificationTransport.Requests != 1)
                throw new IOException("Repeated search clicks were not coalesced.");
            checks.Add("PASS explicit search and duplicate-click suppression");
            var operation = new ViewModels.OperationState("Fixture task");
            if (operation.HasReceived)
                throw new IOException("Local task displayed network bytes.");
            operation.AddReceived(4096);
            operation.Progress = 42;
            if (!operation.HasReceived || operation.Percent != 42)
                throw new IOException("Task progress did not update.");
            operation.Finish();
            if (operation.IsRunning || operation.IsIndeterminate)
                throw new IOException("Completed task remained active.");
            session.Operations.Insert(0, operation);
            checks.Add("PASS task progress and terminal state");
            await InstanceSettings();
            checks.Add("PASS instance settings");
            await ContentPage(instance, "mod");
            checks.Add("PASS mod management");
            await WorldsPage(instance);
            checks.Add("PASS world management");
            await WorldBackupsPage(instance);
            checks.Add("PASS world backups");
            await SchematicsPage(instance);
            checks.Add("PASS schematic tree");
            await ExportPackPage(instance);
            checks.Add("PASS modpack export");
            foreach (var theme in new[] { "light", "dark" })
            {
                session.Store.Config.Settings.Theme = theme;
                ApplyTheme();
                await Navigate("settings");
                await Task.Delay(120);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
                bitmap.Render(this);
                bitmap.Save(Path.Combine(VerificationDirectory!, theme + ".png"));
                checks.Add("PASS " + theme + " rendering");
            }
            await File.WriteAllLinesAsync(
                Path.Combine(VerificationDirectory!, "passed.txt"),
                checks
            );
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(
                Path.Combine(VerificationDirectory!, "failure.txt"),
                ex.ToString()
            );
            Environment.ExitCode = 1;
        }
        Close();
    }

    sealed class VerificationHandler : HttpMessageHandler
    {
        public int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref Requests);
            await Task.Delay(100, cancellationToken);
            return request.RequestUri?.AbsolutePath == "/v2/search"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"hits\":[],\"total_hits\":0,\"offset\":0,\"limit\":16}"
                    ),
                }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
