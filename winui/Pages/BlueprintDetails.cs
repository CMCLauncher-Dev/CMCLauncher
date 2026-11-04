using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Blueprint? detailBlueprint;
    Instance? detailTarget;

    string detailSource = "cms";

    Task CmsDetails(Blueprint entry, Func<Instance?> target, bool hasTarget) =>
        OpenBlueprintDetails(entry, target(), "cms");

    async Task OpenBlueprintDetails(Blueprint entry, Instance? target, string source)
    {
        detailBlueprint = entry;
        detailTarget = target;
        detailSource = source;
        resourceReturnPage = "download-blueprint";
        page = "blueprint-detail";
        if (source == "cms")
            Cms.ResumeUserRequest();
        await RenderAsync();
    }

    async Task BlueprintDetailPage()
    {
        if (detailBlueprint is not { } entry)
        {
            page = "download-blueprint";
            await RenderAsync();
            return;
        }
        Header("", "");
        var generation = renderVersion;
        var identity = new StackPanel { Spacing = 5 };
        identity.Children.Add(Text(entry.Title, 21, true));
        foreach (
            var text in new[]
            {
                entry.Author,
                entry.Downloads.Length > 0 ? Ui("Downloads: ") + entry.Downloads : "",
                entry.Versions,
                entry.Size,
            }
        )
            if (text.Length > 0)
                identity.Children.Add(Text(text, 13));
        var heading = new Grid { ColumnSpacing = 14 };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var preview = new Grid { Width = 120, Height = 90 };
        preview.Children.Add(
            new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 28,
                Opacity = .5,
            }
        );
        AddBlueprintPreview(preview, entry, generation, detailSource);
        heading.Children.Add(preview);
        Grid.SetColumn(identity, 1);
        heading.Children.Add(identity);
        PageBody.Children.Add(Card(heading));
        PageBody.Children.Add(
            new HyperlinkButton
            {
                Content = Ui("View on website"),
                NavigateUri = new Uri(entry.Website),
            }
        );
        var status = new InfoBar
        {
            IsOpen = true,
            Severity = InfoBarSeverity.Informational,
            Message = Ui("Loading blueprint details…"),
        };
        PageBody.Children.Add(status);
        var host = new StackPanel { Spacing = 8 };
        PageBody.Children.Add(host);
        if (pageFrameReady)
            NavigatePage(PageBody);
        using var request = new CancellationTokenSource(resourceDetailTimeout);
        resourceSearch = request;
        try
        {
            CmsDetail data;
            bool stale = false;
            if (detailSource == "cms")
            {
                var cached = await Cms.Detail(entry, request.Token);
                data = cached.Detail;
                stale = cached.Stale;
            }
            else
            {
                using var http = BlueprintHttp();
                data = await new BlueprintService(http).Details(entry, detailSource, request.Token);
            }
            var result = (Detail: data, Stale: stale);
            if (generation != renderVersion)
                return;
            status.IsOpen = result.Stale;
            status.Severity = InfoBarSeverity.Warning;
            status.Message = Ui("Cached details may be out of date.");
            var description = string.Join(
                "\n\n",
                new[] { result.Detail.Entry.Description, result.Detail.Note }.Where(value =>
                    !string.IsNullOrWhiteSpace(value)
                )
            );
            if (description.Length > 0)
                host.Children.Add(
                    new Expander
                    {
                        Header = Ui("Project description"),
                        IsExpanded = store.Config.Settings.ExpandProjectIntroduction,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Content = new ScrollViewer
                        {
                            MaxHeight = 360,
                            Content =
                                detailSource == "mcschematic"
                                    ? RenderMarkdown(description)
                                    : Text(description, 13),
                        },
                    }
                );
            var candidates = store.Config.Instances.ToArray();
            var compatible = await Task.Run(
                () =>
                    candidates
                        .Where(instance =>
                        {
                            try
                            {
                                return SchematicManager.EnsureDirectory(store, instance, false)
                                    != null;
                            }
                            catch (Exception ex)
                                when (ex
                                        is IOException
                                            or UnauthorizedAccessException
                                            or ArgumentException
                                )
                            {
                                return false;
                            }
                        })
                        .ToArray(),
                request.Token
            );
            if (generation != renderVersion)
                return;
            var target =
                compatible.FirstOrDefault(i => i == detailTarget) ?? compatible.FirstOrDefault();
            var picker = new BelowComboBox
            {
                Header = Ui("Install to instance"),
                ItemsSource = compatible,
                DisplayMemberPath = nameof(Instance.Name),
                SelectedItem = target,
                IsEnabled = compatible.Length > 0,
                PlaceholderText = Ui("No compatible instances"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            picker.SelectionChanged += (_, _) =>
            {
                target = picker.SelectedItem as Instance;
                detailTarget = target;
            };
            host.Children.Add(picker);
            host.Children.Add(
                Text(
                    compatible.Length == 0
                        ? "No compatible instances. Install Create or a schematic mod in a modded instance first. You can still download files."
                        : "Choose a blueprint format your mod supports. Installing a file does not convert it or install required mods.",
                    12
                )
            );
            host.Children.Add(Text("Downloads", 16, true));
            foreach (var file in result.Detail.Files)
            {
                var labels = new StackPanel { Spacing = 3 };
                labels.Children.Add(Text(file.Name, 13, true));
                if (file.Size.Length > 0)
                    labels.Children.Add(Text(file.Size, 11));
                var install = Action(
                    "Install blueprint",
                    async (_, _) => await SaveBlueprintDetail(file, entry, true, target, status)
                );
                if (compatible.Length == 0)
                {
                    install.IsEnabled = false;
                    taskButtons.Remove(install);
                }
                var actions = Buttons(
                    Action(
                        "Download file",
                        async (_, _) => await SaveBlueprintDetail(file, entry, false, null, status)
                    ),
                    install
                );
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(labels);
                Grid.SetColumn(actions, 1);
                row.Children.Add(actions);
                AdaptCard(row, labels, actions);
                host.Children.Add(CompactCard(row));
            }
            ApplyPersonalization();
        }
        catch (OperationCanceledException) when (generation != renderVersion) { }
        catch (Exception ex)
        {
            if (generation != renderVersion)
                return;
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Warning;
            status.Message = CmsError(ex);
            host.Children.Add(
                Action(
                    "重新加载",
                    async (_, _) =>
                    {
                        if (detailSource == "cms")
                            Cms.ResumeUserRequest();
                        await RenderAsync();
                    }
                )
            );
        }
        finally
        {
            if (ReferenceEquals(resourceSearch, request))
                resourceSearch = null;
        }
    }

    async Task SaveBlueprintDetail(
        CmsFile file,
        Blueprint entry,
        bool install,
        Instance? target,
        InfoBar status
    )
    {
        if (detailSource == "cms")
        {
            await CmsSave(file, install, target, status);
            return;
        }
        try
        {
            await DownloadBlueprint(entry, install, detailSource, target);
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Success;
            status.Message = Ui("Blueprint saved successfully.");
        }
        catch (OperationCanceledException)
        {
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Informational;
            status.Message = Ui("Canceled");
        }
        catch (Exception ex)
        {
            status.IsOpen = true;
            status.Severity = InfoBarSeverity.Warning;
            status.Message = Ui(ex.Message);
        }
    }

    void AddBlueprintPreview(Grid preview, Blueprint entry, int generation, string source)
    {
        if (entry.ImageUrl.Length == 0)
            return;
        if (source == "cms")
        {
            CmsPreview(preview, entry.ImageUrl, generation);
            return;
        }
        if (
            Uri.TryCreate(entry.ImageUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == "https"
            && uri.UserInfo.Length == 0
        )
        {
            var image = new Image
            {
                Source = new BitmapImage(uri) { DecodePixelWidth = 240 },
                Width = 120,
                Height = 90,
                Stretch = Stretch.Uniform,
            };
            image.ImageFailed += (_, _) => image.Visibility = Visibility.Collapsed;
            preview.Children.Add(image);
        }
    }

    Border BlueprintListCard(Blueprint entry, Func<Instance?> target, string source)
    {
        var generation = renderVersion;
        var body = new StackPanel { Spacing = 5 };
        body.Children.Add(Text(entry.Title, 15, true));
        foreach (
            var value in new[]
            {
                entry.Author,
                entry.Downloads.Length > 0 ? Ui("Downloads: ") + entry.Downloads : "",
                entry.Versions,
                entry.Size,
            }
        )
            if (value.Length > 0)
                body.Children.Add(Text(value, 12));
        if (entry.Description.Length > 0)
        {
            var text = Text(entry.Description.Replace('\r', ' ').Replace('\n', ' '), 12);
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(text);
        }
        body.Children.Add(
            Action(
                "View blueprint details",
                async (_, _) => await OpenBlueprintDetails(entry, target(), source)
            )
        );
        var preview = new Grid
        {
            Width = 120,
            Height = 90,
            VerticalAlignment = VerticalAlignment.Top,
        };
        preview.Children.Add(
            new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 28,
                Opacity = .5,
            }
        );
        AddBlueprintPreview(preview, entry, generation, source);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(preview);
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        var card = CompactCard(row);
        card.Tag = source == "cms" ? "cms-blueprint-card" : "blueprint-card";
        card.Tapped += async (_, e) =>
        {
            for (
                var node = e.OriginalSource as DependencyObject;
                node != null && node != card;
                node = VisualTreeHelper.GetParent(node)
            )
                if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                    return;
            if (!dialogOpen)
                await OpenBlueprintDetails(entry, target(), source);
        };
        return card;
    }
}
