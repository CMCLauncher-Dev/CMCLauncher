using CMCLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed class JavaSettingsPage : CMCPage
{
    public JavaSettingsPage(MainWindow window)
        : base(window) { }

    public override void Build()
    {
        Header(Ui("Java management"), "");
        var settings = Store.Config.Settings;
        var toolbar = Buttons(
            Action(
                Ui("Import Java"),
                async (_, _) =>
                {
                    var picker = new DesktopFileOpenPicker();
                    DesktopPicker.Initialize(picker, WindowNative.GetWindowHandle(Window));
                    picker.FileTypeFilter.Add(".exe");
                    var file = await picker.PickSingleFileAsync();
                    if (file == null)
                        return;
                    await Run(
                        async ct =>
                        {
                            var java = await JavaManager.Inspect(file.Path, ct);
                            JavaManager.Register(settings, java);
                            await Store.Save();
                        },
                        Ui("Import Java")
                    );
                    if (PageName == "java")
                        Render();
                }
            ),
            Action(Ui("Scan Java"), async (_, _) => await ScanJava(null)),
            Action(
                Ui("Scan folder"),
                async (_, _) =>
                {
                    var folder = await Task.FromResult(FolderPickerNative.PickFolder(Window));
                    if (folder != null)
                        await ScanJava(folder);
                }
            ),
            Action(
                Ui("Download Java"),
                async (_, _) =>
                {
                    var versions = new BelowComboBox
                    {
                        Header = Ui("Java version"),
                        ItemsSource = new[] { 8, 11, 17, 21, 25 },
                        SelectedItem = 21,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    var brands = new BelowComboBox
                    {
                        Header = Ui("Java brand"),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    void RefreshBrands()
                    {
                        var previous = (brands.SelectedItem as JavaManager.JavaBrand)?.Id;
                        var available = JavaManager
                            .DownloadBrands((int)versions.SelectedItem)
                            .ToArray();
                        brands.ItemsSource = available;
                        brands.SelectedItem =
                            available.FirstOrDefault(brand => brand.Id == previous) ?? available[0];
                    }
                    RefreshBrands();
                    versions.SelectionChanged += (_, _) =>
                    {
                        if (versions.SelectedItem is int)
                            RefreshBrands();
                    };
                    string? installParent = null;
                    var directory = new TextBlock
                    {
                        Text = Path.Combine(Store.Root, "runtimes"),
                        TextWrapping = TextWrapping.Wrap,
                    };
                    var chooseDirectory = Action(
                        Ui("Choose folder"),
                        (_, _) =>
                        {
                            var folder = FolderPickerNative.PickFolder(Window);
                            if (folder != null)
                            {
                                installParent = folder;
                                directory.Text = folder;
                            }
                        }
                    );
                    var dialog = new ContentDialog
                    {
                        XamlRoot = PageBody.XamlRoot,
                        RequestedTheme = LightTheme ? ElementTheme.Light : ElementTheme.Dark,
                        Title = Ui("Download Java"),
                        Content = new StackPanel
                        {
                            Spacing = 12,
                            Children = { versions, brands, chooseDirectory, directory },
                        },
                        PrimaryButtonText = Ui("Download Java"),
                        CloseButtonText = Ui("Cancel"),
                    };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                        return;
                    var major = (int)versions.SelectedItem;
                    var brand = ((JavaManager.JavaBrand)brands.SelectedItem).Id;
                    await Run(
                        async ct =>
                        {
                            var java = await JavaManager.Download(
                                Core,
                                Store,
                                major,
                                ct,
                                message =>
                                {
                                    if (Window.activeWork != null)
                                    {
                                        Window.activeWork.Stage = message;
                                        Window.activeWork.Progress = null;
                                        Window.RefreshTaskMetrics();
                                    }
                                    AddLog(message);
                                },
                                brand,
                                installParent
                            );
                            JavaManager.Register(settings, java);
                            await Store.Save();
                        },
                        UiFormat("Download Java {0}", major)
                    );
                    if (PageName == "java")
                        Render();
                },
                true
            ),
            Action(
                Ui("Use Java from PATH"),
                async (_, _) =>
                {
                    var found = await Task.Run(() =>
                        (Environment.GetEnvironmentVariable("PATH") ?? "")
                            .Split(';')
                            .Where(s => s.Length > 0)
                            .Select(s => Path.Combine(s.Trim().Trim('"'), "java.exe"))
                            .Where(File.Exists)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    );
                    if (found.Count == 0)
                        throw new Exception(Ui("Java not found in PATH."));
                    await Run(
                        async ct =>
                        {
                            foreach (var path in found)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    JavaManager.Register(
                                        settings,
                                        await JavaManager.Inspect(path, ct)
                                    );
                                }
                                catch (Exception ex)
                                {
                                    AddLog(UiFormat("Skipped {0}: {1}", path, ex.Message));
                                }
                            }
                            await Store.Save();
                        },
                        Ui("Import Java from PATH")
                    );
                    Render();
                }
            )
        );
        PageBody.Children.Add(toolbar);
        if (
            !string.IsNullOrWhiteSpace(settings.JavaPath)
            && !settings.JavaInstallations.Any(j =>
                string.Equals(j.Path, settings.JavaPath, StringComparison.OrdinalIgnoreCase)
            )
        )
            PageBody.Children.Add(
                Text(
                    UiFormat(
                        "Manual Java setting: {0} (import it for full details)",
                        settings.JavaPath
                    ),
                    12
                )
            );
        if (settings.JavaInstallations.Count == 0)
            PageBody.Children.Add(
                Text(Ui("No Java added yet. Import or scan local installations."), 13)
            );
        foreach (
            var java in settings
                .JavaInstallations.OrderByDescending(j => j.Major)
                .ThenBy(j => j.Vendor)
        )
        {
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(Text($"Java {java.Version} · {java.Vendor}", 14, true));
            panel.Children.Add(Text(java.Architecture + " · " + java.Path, 12));
            if (JavaManager.AffectedByJava4Shell(java))
            {
                var warning = Text(
                    "This Java version is affected by the Java4Shell vulnerability (CVE-2026-114938). Update to a fixed release or the launcher will disable its fast Base64 path automatically.",
                    12
                );
                warning.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    LightTheme
                        ? Windows.UI.Color.FromArgb(255, 139, 23, 31)
                        : Windows.UI.Color.FromArgb(255, 255, 180, 188)
                );
                warning.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                panel.Children.Add(
                    new Border
                    {
                        Padding = new Thickness(10, 6, 10, 6),
                        CornerRadius = new CornerRadius(4),
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            LightTheme
                                ? Windows.UI.Color.FromArgb(255, 255, 235, 237)
                                : Windows.UI.Color.FromArgb(255, 64, 24, 32)
                        ),
                        Child = warning,
                    }
                );
            }
            if (!File.Exists(java.Path))
                panel.Children.Add(
                    Text(Ui("File does not exist. Re-import or remove the entry."), 12)
                );
            var remove = Action(
                Ui("Remove from list"),
                async (sender, _) =>
                {
                    if (
                        !await Window.ConfirmPop(
                            (FrameworkElement)sender,
                            Ui("Remove Java entry?"),
                            java.Path,
                            Ui("Remove")
                        )
                    )
                        return;
                    JavaManager.Remove(settings, java);
                    await Store.Save();
                    Window.ShowToast(Ui("Java entry removed"));
                    Render();
                }
            );
            var update = Action(Ui("Update Java"), async (_, _) => await UpdateJava(java));
            var migrate = Action(
                Ui("Move Java"),
                async (_, _) =>
                {
                    Window.EnsureGameFilesIdle();
                    var parent = FolderPickerNative.PickFolder(Window);
                    if (parent == null)
                        return;
                    await Run(
                        async ct =>
                        {
                            Window.EnsureGameFilesIdle();
                            var removedSource = await JavaManager.MigrateJava(
                                Store,
                                java,
                                parent,
                                ct
                            );
                            Window.ShowToast(
                                Ui(
                                    removedSource
                                        ? "Java moved successfully."
                                        : "Switched to the new Java folder. Original files were kept."
                                )
                            );
                        },
                        Ui("Move Java")
                    );
                    if (PageName == "java")
                        Render();
                }
            );
            var delete = Action(Ui("Delete Java folder"), async (_, _) => await DeleteJava(java));
            SetJavaActionIcon(update, "\uE72C");
            SetJavaActionIcon(migrate, "\uEA2A");
            SetJavaActionIcon(remove, "\uE738");
            SetJavaActionIcon(delete, "\uE74D");
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { update, migrate, remove, delete },
            };
            var row = new Grid
            {
                ColumnSpacing = 12,
                ColumnDefinitions =
                {
                    new(),
                    new() { Width = GridLength.Auto },
                },
                Children = { panel, actions },
            };
            Grid.SetColumn(actions, 1);
            PageBody.Children.Add(CompactCard(row));
        }
    }

    static void SetJavaActionIcon(Button button, string glyph)
    {
        var label = button.Content?.ToString() ?? "";
        button.Content = new Viewbox
        {
            Width = 16,
            Height = 16,
            Child = new FontIcon { Glyph = glyph },
        };
        button.MinWidth = 28;
        button.MinHeight = 28;
        button.Padding = new Thickness(6);
        button.BorderThickness = new Thickness(0);
        button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.Transparent
        );
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
    }

    async Task DeleteJava(JavaInstallation java)
    {
        Window.EnsureGameFilesIdle();
        var directory = JavaManager.JavaDirectoryForDeletion(Store, java);
        var dialog = new ContentDialog
        {
            XamlRoot = PageBody.XamlRoot,
            RequestedTheme = LightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Delete Java folder"),
            Content = Text(
                Ui(
                    "Move this Java installation to the Recycle Bin? Other apps using it may stop working."
                )
                    + Environment.NewLine
                    + directory,
                13
            ),
            PrimaryButtonText = Ui("Delete Java folder"),
            CloseButtonText = Ui("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        await Run(
            async ct =>
            {
                Window.EnsureGameFilesIdle();
                if (
                    !string.Equals(
                        directory,
                        JavaManager.JavaDirectoryForDeletion(Store, java),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new IOException("The Java installation folder changed.");
                ct.ThrowIfCancellationRequested();
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    directory,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                );
                if (Directory.Exists(directory))
                    throw new IOException("The Java installation folder was not deleted.");
                JavaManager.Remove(Store.Config.Settings, java);
                await Store.Save();
                Window.ShowToast(Ui("Java folder deleted."));
            },
            Ui("Delete Java folder")
        );
        if (PageName == "java")
            Render();
    }

    async Task UpdateJava(JavaInstallation selected)
    {
        if (JavaManager.UpdateBrand(Store, selected) == null)
        {
            Window.ShowToast(
                Ui(
                    "Automatic updates are unavailable for this Java brand, version or architecture."
                )
            );
            return;
        }
        if (JavaManager.ManagedBrand(Store, selected) == null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = PageBody.XamlRoot,
                RequestedTheme = LightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui("Update Java"),
                Content = Text(
                    "CMCLauncher will install the update separately and switch its Java entry. The imported files will be kept.",
                    13
                ),
                PrimaryButtonText = Ui("Update Java"),
                CloseButtonText = Ui("Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }
        await Run(
            async ct =>
            {
                Window.EnsureGameFilesIdle();
                var updated = await JavaManager.UpdateJava(Core, Store, selected, ct, AddLog);
                Window.ShowToast(Ui(updated ? "Java updated" : "No newer Java release found"));
            },
            Ui("Update Java")
        );
        if (PageName == "java")
            Render();
    }

    async Task ScanJava(string? folder)
    {
        var settings = Store.Config.Settings;
        int added = 0;
        await Run(
            async ct =>
            {
                var candidates = await Task.Run(
                    () => JavaManager.Candidates(Store, folder, ct).ToList(),
                    ct
                );
                for (int index = 0; index < candidates.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = candidates[index];
                    if (Window.activeWork != null)
                    {
                        Window.activeWork.Stage = UiFormat(
                            "Identifying Java {0}/{1}",
                            index + 1,
                            candidates.Count
                        );
                        Window.activeWork.Progress = 100.0 * index / candidates.Count;
                        Window.RefreshTaskMetrics();
                    }
                    if (settings.IgnoredJavaPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        var java = await JavaManager.Inspect(path, ct);
                        JavaManager.Register(settings, java);
                        added++;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddLog(UiFormat("Java scan skipped {0}: {1}", path, ex.Message));
                    }
                }
                await Store.Save();
                AddLog(UiFormat("Scan finished, detected {0} Java installation(s).", added));
            },
            Ui("Scan Java")
        );
        if (PageName == "java")
            Render();
    }
}
