using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyDataPackUi()
    {
        var checks = new List<string>();
        try
        {
            session.Store.Config.Settings.LauncherLanguage = "zh_cn";
            foreach (var theme in new[] { "light", "dark" })
            {
                session.Store.Config.Settings.Theme = theme;
                ApplyTheme();
                await VerifyDataPackParity(checks);
                await Task.Delay(100);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                    new global::Avalonia.PixelSize((int)Width, (int)Height)
                );
                bitmap.Render(this);
                bitmap.Save(Path.Combine(VerificationDirectory!, "datapack-" + theme + ".png"));
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

    async Task VerifyDataPackParity(List<string> checks)
    {
        dataPackBasicDraft = new("", "", Namespace: "my_pack");
        dataPackEntryDraft = CMCLauncher
            .Core.Minecraft.DataPacks.DataPackEntryEditor.StarterEntries()
            .ToList();
        await Navigate("datapack-generator");
        T Find<T>(string tag)
            where T : Control =>
            pageContent.GetVisualDescendants().OfType<T>().Single(c => Equals(c.Tag, tag));
        void Click(string tag) =>
            Find<Button>(tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (
            Find<Button>("datapack-previous").IsEnabled || Find<Button>("datapack-export").IsVisible
        )
            throw new IOException("Data pack wizard does not start on basic information.");
        Click("datapack-next");
        if (Find<Button>("datapack-previous").IsEnabled)
            throw new IOException("Incomplete data pack details were accepted.");
        Find<TextBox>("datapack-name").Text = "UI fixture";
        Find<ComboBox>("datapack-version").SelectedItem = "1.21.2";
        Click("datapack-next");

        Find<TextBox>("datapack-entry-id").Text = "tools/custom";
        Find<TextBox>("datapack-field-result").Text = "minecraft:diamond";
        Find<TextBox>("datapack-field-count").Text = "2";
        Click("datapack-entry-duplicate");
        if (Find<ComboBox>("datapack-entries").ItemCount != 6)
            throw new IOException("Entry duplication failed.");
        Click("datapack-entry-remove");
        Click("datapack-entry-add");
        Click("datapack-entry-remove");
        Find<ComboBox>("datapack-entries").SelectedIndex = 0;
        Find<CheckBox>("datapack-entry-raw").IsChecked = true;
        if (!Find<TextBox>("datapack-entry-source").IsVisible)
            throw new IOException("Full source editor did not expand.");
        Find<TextBox>("datapack-entry-source").Text = "{broken";
        Click("datapack-next");
        if (Find<Button>("datapack-export").IsVisible)
            throw new IOException("Invalid source was accepted.");
        Find<TextBox>("datapack-entry-source").Text =
            "{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[\"minecraft:dirt\"],\"result\":{\"id\":\"minecraft:diamond\",\"count\":2}}";
        Click("datapack-next");
        if (
            !Find<Button>("datapack-export").IsVisible
            || Find<ComboBox>("datapack-files").ItemCount != 8
        )
            throw new IOException("Data pack preview does not match WinUI.");
        await Task.Delay(100);
        using (
            var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                new global::Avalonia.PixelSize((int)Width, (int)Height)
            )
        )
        {
            bitmap.Render(this);
            bitmap.Save(
                Path.Combine(
                    VerificationDirectory!,
                    "datapack-" + session.Store.Config.Settings.Theme + "-preview.png"
                )
            );
        }
        Click("datapack-previous");
        Click("datapack-previous");
        if (Find<TextBox>("datapack-name").Text != "UI fixture")
            throw new IOException("Data pack wizard lost its inputs.");
        Find<TextBox>("datapack-namespace").Text = "../bad";
        Click("datapack-next");
        if (Find<Button>("datapack-previous").IsEnabled)
            throw new IOException("Invalid data pack namespace was accepted.");
        checks.Add(
            "PASS data pack wizard validation, templates, preview and backwards navigation match WinUI"
        );
    }
}
