using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyJsonLocalization(Action<bool, string> check)
    {
        var savedPage = page;
        var savedLanguage = store.Config.Settings.LauncherLanguage;
        var directory = Path.Combine(Program.UserDirectory, "locales");
        // 自带语言包在 Default/；验证时把修改写到外层同名文件，模拟用户修改。
        var english = Path.Combine(directory, "en_us.json");
        var french = Path.Combine(directory, "fr_fr.json");
        var bundled = await File.ReadAllBytesAsync(
            Path.Combine(BundledResources.DefaultDirectory(directory), "en_us.json")
        );
        var original = File.Exists(english) ? await File.ReadAllBytesAsync(english) : null;
        var originalFrench = File.Exists(french) ? await File.ReadAllBytesAsync(french) : null;
        async Task RestoreEnglish()
        {
            if (original == null)
                File.Delete(english);
            else
                await File.WriteAllBytesAsync(english, original);
        }
        try
        {
            var json = JsonNode.Parse(bundled)!;
            json["strings"]!["Logs"] = "Runtime logs from JSON";
            await File.WriteAllTextAsync(english, json.ToJsonString());
            page = "logs";
            await SwitchLanguage("en_us");
            await RenderAsync();
            check(
                PageTitle.Text == "Runtime logs from JSON",
                "editing external JSON changes visible UI text after language reload without recompilation"
            );
            await File.WriteAllTextAsync(english, "{invalid");
            await SwitchLanguage("en_us");
            await RenderAsync();
            check(
                PageTitle.Text == "Logs",
                "invalid external JSON falls back to bundled translations in the actual window"
            );
            await RestoreEnglish();
            await File.WriteAllTextAsync(
                french,
                """{"code":"fr_fr","displayName":"Français","strings":{"Launcher settings":"Paramètres JSON","Launcher language":"Langue"}}"""
            );
            page = "settings";
            await SwitchLanguage("fr_fr");
            await RenderAsync();
            check(
                PageTitle.Text == "Paramètres JSON",
                "additional JSON language translates the settings page"
            );
            var selector = Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Header?.ToString() == "Langue");
            check(
                selector.Items.OfType<InterfaceLanguage>().Any(l => l.Code == "fr_fr")
                    && (selector.SelectedItem as InterfaceLanguage)?.Code == "fr_fr",
                "additional language is selectable and remains selected by code"
            );
        }
        finally
        {
            await RestoreEnglish();
            if (originalFrench == null)
                File.Delete(french);
            else
                await File.WriteAllBytesAsync(french, originalFrench);
            page = savedPage;
            await SwitchLanguage(savedLanguage);
            await RenderAsync();
        }
    }
}
