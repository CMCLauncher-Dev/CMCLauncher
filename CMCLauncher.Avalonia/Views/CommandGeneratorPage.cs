using Avalonia.Controls;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control CommandGeneratorPage()
    {
        var version = Input(session.Selected?.GameVersion ?? "1.21.5");
        var command = new ComboBox
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        var fields = Stack();
        var syntax = T("");
        var error = T("");
        var output = Input();
        output.IsReadOnly = true;
        var copy = Btn(
            "Copy",
            async () =>
            {
                if (string.IsNullOrEmpty(output.Text))
                    return;
                if (Clipboard == null)
                    throw new IOException(Ui("Clipboard is unavailable."));
                await Clipboard.SetTextAsync(output.Text);
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
                        version.Text ?? "",
                        forms[command.SelectedIndex].Name,
                        fields
                            .Children.OfType<Panel>()
                            .SelectMany(panel => panel.Children.OfType<TextBox>())
                            .Select(box => box.Text ?? "")
                            .ToArray()
                    );
                }
                catch (ArgumentException ex)
                {
                    error.Text = Ui(ex.Message);
                }
            copy.IsEnabled = !string.IsNullOrEmpty(output.Text);
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
                    var box = Input(field.Example, field.Hint, multiline: field.Hint != null);
                    if (field.Hint != null)
                    {
                        box.MinHeight = 96;
                        box.MaxHeight = 240;
                        box.FontFamily = global::Avalonia.Media.FontFamily.Parse("monospace");
                    }
                    box.TextChanged += (_, _) => Preview();
                    fields.Children.Add(Field(field.Label, box));
                }
            Preview();
        }

        void RefreshVersion()
        {
            var previous = command.SelectedItem as string;
            try
            {
                forms = CommandGenerator.Forms(version.Text ?? "");
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
        return Stack(
            Btn("Back to toolbox", () => Navigate("toolbox")),
            Card(
                Stack(
                    Field("Minecraft version", version),
                    Field("Command", command),
                    syntax,
                    fields,
                    error,
                    Field("Generated command", output),
                    copy,
                    T(
                        "Use IDs that exist in the selected version. Commands require permission to execute."
                    )
                )
            )
        );
    }
}
