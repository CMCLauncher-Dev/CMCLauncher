using CMCLauncher.Core.Minecraft.DataPacks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    List<DataPackEntry> dataPackEntryDraft = DataPackEntryEditor.StarterEntries().ToList();

    sealed record DataPackKindChoice(DataPackEntryKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    (FrameworkElement View, Func<IReadOnlyList<DataPackEntry>> Read) BuildDataPackEntryEditor(
        Func<string> version,
        Func<string> packNamespace
    )
    {
        var entries = dataPackEntryDraft;
        var panel = new StackPanel { Spacing = 10, Tag = "datapack-entry-editor" };
        panel.Children.Add(
            Text(
                "Add as many entries as you need. Select an entry to edit it; changes are saved when switching entries or continuing to preview.",
                12
            )
        );
        var list = new ComboBox
        {
            Header = Ui("Content entries"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "datapack-entries",
        };
        panel.Children.Add(list);
        var editor = new StackPanel { Spacing = 10 };
        var id = new TextBox
        {
            Header = Ui("Entry ID"),
            MaxLength = 512,
            Tag = "datapack-entry-id",
        };
        var kind = new ComboBox
        {
            Header = Ui("Entry type"),
            ItemsSource = Enum.GetValues<DataPackEntryKind>()
                .Select(k => new DataPackKindChoice(k, Ui(DataPackEntryEditor.Label(k))))
                .ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "datapack-entry-kind",
        };
        var entryNamespace = new TextBox
        {
            Header = Ui("Entry namespace (optional)"),
            PlaceholderText = Ui("Leave empty to use the pack namespace"),
            MaxLength = 100,
            Tag = "datapack-entry-namespace",
        };
        editor.Children.Add(entryNamespace);
        editor.Children.Add(kind);
        editor.Children.Add(id);
        editor.Children.Add(
            Text(
                "Use a relative ID such as tools/hammer, without a file extension. Different entry types may share an ID.",
                12
            )
        );
        var fields = new StackPanel { Spacing = 8 };
        var guided = new ContentControl
        {
            Content = fields,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        editor.Children.Add(guided);
        var raw = new CheckBox { Content = Ui("Edit full source"), Tag = "datapack-entry-raw" };
        var source = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 220,
            MaxLength = 2 * 1024 * 1024,
            Tag = "datapack-entry-source",
        };
        editor.Children.Add(raw);
        editor.Children.Add(
            Text(
                "Full source replaces the guided output for this entry. JSON syntax is checked, but game-specific schemas and commands must be tested in Minecraft. Turning this off returns to the guided fields.",
                12
            )
        );
        editor.Children.Add(source);
        panel.Children.Add(editor);
        var status = new InfoBar { IsClosable = true, Tag = "datapack-entry-status" };
        panel.Children.Add(status);
        var getters = new Dictionary<string, Func<string>>();
        var selected = -1;
        var loading = false;
        void Error(Exception ex)
        {
            status.Message =
                Ui(ex.Message)
                + (
                    ex is DataPackJsonException json
                        ? " " + string.Format(Ui("Line {0}, column {1}"), json.Line, json.Column)
                        : ""
                );
            status.Severity = InfoBarSeverity.Error;
            status.IsOpen = true;
        }
        DataPackEntry Capture() =>
            new(
                ((DataPackKindChoice)kind.SelectedItem).Kind,
                id.Text,
                getters.ToDictionary(p => p.Key, p => p.Value())
            )
            {
                SourceOverride = raw.IsChecked == true ? source.Text : null,
                Namespace = entryNamespace.Text.Length == 0 ? null : entryNamespace.Text,
            };
        void ValidateEntries(IReadOnlyList<DataPackEntry> values) =>
            DataPackGenerator.Preview(
                new DataPackOptions("Editor", version(), Namespace: packNamespace())
                {
                    Entries = values,
                }
            );
        void Save()
        {
            if (selected < 0)
                return;
            var entry = Capture();
            var candidate = entries.ToArray();
            candidate[selected] = entry;
            ValidateEntries(candidate);
            entries[selected] = entry;
            status.IsOpen = false;
        }
        void Fields(DataPackEntry entry)
        {
            fields.Children.Clear();
            getters.Clear();
            foreach (var field in DataPackEntryEditor.Fields(entry.Kind))
            {
                var value = entry.Fields.TryGetValue(field.Key, out var stored)
                    ? stored
                    : field.DefaultValue;
                if (field.Choices is ["true", "false"])
                {
                    var input = new CheckBox
                    {
                        Content = Ui(field.Label),
                        IsChecked = value == "true",
                        Tag = "datapack-field-" + field.Key,
                    };
                    getters[field.Key] = () => input.IsChecked == true ? "true" : "false";
                    fields.Children.Add(input);
                }
                else if (field.Choices != null)
                {
                    var input = new ComboBox
                    {
                        Header = Ui(field.Label),
                        ItemsSource = field.Choices,
                        SelectedItem = value,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Tag = "datapack-field-" + field.Key,
                    };
                    getters[field.Key] = () => input.SelectedItem as string ?? "";
                    fields.Children.Add(input);
                }
                else
                {
                    var input = new TextBox
                    {
                        Header = Ui(field.Label),
                        Text = value,
                        AcceptsReturn = field.Multiline,
                        TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        MinHeight = field.Multiline ? 90 : 0,
                        MaxHeight = field.Multiline ? 200 : double.PositiveInfinity,
                        MaxLength = 2 * 1024 * 1024,
                        Tag = "datapack-field-" + field.Key,
                    };
                    getters[field.Key] = () => input.Text;
                    fields.Children.Add(input);
                }
                if (field.Help.Length > 0)
                    fields.Children.Add(Text(field.Help, 12));
            }
        }
        void Load(int index)
        {
            selected = index;
            loading = true;
            editor.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (index >= 0)
            {
                var entry = entries[index];
                id.Text = entry.Id;
                entryNamespace.Text = entry.Namespace ?? "";
                kind.SelectedItem = kind
                    .Items.Cast<DataPackKindChoice>()
                    .Single(c => c.Kind == entry.Kind);
                Fields(entry);
                raw.IsChecked = entry.SourceOverride != null;
                source.Text = entry.SourceOverride ?? "";
                guided.IsEnabled = raw.IsChecked != true;
                source.Visibility =
                    raw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
            loading = false;
        }
        void Refresh(int index)
        {
            loading = true;
            list.ItemsSource = entries
                .Select(
                    (e, i) => (i + 1) + ". " + e.Id + " · " + Ui(DataPackEntryEditor.Label(e.Kind))
                )
                .ToArray();
            list.SelectedIndex = index;
            loading = false;
            Load(index);
        }
        list.SelectionChanged += (_, _) =>
        {
            if (loading)
                return;
            var next = list.SelectedIndex;
            try
            {
                Save();
                Refresh(next);
            }
            catch (Exception ex)
            {
                loading = true;
                list.SelectedIndex = selected;
                loading = false;
                Error(ex);
            }
        };
        kind.SelectionChanged += (_, _) =>
        {
            if (loading || kind.SelectedItem is not DataPackKindChoice choice)
                return;
            Fields(DataPackEntryEditor.Create(choice.Kind, id.Text));
            raw.IsChecked = false;
            source.Text = "";
            guided.IsEnabled = true;
            source.Visibility = Visibility.Collapsed;
        };
        void RefreshRaw(object sender, RoutedEventArgs args)
        {
            if (loading)
                return;
            try
            {
                if (raw.IsChecked == true && source.Text.Length == 0)
                    source.Text = DataPackEntryEditor
                        .Build(Capture() with { SourceOverride = null }, version(), packNamespace())
                        .Value;
                guided.IsEnabled = raw.IsChecked != true;
                source.Visibility =
                    raw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                raw.IsChecked = false;
                Error(ex);
            }
        }
        raw.Checked += RefreshRaw;
        raw.Unchecked += RefreshRaw;
        void Execute(System.Action operation)
        {
            try
            {
                operation();
                status.IsOpen = false;
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var add = Action(
            "Add entry",
            (_, _) =>
                Execute(() =>
                {
                    Save();
                    var newId = "entry_" + (entries.Count + 1);
                    while (entries.Any(e => e.Id == newId))
                        newId += "_new";
                    entries.Add(
                        DataPackEntryEditor.Create(DataPackEntryKind.ShapelessRecipe, newId)
                    );
                    Refresh(entries.Count - 1);
                })
        );
        add.Tag = "datapack-entry-add";
        var duplicate = Action(
            "Duplicate entry",
            (_, _) =>
                Execute(() =>
                {
                    Save();
                    if (selected < 0)
                        return;
                    var entry = entries[selected];
                    var newId = entry.Id + "_copy";
                    while (entries.Any(e => e.Id == newId))
                        newId += "_copy";
                    entries.Add(entry with { Id = newId });
                    Refresh(entries.Count - 1);
                })
        );
        duplicate.Tag = "datapack-entry-duplicate";
        var remove = Action(
            "Remove entry",
            (_, _) =>
            {
                if (selected < 0)
                    return;
                entries.RemoveAt(selected);
                Refresh(Math.Min(selected, entries.Count - 1));
                status.IsOpen = false;
            }
        );
        remove.Tag = "datapack-entry-remove";
        actions.Children.Add(add);
        actions.Children.Add(duplicate);
        actions.Children.Add(remove);
        panel.Children.Insert(2, actions);
        panel.Children.Add(
            Action(
                "Apply entry changes",
                (_, _) =>
                    Execute(() =>
                    {
                        Save();
                        Refresh(selected);
                    })
            )
        );
        panel.Children.Add(
            Action(
                "Discard entry changes",
                (_, _) =>
                {
                    Load(selected);
                    status.IsOpen = false;
                }
            )
        );
        Refresh(0);
        return (
            panel,
            () =>
            {
                Save();
                ValidateEntries(entries);
                return entries.ToArray();
            }
        );
    }
}
