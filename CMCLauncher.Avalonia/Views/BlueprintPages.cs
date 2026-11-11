using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    sealed record BlueprintBrowse(
        string Query,
        int Page,
        IReadOnlyList<Blueprint> Items,
        bool Next,
        int? Total,
        string Message = ""
    );

    readonly Dictionary<(string Source, string Query, int Page), BlueprintBrowse> blueprintCache =
        new();
    readonly Dictionary<string, BlueprintBrowse> lastBlueprintState = new();
    string blueprintSource = "schematicraft";
    bool cmsAccepted;

    Control BlueprintsPage()
    {
        Button previous = null!,
            next = null!;
        var loading = false;
        BlueprintBrowse? current = null;
        var source = Choice(new[] { "schematicraft", "mcschematic", "cms" }, blueprintSource);
        var query = Input("", "Search blueprints");
        var results = Stack();
        var status = T("");
        var pageLabel = T("");
        var jump = Input("1");
        jump.Width = 70;
        var target = Choice(
            session.Store.Config.Instances.Where(i =>
                SchematicManager.EnsureDirectory(session.Store, i, false) != null
            ),
            session.Selected
        );
        var body = Stack(Field("Source", source), Field("Install to", target));
        var search = Btn("Search", () => Load(query.Text?.Trim() ?? "", 1), true);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
        };
        row.Children.Add(query);
        Grid.SetColumn(search, 1);
        row.Children.Add(search);
        body.Children.Add(row);
        body.Children.Add(status);
        body.Children.Add(results);
        previous = Btn("Previous", () => Load(current?.Query ?? "", (current?.Page ?? 1) - 1));
        next = Btn("Next", () => Load(current?.Query ?? "", (current?.Page ?? 1) + 1));
        body.Children.Add(
            Row(
                previous,
                pageLabel,
                jump,
                Btn(
                    "Go",
                    () =>
                        int.TryParse(jump.Text, out var value) && value > 0
                            ? Load(query.Text?.Trim() ?? "", value)
                            : throw new IOException("Enter a valid page number.")
                ),
                next
            )
        );
        void Draw(BlueprintBrowse state)
        {
            current = state;
            results.Children.Clear();
            query.Text = state.Query;
            jump.Text = state.Page.ToString();
            status.Text = state.Message;
            foreach (var entry in state.Items)
            {
                var labels = Stack(
                    T(entry.Title, 17, true),
                    T(entry.Author, 12),
                    T(entry.Downloads + " · " + entry.Versions + " · " + entry.Size, 12),
                    Btn(
                        "View blueprint details",
                        () =>
                            BlueprintDetails(
                                entry,
                                blueprintSource,
                                target.SelectedItem as Instance
                            )
                    )
                );
                var card = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("100,*"),
                    ColumnSpacing = 14,
                };
                card.Children.Add(Preview(entry.ImageUrl, blueprintSource == "cms"));
                Grid.SetColumn(labels, 1);
                card.Children.Add(labels);
                results.Children.Add(Card(card));
            }
            previous.IsEnabled = state.Page > 1;
            next.IsEnabled = state.Next;
            pageLabel.Text = state.Total is int total
                ? $"{state.Page}/{Math.Max(1, (total + (blueprintSource == "cms" ? 19 : 15)) / (blueprintSource == "cms" ? 20 : 16))}"
                : state.Page.ToString();
        }
        async Task Load(string text, int page)
        {
            if (loading || page < 1)
                return;
            var provider = source.SelectedItem?.ToString() ?? "schematicraft";
            if (provider == "cms" && !cmsAccepted)
            {
                if (!await CmsNotice())
                    return;
                cmsAccepted = true;
            }
            loading = true;
            source.IsEnabled = false;
            status.Text = Ui("Loading…");
            try
            {
                if (!blueprintCache.TryGetValue((provider, text, page), out var state))
                {
                    var ct = navigationRequest?.Token ?? default;
                    if (provider == "cms")
                    {
                        session.Cms.ResumeUserRequest();
                        var response = await session.Cms.Search(text, "download", page, ct);
                        state = new(
                            text,
                            page,
                            response.Items,
                            page < response.Pages,
                            response.Total,
                            response.Stale
                                ? Ui("Showing cached results. They may be out of date.")
                                : ""
                        );
                    }
                    else
                    {
                        var response = await session.Blueprints.Search(text, page, ct, provider);
                        state = new(text, page, response.Items, response.HasNext, response.Total);
                    }
                    blueprintCache[(provider, text, page)] = state;
                }
                blueprintSource = provider;
                lastBlueprintState[provider] = state;
                Draw(state);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
            finally
            {
                loading = false;
                source.IsEnabled = true;
            }
        }
        source.SelectionChanged += async (_, _) =>
        {
            var provider = source.SelectedItem?.ToString() ?? "schematicraft";
            if (provider == "cms")
                cmsAccepted = false;
            if (lastBlueprintState.TryGetValue(provider, out var cached) && provider != "cms")
            {
                blueprintSource = provider;
                Draw(cached);
            }
            else
                await Load("", 1);
        };
        query.KeyDown += async (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                await Load(query.Text?.Trim() ?? "", 1);
            }
        };
        if (lastBlueprintState.TryGetValue(blueprintSource, out var cached))
            Draw(cached);
        else if (!Verifying)
            _ = Load("", 1);
        return body;
    }

    async Task<bool> CmsNotice()
    {
        var window = new Window
        {
            Title = Ui("Access notice"),
            Width = 540,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var accepted = Btn(
            "I understand (3s)",
            () =>
            {
                window.Close(true);
                return Task.CompletedTask;
            },
            true
        );
        accepted.IsEnabled = false;
        window.Content = new Border
        {
            Padding = new Thickness(24),
            Child = Stack(
                T("Access notice", 20, true),
                T(
                    "CMS does not currently provide a public API. Frequent requests may cause HTTP 403 or 429 errors. Please use search responsibly and avoid repeated requests in a short period."
                ),
                accepted
            ),
        };
        var ready = false;
        window.Closing += (_, e) =>
        {
            if (!ready)
                e.Cancel = true;
        };
        window.Opened += async (_, _) =>
        {
            for (var remaining = 3; remaining > 0; remaining--)
            {
                accepted.Content = Ui("I understand") + $" ({remaining}s)";
                await Task.Delay(1000);
            }
            ready = true;
            accepted.Content = Ui("I understand");
            accepted.IsEnabled = true;
        };
        return await window.ShowDialog<bool>(this);
    }

    async Task BlueprintDetails(Blueprint entry, string source, Instance? selected)
    {
        var revision = ++generation;
        pageTitle.Text = entry.Title;
        var host = Stack(Btn("Back", () => Navigate("blueprints")), T("Loading…"));
        pageContent.Content = host;
        var ct = navigationRequest?.Token ?? default;
        if (source == "cms")
            session.Cms.ResumeUserRequest();
        var detail =
            source == "cms"
                ? (await session.Cms.Detail(entry, ct)).Detail
                : await session.Blueprints.Details(entry, source, ct);
        if (revision != generation)
            return;
        host.Children.Clear();
        host.Children.Add(Btn("Back", () => Navigate("blueprints")));
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*"),
            ColumnSpacing = 14,
        };
        header.Children.Add(Preview(entry.ImageUrl, source == "cms"));
        var labels = Stack(
            T(entry.Title, 23, true),
            T(entry.Author),
            T(entry.Downloads + " · " + entry.Versions + " · " + entry.Size),
            Btn(
                "Open website",
                () =>
                {
                    Open(entry.Website);
                    return Task.CompletedTask;
                }
            )
        );
        Grid.SetColumn(labels, 1);
        header.Children.Add(labels);
        host.Children.Add(Card(header));
        host.Children.Add(
            new Expander
            {
                Header = Ui("Description"),
                Content = MarkdownContent(detail.Entry.Description + "\n" + detail.Note),
                IsExpanded = session.Store.Config.Settings.ExpandProjectIntroduction,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            }
        );
        var targets = session
            .Store.Config.Instances.Where(i =>
                SchematicManager.EnsureDirectory(session.Store, i, false) != null
            )
            .ToArray();
        var picker = Choice(targets, selected);
        host.Children.Add(Field("Install to", picker));
        foreach (var file in detail.Files)
        {
            var install = Btn(
                "Install blueprint",
                () => SaveBlueprint(file, entry, source, picker.SelectedItem as Instance),
                true
            );
            install.IsEnabled = targets.Length > 0;
            host.Children.Add(
                Card(
                    Stack(
                        T(file.Name, 15, true),
                        T(file.Size, 12),
                        Row(
                            Btn("Download file", () => SaveBlueprint(file, entry, source, null)),
                            install
                        )
                    )
                )
            );
        }
    }

    async Task SaveBlueprint(CmsFile file, Blueprint entry, string source, Instance? target)
    {
        if (target != null)
            session.EnsureFilesIdle();
        await session.Run(
            "Download blueprint",
            async ct =>
            {
                var stage = Path.Combine(
                    session.Store.Root,
                    "blueprint-downloads",
                    Guid.NewGuid().ToString("N")
                );
                Directory.CreateDirectory(stage);
                try
                {
                    if (source == "cms")
                        session.Cms.ResumeUserRequest();
                    var downloaded =
                        source == "cms"
                            ? await session.Cms.Download(file, stage, ct)
                            : await session.Blueprints.Download(
                                entry.Id,
                                stage,
                                ct,
                                source,
                                entry.Version
                            );
                    if (target != null)
                    {
                        var directory =
                            SchematicManager.EnsureDirectory(session.Store, target)
                            ?? throw new IOException("The instance no longer supports schematics.");
                        await SchematicManager.Import(directory, [downloaded], ct);
                    }
                    else
                    {
                        var destination = await SaveFile(
                            Path.GetFileName(downloaded),
                            Path.GetExtension(downloaded)
                        );
                        if (destination != null)
                            File.Copy(downloaded, destination, true);
                    }
                }
                finally
                {
                    if (Directory.Exists(stage))
                        Directory.Delete(stage, true);
                }
            }
        );
    }

    readonly SemaphoreSlim imageGate = new(2);

    Image Preview(string url, bool cms = false)
    {
        var image = new Image
        {
            Width = 96,
            Height = 72,
            Stretch = Stretch.UniformToFill,
        };
        var started = false;
        var attached = false;
        image.AttachedToVisualTree += (_, _) => attached = true;
        image.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (image.Source is IDisposable bitmap)
                bitmap.Dispose();
            image.Source = null;
        };
        image.EffectiveViewportChanged += async (_, e) =>
        {
            if (
                started
                || string.IsNullOrWhiteSpace(url)
                || e.EffectiveViewport.Width <= 0
                || e.EffectiveViewport.Height <= 0
            )
                return;
            started = true;
            try
            {
                await imageGate.WaitAsync();
                byte[] bytes;
                try
                {
                    bytes = cms
                        ? await session.Cms.Cover(url, navigationRequest?.Token ?? default)
                        : await CoverBytes(url);
                }
                finally
                {
                    imageGate.Release();
                }
                if (attached)
                    image.Source = new Bitmap(new MemoryStream(bytes));
            }
            catch { }
        };
        return image;
    }

    async Task<byte[]> CoverBytes(string url)
    {
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Host
                is not (
                    "cdn.modrinth.com"
                    or "media.forgecdn.net"
                    or "mediafilez.forgecdn.net"
                    or "api.schematicraft.com"
                    or "schematicraft.com"
                    or "mcschematic.top"
                )
        )
            throw new IOException("Untrusted preview host.");
        var folder = Path.Combine(session.Store.Root, "cache", "avalonia-covers");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(
            folder,
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(url)
                )
            ) + ".image"
        );
        if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-24))
            return await File.ReadAllBytesAsync(path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var response = await session.ImageHttp.GetAsync(
            uri,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        if (
            response.Content.Headers.ContentLength > 4 * 1024 * 1024
            || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true
        )
            throw new IOException("Invalid preview response.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + read > 4 * 1024 * 1024)
                throw new IOException("Preview is too large.");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        await File.WriteAllBytesAsync(path, bytes, timeout.Token);
        var total = new DirectoryInfo(folder).EnumerateFiles().Sum(f => f.Length);
        foreach (
            var old in new DirectoryInfo(folder).EnumerateFiles().OrderBy(f => f.LastWriteTimeUtc)
        )
        {
            if (total <= 64L * 1024 * 1024)
                break;
            total -= old.Length;
            old.Delete();
        }
        return bytes;
    }
}
