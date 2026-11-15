using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal async Task VerifyDataPackUi(string directory)
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
        foreach (var theme in new[] { "light", "dark" })
        {
            store.Config.Settings.Theme = theme;
            await SwitchLanguage("zh_cn");
            ApplyTheme();
            dataPackBasicDraft = new("", "", Namespace: "my_pack");
            dataPackEntryDraft = CMCLauncher
                .Core.Minecraft.DataPacks.DataPackEntryEditor.StarterEntries()
                .ToList();
            page = "datapack-generator";
            expandedNavigation = "more";
            await RenderAsync();
            T Find<T>(string tag)
                where T : FrameworkElement =>
                Descendants(PageBody).OfType<T>().SingleOrDefault(c => Equals(c.Tag, tag)) ?? throw new InvalidOperationException("Missing control: " + tag);
            void Click(string tag) =>
                (
                    (IInvokeProvider)
                        new ButtonAutomationPeer(Find<Button>(tag)).GetPattern(
                            PatternInterface.Invoke
                        )
                ).Invoke();
            Check(
                !Find<Button>("datapack-previous").IsEnabled
                    && Find<Button>("datapack-export").Visibility == Visibility.Collapsed,
                theme + ": wizard starts on basic information"
            );
            Click("datapack-next");
            Check(
                Descendants(PageBody)
                    .OfType<InfoBar>()
                    .Any(i => i.IsOpen && i.Severity == InfoBarSeverity.Error),
                theme + ": incomplete input produces an inline error"
            );
            Find<TextBox>("datapack-name").Text = "UI fixture";
            Find<ComboBox>("datapack-version").SelectedItem = "1.21.2";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-previous").IsEnabled
                    && Find<Button>("datapack-next").Visibility == Visibility.Visible,
                theme + ": template step is reachable"
            );
            await Task.Delay(100);
            await Capture(Path.Combine(directory, "datapack-" + theme + "-editor.png"));

            Find<TextBox>("datapack-entry-id").Text = "tools/custom";
            Find<TextBox>("datapack-field-result").Text = "minecraft:diamond";
            Find<TextBox>("datapack-field-count").Text = "2";
            Click("datapack-entry-duplicate");
            Check(
                Find<ComboBox>("datapack-entries").Items.Count == 6,
                theme + ": entry can be duplicated"
            );
            Click("datapack-entry-remove");
            Click("datapack-entry-add");
            Click("datapack-entry-remove");
            Find<ComboBox>("datapack-entries").SelectedIndex = 0;
            Find<CheckBox>("datapack-entry-raw").IsChecked = true;
            Check(
                Find<TextBox>("datapack-entry-source").Visibility == Visibility.Visible,
                theme + ": complete source editor expands"
            );
            Find<TextBox>("datapack-entry-source").Text = "{broken";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Collapsed,
                theme + ": invalid JSON cannot advance"
            );
            Find<TextBox>("datapack-entry-source").Text =
                "{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[\"minecraft:dirt\"],\"result\":{\"id\":\"minecraft:diamond\",\"count\":2}}";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Visible
                    && Find<ComboBox>("datapack-files").Items.Count == 8,
                theme + ": review contains all eight actual generated files"
            );
            await Capture(Path.Combine(directory, "datapack-" + theme + "-preview.png"));
            Click("datapack-previous");
            Click("datapack-previous");
            Check(
                Find<TextBox>("datapack-name").Text == "UI fixture"
                    && Find<Button>("datapack-previous").Visibility == Visibility.Collapsed,
                theme + ": going back preserves inputs"
            );
            Find<TextBox>("datapack-namespace").Text = "../bad";
            Click("datapack-next");
            Check(
                Find<Button>("datapack-export").Visibility == Visibility.Collapsed
                    && Descendants(PageBody).OfType<InfoBar>().Any(i => i.IsOpen),
                theme + ": invalid namespace cannot advance"
            );
            await Capture(Path.Combine(directory, "datapack-" + theme + "-validation.png"));
        }
    }
}
