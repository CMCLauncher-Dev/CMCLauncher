using System.Net;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    sealed class CmsUiHandler : HttpMessageHandler
    {
        public int Count;
        public bool Fail;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Count++;
            await Task.Delay(40, ct);
            if (Fail)
                return new(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("Unavailable"),
                };
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath == "/"
                            ? "<input name='csrfmiddlewaretoken' value='ui-token'>"
                        : request.RequestUri.AbsolutePath.StartsWith("/detail/")
                            ? "<h4>注释</h4><hr><div>Fixture description</div><div><div>Test.schem</div><div>下载量 3</div><div>尺寸：5×5×5</div><a href='/download/9/'>下载</a></div>"
                        : "<div>1 个结果，1 页</div><a class='list_result' href='/detail/1/'><h2 class='title'>CMS preview fixture</h2><div class='author'>作者：Fixture</div><div class='download'>下载量：100</div></a>",
                    Encoding.UTF8,
                    "text/html"
                ),
            };
        }
    }

    sealed class BlueprintUiHandler : HttpMessageHandler
    {
        public int Count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Count++;
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path == "/schematics")
                json =
                    "[{\"id\":\"fixture\",\"title\":\"Shared blueprint layout\",\"owner\":{\"username\":\"Builder\"}}]";
            else if (path == "/api/v1/blueprints")
                json =
                    "{\"items\":[{\"id\":\"fixture\",\"title\":\"Shared blueprint layout\",\"author\":{\"displayName\":\"Builder\"},\"currentVersion\":{\"number\":2}}],\"totalPages\":1,\"totalCount\":1}";
            else if (path.EndsWith("/versions/2"))
                json = "{\"originalFilename\":\"shared.schem\"}";
            else
                json =
                    "{\"description\":\"Full blueprint introduction\",\"originalFilename\":\"shared.litematic\",\"contentMarkdown\":\"Shared introduction\"}";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    internal async Task VerifyCmsUi(string directory)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
            File.AppendAllText(
                Path.Combine(directory, "passed.txt"),
                message + Environment.NewLine
            );
        }
        await SwitchLanguage("zh_cn");
        await VerifyToolboxUi(Check, directory);
        store.Config.Settings.Theme = "light";
        store.Config.Settings.ColorPreset = "native";
        ApplyTheme();
        var handler = new CmsUiHandler();
        cmsClient = new CmsBlueprints(
            Path.Combine(directory, "cms-cache"),
            store.Config.Settings,
            handler
        );
        page = "download-blueprint";
        blueprintSource = "cms";
        expandedNavigation = "downloads";
        var rendering = RenderAsync();
        for (var attempt = 0; attempt < 200 && !cmsNoticeOpened; attempt++)
            await Task.Delay(30);
        var notice = cmsNoticeDialog ?? throw new Exception("CMS notice modal did not open");
        Check(cmsNoticeOpened, "CMS modal has finished opening before interaction");
        Check(
            !notice.IsPrimaryButtonEnabled && handler.Count == 0,
            "CMS modal blocks confirmation and requests before reading"
        );
        notice.Hide();
        await Task.Delay(100);
        Check(
            cmsNoticeDialog == notice,
            "CMS modal cannot be dismissed during its three-second countdown"
        );
        for (var attempt = 0; attempt < 200 && !notice.IsPrimaryButtonEnabled; attempt++)
            await Task.Delay(30);
        Check(notice.IsPrimaryButtonEnabled, "CMS modal enables confirmation after three seconds");
        var confirm = Descendants(notice).OfType<Button>().First(b => b.Name == "PrimaryButton");
        (
            (IInvokeProvider)new ButtonAutomationPeer(confirm).GetPattern(PatternInterface.Invoke)
        ).Invoke();
        await rendering;
        await Task.Delay(200);
        Check(
            !Descendants(PageBody)
                .OfType<Expander>()
                .Any(e => e.Header?.ToString() == Ui("CMS request settings")),
            "CMS page does not expose request settings"
        );
        var initial = handler.Count;
        Check(
            initial == 2,
            "CMS initial browsing makes one session request and one first-page search"
        );
        var input = Descendants(PageBody)
            .OfType<TextBox>()
            .Single(t => t.Tag?.ToString() == "cms-search-input");
        input.Text = "user typing";
        input.Text = "pasted content";
        input.Focus(FocusState.Programmatic);
        input.Text = "";
        input.Text = "explicit query";
        await Task.Delay(1800);
        Check(
            handler.Count == initial,
            "typing, replacing, clearing and focusing CMS search never sends requests"
        );
        var button = Descendants(PageBody)
            .OfType<Button>()
            .Single(b => b.Tag?.ToString() == "cms-search-button");
        void Click() =>
            (
                (IInvokeProvider)
                    new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)
            ).Invoke();
        Click();
        for (var i = 0; i < 120 && (!button.IsEnabled || handler.Count == initial); i++)
            await Task.Delay(30);
        Check(
            handler.Count == initial + 1,
            "explicit search issues exactly one CMS search request"
        );
        Click();
        await Task.Delay(200);
        Check(handler.Count == initial + 1, "repeating the same explicit search uses cached data");
        page = "more";
        await RenderAsync();
        page = "download-blueprint";
        await RenderAsync();
        Check(
            handler.Count == initial + 1,
            "switching away and back restores CMS results without a new request"
        );
        input = Descendants(PageBody)
            .OfType<TextBox>()
            .Single(t => t.Tag?.ToString() == "cms-search-input");
        button = Descendants(PageBody)
            .OfType<Button>()
            .Single(b => b.Tag?.ToString() == "cms-search-button");
        input.Text = "failed-query";
        handler.Fail = true;
        Click();
        for (var i = 0; i < 120 && (!button.IsEnabled || handler.Count == initial + 1); i++)
            await Task.Delay(30);
        Check(
            Descendants(PageBody)
                .OfType<Border>()
                .Count(b => b.Tag?.ToString() == "cms-blueprint-card") == 1,
            "failed CMS search preserves the current result list"
        );
        Check(
            Descendants(PageBody)
                .OfType<InfoBar>()
                .Any(b => b.Severity == InfoBarSeverity.Warning && b.Message.Contains("CMS")),
            "CMS network failure is shown inline"
        );
        handler.Fail = false;
        var beforeDetail = handler.Count;
        await CmsDetails(cmsSnapshot!.Items[0], () => null, false);
        Check(
            page == "blueprint-detail"
                && PageBackButton.Visibility == Visibility.Visible
                && !dialogOpen,
            "CMS details use a full page with list navigation instead of a modal"
        );
        Check(
            Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "Test.schem")
                && Descendants(PageBody).OfType<Expander>().Any(),
            "CMS details show expandable description and downloadable files using standard cards"
        );
        await Capture(Path.Combine(directory, "cms-detail.png"));
        Check(handler.Count == beforeDetail + 1, "CMS details fetch exactly one requested page");
        PageBack_Click(PageBackButton, new RoutedEventArgs());
        for (
            var i = 0;
            i < 100
                && !Descendants(PageBody)
                    .OfType<TextBox>()
                    .Any(t => t.Tag?.ToString() == "cms-search-input");
            i++
        )
            await Task.Delay(30);
        Check(
            page == "download-blueprint" && handler.Count == beforeDetail + 1,
            "returning from CMS details restores the list without refetching"
        );
        await Capture(Path.Combine(directory, "cms-light.png"));
        store.Config.Settings.Theme = "dark";
        ApplyTheme();
        ApplyPersonalization();
        await Task.Delay(150);
        await Capture(Path.Combine(directory, "cms-dark.png"));
        store.Config.Settings.ColorPreset = "craft";
        ApplyTheme();
        ApplyPersonalization();
        await Task.Delay(150);
        await Capture(Path.Combine(directory, "cms-custom.png"));
        var apiHandler = new BlueprintUiHandler();
        blueprintHttpOverride = () => new HttpClient(apiHandler, false);
        foreach (var source in new[] { "schematicraft", "mcschematic" })
        {
            blueprintSource = source;
            blueprintQuery = "";
            blueprintPage = 1;
            page = "download-blueprint";
            await RenderAsync();
            Check(
                Descendants(PageBody)
                    .OfType<Border>()
                    .Any(b => b.Tag?.ToString() == "blueprint-card"),
                source + " uses shared blueprint list cards"
            );
            var entry = blueprintListCache[(source, "", 1)].Items.Single();
            await OpenBlueprintDetails(entry, null, source);
            Check(
                page == "blueprint-detail"
                    && !dialogOpen
                    && PageBackButton.Visibility == Visibility.Visible,
                source + " opens the same standalone blueprint detail route"
            );
            Check(
                Descendants(PageBody).OfType<Expander>().Any()
                    && Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Any(t => t.Text.StartsWith("shared.")),
                source + " renders the shared description and download-file layout"
            );
            await Capture(Path.Combine(directory, source + "-detail.png"));
            var requests = apiHandler.Count;
            PageBack_Click(PageBackButton, new RoutedEventArgs());
            for (
                var i = 0;
                i < 100
                    && !Descendants(PageBody)
                        .OfType<Border>()
                        .Any(b => b.Tag?.ToString() == "blueprint-card");
                i++
            )
                await Task.Delay(30);
            Check(
                apiHandler.Count == requests,
                source + " returns to cached list without an extra request"
            );
        }
        blueprintHttpOverride = null;
    }
}
