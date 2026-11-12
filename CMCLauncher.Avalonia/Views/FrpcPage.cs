using Avalonia.Controls;
using Avalonia.Layout;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task<Control> FrpcPage(CancellationToken ct)
    {
        var clients = Choice(session.Frpc.Installed(), session.Store.Config.Settings.FrpcVersion);
        var profiles = Choice(session.FrpcProfiles.List());
        var editor = Input("", multiline: true);
        editor.MinHeight = 260;
        async Task ReadProfile()
        {
            if (profiles.SelectedItem is FrpcConfiguration profile)
            {
                editor.Text = await session.FrpcProfiles.Read(profile.Path);
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
                        await session.FrpcProfiles.Save(profile.Path, editor.Text ?? "", ct);
                        await session.Run(
                            "Start frpc",
                            token => session.Frpc.Start(version, profile.Path, token)
                        );
                        state.Text = Ui("Running");
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
            ),
            state
        );
        var name = Input();
        var format = Choice(new[] { "toml", "ini" });
        var draft = Input("", multiline: true);
        var create = new Expander
        {
            Header = Ui("New configuration"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(
                Field("Configuration name", name),
                Field("Format", format),
                Field("Configuration content", draft),
                Row(
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
        var path = Input("", "File or directory path");
        async Task Import(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return;
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
            Header = Ui("Import configuration"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(
                path,
                Row(
                    Btn("Add path", () => Import(path.Text)),
                    Btn("Choose file", async () => await Import(await OpenFile("*.toml", "*.ini"))),
                    Btn("Choose folder", async () => await Import(await Folder()))
                )
            ),
        };
        var output = new ListBox { ItemsSource = session.Logs, MaxHeight = 260 };
        var body = Stack(
            Btn("Back to toolbox", () => Navigate("toolbox")),
            Btn("Manage downloaded clients", () => Navigate("settings")),
            Card(configs),
            Card(create),
            Card(import),
            Card(
                new Expander
                {
                    Header = Ui("Runtime logs"),
                    Content = output,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                }
            )
        );
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
        var releasePage = 0;
        async Task Load(int page)
        {
            var releases = await session.Frpc.Releases(page, ct);
            var previous = page == 1 ? Array.Empty<string>() : online.ItemsSource!.Cast<string>();
            var values = previous.Concat(releases.Versions).Distinct().ToArray();
            online.ItemsSource = values;
            online.SelectedIndex = 0;
            releasePage = page;
            message.Text = $"{values.Length} " + Ui("versions loaded");
        }
        var downloads = new Expander
        {
            Header = Ui("Download frpc"),
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(
                Field("Online version", online),
                Field("Downloaded client", clients),
                Row(
                    Btn(
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
                    ),
                    Btn("Refresh releases", () => Load(1)),
                    Btn("Load more releases", () => Load(releasePage + 1)),
                    Btn(
                        "Delete client",
                        async () =>
                        {
                            if (
                                clients.SelectedItem is string version
                                && await Confirm("Delete client?", version)
                            )
                            {
                                await session.Frpc.Remove(version);
                                await Navigate("settings");
                            }
                        }
                    )
                ),
                message
            ),
        };
        clients.SelectionChanged += async (_, _) =>
        {
            if (clients.SelectedItem is string version)
            {
                session.Store.Config.Settings.FrpcVersion = version;
                await session.Save();
            }
        };
        downloads.PropertyChanged += async (_, args) =>
        {
            if (
                args.Property != Expander.IsExpandedProperty
                || !downloads.IsExpanded
                || releasePage != 0
            )
                return;
            try
            {
                await Load(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                message.Text = ex.Message;
            }
        };
        return Card(downloads);
    }
}
