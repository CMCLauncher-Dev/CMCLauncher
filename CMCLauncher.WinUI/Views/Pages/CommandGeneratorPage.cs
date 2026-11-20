using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void CommandGeneratorPage()
    {
        Header("Command generator", "Build Minecraft Java Edition commands with a live preview.");
        var version = new TextBox
        {
            Header = Ui("Minecraft version"),
            Text = SelectedInstance?.GameVersion ?? "1.21.5",
        };
        var command = new BelowComboBox
        {
            Header = Ui("Command"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var fields = new StackPanel { Spacing = 8 };
        var syntax = Text("");
        var error = Text("");
        var output = new TextBox
        {
            Header = Ui("Generated command"),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
        };
        var copy = Action(
            "Copy",
            (_, _) =>
            {
                if (output.Text.Length == 0)
                    return;
                if (!DesktopClipboard.TrySetText(output.Text, out var failure))
                    throw new IOException(failure);
                ShowToast(Ui("Copied"));
            }
        );

        IReadOnlyList<CommandForm> forms = [];
        void Preview()
        {
            output.Text = "";
            error.Text = "";
            if (command.SelectedIndex >= 0 && command.SelectedIndex < forms.Count)
                try
                {
                    output.Text = CommandGenerator.Generate(
                        version.Text,
                        forms[command.SelectedIndex].Name,
                        fields.Children.OfType<TextBox>().Select(box => box.Text).ToArray()
                    );
                }
                catch (ArgumentException ex)
                {
                    error.Text = Ui(ex.Message);
                }
            copy.IsEnabled = output.Text.Length != 0;
        }

        void RebuildFields()
        {
            fields.Children.Clear();
            var form =
                command.SelectedIndex >= 0 && command.SelectedIndex < forms.Count
                    ? forms[command.SelectedIndex]
                    : null;
            syntax.Text = form?.Syntax ?? "";
            if (form != null)
                foreach (var field in form.Fields)
                {
                    var box = new TextBox { Header = Ui(field.Label), Text = field.Example };
                    if (field.Hint != null)
                    {
                        box.AcceptsReturn = true;
                        box.TextWrapping = TextWrapping.Wrap;
                        box.PlaceholderText = field.Hint;
                        box.MinHeight = 96;
                        box.MaxHeight = 240;
                        box.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                    }
                    box.TextChanged += (_, _) => Preview();
                    fields.Children.Add(box);
                }
            Preview();
        }

        void RefreshVersion()
        {
            var previous = command.SelectedItem as string;
            try
            {
                forms = CommandGenerator.Forms(version.Text);
                var names = forms.Select(form => Ui(form.Name)).ToArray();
                command.ItemsSource = names;
                command.SelectedItem = names.Contains(previous) ? previous : names[0];
                RebuildFields();
            }
            catch (ArgumentException)
            {
                forms = [CommandGenerator.CustomForm];
                command.ItemsSource = new[] { Ui(CommandGenerator.CustomCommand) };
                command.SelectedIndex = 0;
                RebuildFields();
            }
        }
        version.TextChanged += (_, _) => RefreshVersion();
        command.SelectionChanged += (_, _) => RebuildFields();
        RefreshVersion();
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        version,
                        command,
                        syntax,
                        fields,
                        error,
                        output,
                        copy,
                        Text(
                            "Use IDs that exist in the selected version. Commands require permission to execute."
                        ),
                    },
                }
            )
        );
    }
}
