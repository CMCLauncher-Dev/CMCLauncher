using Avalonia.Controls;
using CMCLauncher.Core.Minecraft.DataPacks;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    List<DataPackEntry> dataPackEntryDraft = DataPackEntryEditor.StarterEntries().ToList();

    sealed record DataPackKindChoice(DataPackEntryKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    (Control View, Func<IReadOnlyList<DataPackEntry>> Read) BuildDataPackEntryEditor(
        Func<string> version,
        Func<string> packNamespace
    )
    {
        var entries = dataPackEntryDraft;
        var list = new ComboBox
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            Tag = "datapack-entries",
        };
        var panel = Stack(
            T(
                "Add as many entries as you need. Select an entry to edit it; changes are saved when switching entries or continuing to preview.",
                12
            ),
            Field("Content entries", list)
        );
        panel.Tag = "datapack-entry-editor";
        var id = Input();
        id.MaxLength = 512;
        id.Tag = "datapack-entry-id";
        var choices = Enum.GetValues<DataPackEntryKind>()
            .Select(k => new DataPackKindChoice(k, Ui(DataPackEntryEditor.Label(k))))
            .ToArray();
        var kind = new ComboBox
        {
            ItemsSource = choices,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            Tag = "datapack-entry-kind",
        };
        var fields = Stack();
        var raw = new CheckBox { Content = Ui("Edit full source"), Tag = "datapack-entry-raw" };
        var source = Input("", null, true);
        source.Height = 220;
        source.MaxLength = 2 * 1024 * 1024;
        source.Tag = "datapack-entry-source";
        var entryNamespace = Input("", "Leave empty to use the pack namespace");
        entryNamespace.MaxLength = 100;
        entryNamespace.Tag = "datapack-entry-namespace";
        var editor = Stack(
            Field("Entry namespace (optional)", entryNamespace),
            Field("Entry type", kind),
            Field("Entry ID", id),
            T(
                "Use a relative ID such as tools/hammer, without a file extension. Different entry types may share an ID.",
                12
            ),
            fields,
            raw,
            T(
                "Full source replaces the guided output for this entry. JSON syntax is checked, but game-specific schemas and commands must be tested in Minecraft. Turning this off returns to the guided fields.",
                12
            ),
            source
        );
        panel.Children.Add(editor);
        var status = T("");
        status.IsVisible = false;
        status.Tag = "datapack-entry-status";
        panel.Children.Add(status);
        var getters = new Dictionary<string, Func<string>>();
        var selected = -1;
        var loading = false;
        void Error(Exception ex)
        {
            status.Text =
                Ui(ex.Message)
                + (
                    ex is DataPackJsonException json
                        ? " " + string.Format(Ui("Line {0}, column {1}"), json.Line, json.Column)
                        : ""
                );
            status.IsVisible = true;
        }
        DataPackEntry Capture() =>
            new(
                ((DataPackKindChoice)kind.SelectedItem!).Kind,
                id.Text ?? "",
                getters.ToDictionary(p => p.Key, p => p.Value())
            )
            {
                SourceOverride = raw.IsChecked == true ? source.Text ?? "" : null,
                Namespace = string.IsNullOrEmpty(entryNamespace.Text) ? null : entryNamespace.Text,
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
            status.IsVisible = false;
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
                        ItemsSource = field.Choices,
                        SelectedItem = value,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                        Tag = "datapack-field-" + field.Key,
                    };
                    getters[field.Key] = () => input.SelectedItem as string ?? "";
                    fields.Children.Add(Field(field.Label, input));
                }
                else
                {
                    var input = Input(value, null, field.Multiline);
                    input.MinHeight = field.Multiline ? 90 : 38;
                    input.MaxHeight = field.Multiline ? 200 : double.PositiveInfinity;
                    input.MaxLength = 2 * 1024 * 1024;
                    input.Tag = "datapack-field-" + field.Key;
                    getters[field.Key] = () => input.Text ?? "";
                    fields.Children.Add(Field(field.Label, input));
                }
                if (field.Help.Length > 0)
                    fields.Children.Add(T(field.Help, 12));
            }
        }
        void Load(int index)
        {
            selected = index;
            loading = true;
            editor.IsVisible = index >= 0;
            if (index >= 0)
            {
                var entry = entries[index];
                id.Text = entry.Id;
                entryNamespace.Text = entry.Namespace ?? "";
                kind.SelectedItem = choices.Single(c => c.Kind == entry.Kind);
                Fields(entry);
                raw.IsChecked = entry.SourceOverride != null;
                source.Text = entry.SourceOverride ?? "";
                fields.IsEnabled = raw.IsChecked != true;
                source.IsVisible = raw.IsChecked == true;
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
            Fields(DataPackEntryEditor.Create(choice.Kind, id.Text ?? ""));
            raw.IsChecked = false;
            source.Text = "";
            fields.IsEnabled = true;
            source.IsVisible = false;
        };
        raw.IsCheckedChanged += (_, _) =>
        {
            if (loading)
                return;
            try
            {
                if (raw.IsChecked == true && string.IsNullOrEmpty(source.Text))
                    source.Text = DataPackEntryEditor
                        .Build(Capture() with { SourceOverride = null }, version(), packNamespace())
                        .Value;
                fields.IsEnabled = raw.IsChecked != true;
                source.IsVisible = raw.IsChecked == true;
            }
            catch (Exception ex)
            {
                raw.IsChecked = false;
                Error(ex);
            }
        };
        Task Execute(System.Action operation)
        {
            try
            {
                operation();
                status.IsVisible = false;
            }
            catch (Exception ex)
            {
                Error(ex);
            }
            return Task.CompletedTask;
        }
        var add = Btn(
            "Add entry",
            () =>
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
        var duplicate = Btn(
            "Duplicate entry",
            () =>
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
        var remove = Btn(
            "Remove entry",
            () =>
                Execute(() =>
                {
                    if (selected < 0)
                        return;
                    entries.RemoveAt(selected);
                    Refresh(Math.Min(selected, entries.Count - 1));
                })
        );
        remove.Tag = "datapack-entry-remove";
        panel.Children.Insert(2, Row(add, duplicate, remove));
        panel.Children.Add(
            Btn(
                "Apply entry changes",
                () =>
                    Execute(() =>
                    {
                        Save();
                        Refresh(selected);
                    })
            )
        );
        panel.Children.Add(Btn("Discard entry changes", () => Execute(() => Load(selected))));
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
