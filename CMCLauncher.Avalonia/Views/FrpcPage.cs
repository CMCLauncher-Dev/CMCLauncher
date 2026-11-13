using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    readonly Dictionary<string, string> frpcDrafts = new(StringComparer.Ordinal);

    async Task<Control> FrpcPage(CancellationToken ct)
    {
        var clients = Choice(session.Frpc.Installed(), session.Store.Config.Settings.FrpcVersion);
        var profiles = Choice(session.FrpcProfiles.List());
        var editor = Input("", multiline: true);
        var profileRevision = 0;
        string? editedPath = null;
        bool reading = false;
        void EnsureStopped()
        {
            if (session.Frpc.Running)
                throw new IOException(Ui("Stop frpc before changing configurations."));
        }
        editor.TextChanged += (_, _) =>
        {
            if (!reading && editedPath != null)
                frpcDrafts[editedPath] = editor.Text ?? "";
        };
        clients.SelectionChanged += async (_, _) =>
        {
            if (clients.SelectedItem is string version)
            {
                session.Store.Config.Settings.FrpcVersion = version;
                await session.Save();
            }
        };
        editor.Tag = "frpc-editor";
        editor.MinHeight = 260;
        async Task ReadProfile()
        {
            if (profiles.SelectedItem is FrpcConfiguration profile)
            {
                var revision = ++profileRevision;
                var text = frpcDrafts.TryGetValue(profile.Path, out var draftText)
                    ? draftText
                    : await session.FrpcProfiles.Read(profile.Path);
                if (revision != profileRevision || ct.IsCancellationRequested)
                    return;
                reading = true;
                editedPath = profile.Path;
                editor.Text = text;
                reading = false;
                session.Store.Config.Settings.FrpcConfigPath = profile.Path;
                await session.Save();
            }
        }
        profiles.SelectionChanged += async (_, _) =>
        {
            try
            {
                await ReadProfile();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        var saved = session
            .FrpcProfiles.List()
            .FirstOrDefault(p => p.Path == session.Store.Config.Settings.FrpcConfigPath);
        if (saved != null)
            profiles.SelectedItem = saved;
        var state = T(session.Frpc.Running ? "Running" : "Stopped");
        var configs = Stack(
            Field("Downloaded client", clients),
            Field("Saved configuration", profiles),
            Field("Configuration content", editor),
            Row(
                Btn(
                    "Save",
                    async () =>
                    {
                        EnsureStopped();
                        if (profiles.SelectedItem is FrpcConfiguration profile)
                            await session.FrpcProfiles.Save(
                                profile.Path,
                                editor.Text ?? "",
                                default
                            );
                    }
                ),
                Btn(
                    "Verify",
                    async () =>
                    {
                        if (
                            profiles.SelectedItem is not FrpcConfiguration profile
                            || clients.SelectedItem is not string version
                        )
                            throw new IOException("Select a client and configuration.");
                        EnsureStopped();
                        await session.FrpcProfiles.Save(profile.Path, editor.Text ?? "", ct);
                        await session.Run(
                            "Verify frpc",
                            token => session.Frpc.Verify(version, profile.Path, token)
                        );
                    }
                ),
                Btn(
                    "Start frpc",
                    async () =>
                    {
                        if (
                            profiles.SelectedItem is not FrpcConfiguration profile
                            || clients.SelectedItem is not string version
                        )
                            throw new IOException("Select a client and configuration.");
                        EnsureStopped();
                        await session.FrpcProfiles.Save(profile.Path, editor.Text ?? "", ct);
                        await session.Run(
                            "Start frpc",
                            token => session.Frpc.Start(version, profile.Path, token)
                        );
                        state.Text = Ui(session.Frpc.Running ? "Running" : "Stopped");
                    },
                    true
                ),
                Btn(
                    "Stop frpc",
                    async () =>
                    {
                        await session.Frpc.Stop();
                        state.Text = Ui("Stopped");
                    }
                ),
                Btn(
                    "Delete configuration",
                    async () =>
                    {
                        EnsureStopped();
                        if (
                            profiles.SelectedItem is FrpcConfiguration profile
                            && await Confirm("Delete configuration?", profile.Name)
                        )
                        {
                            await Trash(session.FrpcProfiles.Owned(profile.Path));
                            await Navigate("frpc");
                        }
                    }
                )
            )
        );
        var name = Input();
        var format = Choice(new[] { "toml", "ini" });
        var draft = Input("", multiline: true);
        var identity = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,160"),
            ColumnSpacing = 12,
        };
        identity.Children.Add(Field("Configuration name", name));
        var formatField = Field("Format", format);
        Grid.SetColumn(formatField, 1);
        identity.Children.Add(formatField);
        var create = new Expander
        {
            Tag = "frpc-new-panel",
            Header = Ui("New configuration"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(
                identity,
                Field("Configuration content", draft),
                Row(
                    Btn(
                        "Clear content",
                        () =>
                        {
                            draft.Text = "";
                            return Task.CompletedTask;
                        }
                    ),
                    Btn(
                        "Copy current content",
                        () =>
                        {
                            draft.Text = editor.Text;
                            return Task.CompletedTask;
                        }
                    ),
                    Btn(
                        "Create configuration",
                        async () =>
                        {
                            EnsureStopped();
                            var profile = await session.FrpcProfiles.Create(
                                name.Text ?? "",
                                format.SelectedItem?.ToString() ?? "toml",
                                draft.Text ?? "",
                                default
                            );
                            session.Store.Config.Settings.FrpcConfigPath = profile.Path;
                            await session.Save();
                            await Navigate("frpc");
                        },
                        true
                    )
                )
            ),
        };
        var configurationActions = configs.Children.OfType<WrapPanel>().Single();
        configurationActions.Children.Insert(
            1,
            Btn(
                "Save as",
                () =>
                {
                    EnsureStopped();
                    draft.Text = editor.Text;
                    create.IsExpanded = true;
                    name.Focus();
                    return Task.CompletedTask;
                }
            )
        );
        var processActions = Row();
        foreach (
            var button in configurationActions
                .Children.OfType<Button>()
                .Where(button =>
                    button.Content?.ToString() == Ui("Start frpc")
                    || button.Content?.ToString() == Ui("Stop frpc")
                )
                .ToArray()
        )
        {
            configurationActions.Children.Remove(button);
            processActions.Children.Add(button);
        }
        var path = Input("", "File or directory path");
        async Task Import(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return;
            EnsureStopped();
            await session.Run(
                "Import configuration",
                async token =>
                {
                    var imported = await session.FrpcProfiles.Import(source, token);
                    if (imported.Count > 0)
                        session.Store.Config.Settings.FrpcConfigPath = imported[0].Path;
                    await session.Save();
                }
            );
            await Navigate("frpc");
        }
        var import = new Expander
        {
            Tag = "frpc-import-panel",
            Header = Ui("Import configuration"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(
                path,
                Row(
                    Btn("Add path", () => Import(path.Text)),
                    Btn(
                        "Choose files",
                        async () =>
                        {
                            EnsureStopped();
                            var files = await StorageProvider.OpenFilePickerAsync(
                                new()
                                {
                                    AllowMultiple = true,
                                    FileTypeFilter =
                                    [
                                        new FilePickerFileType("FRP configuration")
                                        {
                                            Patterns = ["*.toml", "*.ini"],
                                        },
                                    ],
                                }
                            );
                            if (files.Count == 0)
                                return;
                            await session.Run(
                                "Import configuration",
                                async token =>
                                {
                                    foreach (var file in files)
                                    {
                                        var source =
                                            file.TryGetLocalPath()
                                            ?? throw new IOException(
                                                "Choose a local configuration file."
                                            );
                                        var imported = await session.FrpcProfiles.Import(
                                            source,
                                            token
                                        );
                                        if (imported.Count > 0)
                                            session.Store.Config.Settings.FrpcConfigPath = imported[
                                                0
                                            ].Path;
                                    }
                                    await session.Save();
                                }
                            );
                            await Navigate("frpc");
                        }
                    ),
                    Btn("Choose folder", async () => await Import(await Folder()))
                )
            ),
        };
        var output = Input("", multiline: true);
        output.IsReadOnly = true;
        output.MinHeight = 180;
        output.MaxHeight = 360;
        output.Tag = "frpc-output";
        void UpdateOutput() =>
            output.Text = string.Join(
                Environment.NewLine,
                session
                    .Logs.Where(line => line.Contains("[FRPC]", StringComparison.Ordinal))
                    .TakeLast(300)
            );
        void OutputChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
            Dispatcher.UIThread.Post(UpdateOutput);
        void UpdateState() =>
            Dispatcher.UIThread.Post(() =>
            {
                var running = session.Frpc.Running;
                state.Text = Ui(running ? "Running" : "Stopped");
                editor.IsReadOnly = running;
                clients.IsEnabled = !running;
                profiles.IsEnabled = !running;
                create.IsEnabled = !running;
                import.IsEnabled = !running;
            });
        UpdateOutput();
        var body = Stack(
            Btn("Back to toolbox", () => Navigate("toolbox")),
            Btn("Manage downloaded clients", () => Navigate("settings")),
            Card(configs),
            Card(create),
            Card(import),
            processActions,
            state,
            T(
                "The client keeps running when switching pages and stops when the launcher closes.",
                12
            ),
            output
        );
        body.AttachedToVisualTree += (_, _) =>
        {
            session.Frpc.Changed += UpdateState;
            session.Logs.CollectionChanged += OutputChanged;
            UpdateState();
        };
        body.DetachedFromVisualTree += (_, _) =>
        {
            profileRevision++;
            session.Frpc.Changed -= UpdateState;
            session.Logs.CollectionChanged -= OutputChanged;
        };
        if (profiles.SelectedItem != null)
            await ReadProfile();
        return body;
    }

    Control FrpcClientSettings()
    {
        var ct = navigationRequest?.Token ?? CancellationToken.None;
        var installed = session.Frpc.Installed();
        var online = Choice(Array.Empty<string>());
        var clients = Choice(installed, session.Store.Config.Settings.FrpcVersion);
        var message = T("");
        var download = Btn(
            "Download online",
            async () =>
            {
                if (online.SelectedItem is not string version)
                    throw new IOException("Select an online version.");
                await session.Run(
                    "Download frpc",
                    async token =>
                    {
                        var release = await session.Frpc.FindRelease(version, token);
                        await session.Frpc.Install(release, token);
                        session.Store.Config.Settings.FrpcVersion = version;
                        await session.Save();
                    }
                );
                await Navigate("settings");
            },
            true
        );
        var remove = Btn(
            "Delete client",
            async () =>
            {
                if (
                    clients.SelectedItem is not string version
                    || !await Confirm("Delete client?", version)
                )
                    return;
                await session.Frpc.Remove(version);
                session.Store.Config.Settings.FrpcVersion =
                    session.Frpc.Installed().FirstOrDefault() ?? "";
                await session.Save();
                await Navigate("settings");
            }
        );
        remove.IsEnabled = installed.Length > 0 && !session.Frpc.Running;
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 10,
        };
        var onlineField = Field("Online version", online);
        var installedField = Field("Downloaded client", clients);
        fields.Children.Add(onlineField);
        Grid.SetColumn(installedField, 1);
        fields.Children.Add(installedField);
        Grid.SetRow(download, 1);
        fields.Children.Add(download);
        Grid.SetRow(remove, 1);
        Grid.SetColumn(remove, 1);
        fields.Children.Add(remove);
        var card = Card(Stack(T("frpc", 15, true), fields, message));
        card.Padding = new global::Avalonia.Thickness(12);
        var loading = false;
        online.DropDownOpened += async (_, _) =>
        {
            if (loading || online.ItemCount > 0)
                return;
            loading = true;
            try
            {
                var releases = await session.Frpc.Releases(1, ct);
                online.ItemsSource = releases.Versions;
                online.SelectedIndex = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                message.Text = ex.Message;
            }
            finally
            {
                loading = false;
            }
        };
        clients.SelectionChanged += async (_, _) =>
        {
            if (clients.SelectedItem is string version)
            {
                session.Store.Config.Settings.FrpcVersion = version;
                await session.Save();
            }
        };
        return card;
    }
}
