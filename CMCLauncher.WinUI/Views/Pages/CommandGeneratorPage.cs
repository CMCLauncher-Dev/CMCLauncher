using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    void CommandGeneratorPage()
    {
        var consoleFormat = new CheckBox
        {
            Content = Ui("Server console format (no leading slash)"),
        };
        Header("Command generator", "Build Minecraft Java Edition commands with a live preview.");
        var version = "";
        var versionPicker = new BelowComboBox
        {
            Header = Ui("Minecraft version"),
            PlaceholderText = Ui("Select a Minecraft version"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var snapshots = new CheckBox { Content = Ui("Show snapshots") };
        var april = new CheckBox { Content = Ui("Show April Fools") };
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        filters.Children.Add(snapshots);
        filters.Children.Add(april);
        var installedVersions = store.Config.Instances.Select(instance => instance.GameVersion).ToArray();
        IReadOnlyList<GameRelease> catalog = [];
        Action? versionChanged = null;
        void ChooseVersion(string? selected)
        {
            var value = selected?.Trim() ?? "";
            if (string.Equals(version, value, StringComparison.Ordinal))
                return;
            version = value;
            versionChanged?.Invoke();
        }

        void RefreshVersionChoices()
        {
            var selected = versionPicker.SelectedItem as string;
            versionPicker.ItemsSource = CommandGenerator.VersionChoices(
                catalog, installedVersions, snapshots.IsChecked == true, april.IsChecked == true
            );
            versionPicker.SelectedItem = selected;
            if (versionPicker.SelectedIndex < 0)
                ChooseVersion(null);
        }

        versionPicker.DropDownOpened += (_, _) => versionPicker.FocusUnselectedItem("1.21.1");
        versionPicker.SelectionChanged += (_, _) =>
        {
            if (versionPicker.SelectedItem is string selected)
            {
                ChooseVersion(selected);
            }
        };
        snapshots.Checked += (_, _) => RefreshVersionChoices();
        snapshots.Unchecked += (_, _) => RefreshVersionChoices();
        april.Checked += (_, _) => RefreshVersionChoices();
        april.Unchecked += (_, _) => RefreshVersionChoices();
        RefreshVersionChoices();
        versionPicker.Loaded += async (_, _) =>
        {
            try
            {
                catalog = await core.VersionCatalog();
                RefreshVersionChoices();
            }
            catch (Exception ex)
                when (ex is IOException or HttpRequestException or OperationCanceledException
                    or System.Text.Json.JsonException)
            {
                RuntimeLog.Write("WARN", ex.Message);
            }
        };
        var command = new BelowComboBox
        {
            Header = Ui("Command"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var commandCategory = new BelowComboBox
        {
            MinWidth = 145,
            Header = Ui("Command category"),
            ItemsSource = CommandTree.Categories.Select(Ui).ToArray(),
            SelectedIndex = 0,
        };
        var fields = new StackPanel { Spacing = 8 };
        var advancedFields = new StackPanel { Spacing = 8 };
        var editors = new List<TextBox>();
        var itemTags = new List<ItemTag>();
        var basicCard = Card(
            new StackPanel
            {
                Spacing = 8,
                Children = { Text("Basic command settings", 16, true), fields },
            }
        );
        var advancedCard = Card(
            new StackPanel
            {
                Spacing = 8,
                Children = { Text("Advanced command settings", 16, true), advancedFields },
            }
        );
        basicCard.VerticalAlignment = advancedCard.VerticalAlignment = VerticalAlignment.Top;
        var editorLayout = new Grid
        {
            ColumnDefinitions = { new(), new() },
            RowDefinitions =
            {
                new() { Height = GridLength.Auto },
                new() { Height = GridLength.Auto },
            },
            RowSpacing = 12,
            Children = { basicCard, advancedCard },
        };
        void ArrangeEditors()
        {
            var shown = fields.Children.Count != 0 || advancedFields.Children.Count != 0;
            editorLayout.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            var hasAdvanced = advancedFields.Children.Count > 0;
            var sideBySide = hasAdvanced && editorLayout.ActualWidth >= 700;
            advancedCard.Visibility = hasAdvanced ? Visibility.Visible : Visibility.Collapsed;
            editorLayout.ColumnSpacing = sideBySide ? 12 : 0;
            editorLayout.ColumnDefinitions[1].Width = sideBySide
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            Grid.SetColumn(advancedCard, sideBySide ? 1 : 0);
            Grid.SetRow(advancedCard, sideBySide ? 0 : 1);
        }
        editorLayout.SizeChanged += (_, _) => ArrangeEditors();
        var syntax = Text("");
        var error = Text("");
        void ShowError(string message)
        {
            error.Text = message;
            var shown = message.Length != 0;
            error.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowError("");
        var catalogOutput = "";
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
                if (!DesktopClipboard.TrySetText(WinRT.Interop.WindowNative.GetWindowHandle(this), output.Text, out var failure))
                    throw new IOException(failure);
                ShowToast(Ui("Copied"));
            }
        );

        IReadOnlyList<CommandForm> availableForms = [];
        IReadOnlyList<CommandForm> forms = [];
        CommandTree? commandTree = null;
        Action? selectedCommandChanged = null;
        string? SelectedCommand() => command.SelectedIndex >= 0 && command.SelectedIndex < forms.Count
            ? forms[command.SelectedIndex].Name
            : null;
        bool HasBuiltInForm(string name) => availableForms.Any(form => form.Name == name);
        void Preview()
        {
            output.Text = "";
            ShowError("");
            copy.IsEnabled = false;
            if (string.IsNullOrWhiteSpace(version))
                return;
            if (SelectedCommand() is { } name && HasBuiltInForm(name))
                try
                {
                    var inputs = editors.Select(box => box.Text ?? "").ToArray();
                    if (
                        name == "give"
                        && inputs.Length > 5
                        && itemTags.Count != 0
                    )
                    {
                        inputs[5] = CommandGenerator.BuildItemTags(
                            version,
                            inputs[3],
                            itemTags
                        );
                        inputs[3] = "";
                    }
                    output.Text = CommandGenerator.Generate(
                        version,
                        name,
                        inputs
                    );
                }
                catch (ArgumentException ex)
                {
                    ShowError(Ui(ex.Message));
                }
            else if (SelectedCommand() is { } catalogCommand && !HasBuiltInForm(catalogCommand))
                output.Text = catalogOutput;
            if (
                consoleFormat.IsChecked == true
                && output.Text is { Length: > 0 } generated
                && generated[0] == '/'
            )
                output.Text = generated[1..];
            copy.IsEnabled = output.Text.Length != 0;
        }

        void RebuildFields()
        {
            fields.Children.Clear();
            advancedFields.Children.Clear();
            editors.Clear();
            itemTags.Clear();
            var form = availableForms.FirstOrDefault(form => form.Name == SelectedCommand());
            syntax.Text = form?.Syntax ?? "";
            syntax.Visibility = string.IsNullOrEmpty(syntax.Text) ? Visibility.Collapsed : Visibility.Visible;
            void UpdateDependentFields()
            {
                if (form == null || editors.Count == 0)
                    return;
                for (var index = 0; index < editors.Count; index++)
                {
                    var allowed = form.Fields[index].EnabledForFirstField;
                    if (allowed == null)
                        continue;
                    var input = editors[index];
                    input.IsEnabled = allowed.Contains(editors[0].Text ?? "", StringComparer.Ordinal);
                    if (!input.IsEnabled)
                        input.Text = "";
                }
            }
            if (form != null)
            {
                foreach (var field in form.Fields)
                {
                    if (form.Name == "give" && editors.Count is 4 or 5)
                    {
                        editors.Add(new TextBox());
                        continue;
                    }
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
                    editors.Add(box);
                    var destination =
                        field.Hint != null || field.Label == "Custom item data (JSON, optional)"
                            ? advancedFields
                            : fields;
                    var registry = CommandValueCatalog.RegistryForField(field.Label);
                    var options = CommandValueCatalog.OptionsForField(field.Label);
                    if (field.Label is "Coordinates" or "Start coordinates" or "End coordinates")
                        destination.Children.Add(CoordinateEditor(
                            field.Label, field.Example, false, value =>
                            {
                                box.Text = value;
                                Preview();
                            }
                        ));
                    else if (options.Count != 0)
                        destination.Children.Add(CommandOptionPicker(
                            field.Label, options, box.Text ?? "", field.Optional, value =>
                            {
                                box.Text = value;
                                UpdateDependentFields();
                                Preview();
                            }
                        ));
                    else if (registry == null)
                    {
                        if ((form.Name is "give" or "effect" or "tp" && field.Label == "Target")
                            || field.Label == "Destination player")
                        {
                            var selectors = CommandValueCatalog.TargetSelectors(
                                "minecraft:entity", version,
                                singleTarget: field.Label == "Destination player",
                                playersOnly: form.Name == "give"
                            );
                            destination.Children.Add(TargetSelectorPicker(box, selectors));
                        }
                        destination.Children.Add(box);
                    }
                    else
                        destination.Children.Add(CommandValueButton(
                            version, field.Label, registry, box.Text ?? "", value =>
                            {
                                box.Text = value;
                                Preview();
                            }
                        ));
                }
                if (form.Name == "give" && editors.Count > 5)
                    advancedFields.Children.Add(
                        ItemTagsPanel(
                            version,
                            itemTags,
                            CommandGenerator.UsesItemComponents(version),
                            Preview,
                            itemEditor: true
                        )
                    );
            }
            UpdateDependentFields();
            ArrangeEditors();
            Preview();
        }


        var updatingCommandChoices = false;
        void FilterCommands(bool versionChanged = false)
        {
            var previous = SelectedCommand();
            var catalogForms = commandTree?.Choices([])
                .Where(branch => !branch.Argument && !HasBuiltInForm(branch.Name))
                .Select(branch => new CommandForm(branch.Name, "", [])) ?? [];
            var filtered = availableForms.Concat(catalogForms)
                .Where(form => CommandTree.MatchesCommand(
                    form.Name, Ui(form.Name), commandCategory.SelectedIndex, null
                ))
                .OrderBy(form => form.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            forms = filtered;
            var match = Array.FindIndex(filtered, form => form.Name == previous);
            var selectedIndex = match >= 0 ? match : filtered.Length > 0 ? 0 : -1;
            updatingCommandChoices = true;
            try
            {
                command.ItemsSource = forms.Select(form => CommandGenerator.CommandLabel(form.Name, Ui)).ToArray();
                command.IsEnabled = forms.Count > 0;
                command.SelectedIndex = selectedIndex;
            }
            finally
            {
                updatingCommandChoices = false;
            }
            if (versionChanged || match < 0)
            {
                RebuildFields();
                selectedCommandChanged?.Invoke();
            }
            else
                Preview();
            if (forms.Count == 0)
                ShowError(Ui(version.Length == 0 ? "Select a Minecraft version" : "No matching commands"));
        }

        void RefreshVersion()
        {
            try
            {
                availableForms = version.Length == 0 ? [] : CommandGenerator.Forms(version);
            }
            catch (ArgumentException)
            {
                availableForms = [CommandGenerator.CustomForm];
            }
            commandCategory.IsEnabled = version.Length != 0;
            FilterCommands(versionChanged: true);
        }
        commandCategory.SelectionChanged += (_, _) => FilterCommands();
        versionChanged += RefreshVersion;
        consoleFormat.Checked += (_, _) => Preview();
        consoleFormat.Unchecked += (_, _) => Preview();
        command.SelectionChanged += (_, _) =>
        {
            if (!updatingCommandChoices)
            {
                RebuildFields();
                selectedCommandChanged?.Invoke();
            }
        };
        RefreshVersion();
        var toolbar = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new(), new() { Width = GridLength.Auto }, new() },
            Children = { versionPicker, commandCategory, command },
        };
        Grid.SetColumn(commandCategory, 1);
        Grid.SetColumn(command, 2);
        var catalogStatus = Text("");
        var catalogEditor = CommandCatalogEditor(
            () => version,
            () => SelectedCommand() is { } name && !HasBuiltInForm(name) ? name : null,
            callback => versionChanged += callback,
            callback => selectedCommandChanged += callback,
            loaded =>
            {
                commandTree = loaded;
                FilterCommands();
            },
            generated =>
            {
                catalogOutput = generated;
                if (SelectedCommand() is { } name && !HasBuiltInForm(name))
                    Preview();
            },
            catalogStatus
        );
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 8,
                    Children = { Text("Command setup", 16, true), toolbar, filters, consoleFormat, syntax, catalogStatus },
                }
            )
        );
        PageBody.Children.Add(editorLayout);
        PageBody.Children.Add(catalogEditor);
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
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

    Border CommandCatalogEditor(
        Func<string> getVersion,
        Func<string?> getCommand,
        Action<Action> onVersionChanged,
        Action<Action> onCommandChanged,
        Action<CommandTree?> catalogLoaded,
        Action<string> updateOutput,
        TextBlock status
    )
    {
        CommandTree? tree = null;
        var path = new List<string>();
        var values = new List<string>();
        var optionalLeafDepth = -1;
        var includeOptionalLeaf = false;
        var arguments = new HashSet<int>();
        var rows = new StackPanel { Spacing = 8 };
        var editor = Card(rows);
        var revision = 0;
        string? requestedVersion = null;
        void Preview()
        {
            var shown = rows.Children.Count != 0;
            editor.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            var enabled = true;
            for (var index = 0; index < rows.Children.Count; index++)
            {
                ((ContentControl)rows.Children[index]).IsEnabled = enabled;
                var depth = index + 1;
                if (arguments.Contains(depth) && string.IsNullOrWhiteSpace(values[depth]))
                    enabled = false;
            }
            var omitLeaf = optionalLeafDepth >= 0 && !includeOptionalLeaf;
            updateOutput(omitLeaf
                ? tree?.Preview(path.Take(optionalLeafDepth).ToArray(), values.Take(optionalLeafDepth).ToArray()) ?? ""
                : tree?.Preview(path, values) ?? "");
        }
        void ShowStatus(string message)
        {
            status.Text = message;
            var shown = message.Length != 0;
            status.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowStatus("");
        Preview();
        void AppendChoices(int depth)
        {
            if (tree == null || depth >= 64)
                return;
            var choices = tree.Choices(path);
            if (choices.Count == 0)
                return;
            var optional = tree.IsExecutable(path);
            var optionalLeaf = optional && choices.Count == 1 && choices[0].Argument
                && tree.Choices(path.Append(choices[0].Name)).Count == 0;
            var automatic = choices.Count == 1 && (!optional || optionalLeaf);
            var row = new StackPanel { Spacing = 6 };
            var rowContainer = new ContentControl
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            rows.Children.Add(rowContainer);
            void ChooseBranch(int index, int fixedChildren)
            {
                if (depth > path.Count || !rows.Children.Contains(rowContainer))
                    return;
                while (rows.Children.Count > depth)
                    rows.Children.RemoveAt(rows.Children.Count - 1);
                while (row.Children.Count > fixedChildren)
                    row.Children.RemoveAt(fixedChildren);
                if (optionalLeafDepth >= depth)
                    optionalLeafDepth = -1;
                arguments.RemoveWhere(index => index >= depth);
                path.RemoveRange(depth, path.Count - depth);
                values.RemoveRange(depth, values.Count - depth);
                if (index < 0)
                {
                    Preview();
                    return;
                }
                var branch = choices[index];
                path.Add(branch.Name);
                if (branch.Argument)
                    arguments.Add(depth);
                values.Add("");
                if (optionalLeaf)
                    optionalLeafDepth = depth;
                void SetValue(string value)
                {
                    if (depth < values.Count && rows.Children.Contains(rowContainer))
                    {
                        values[depth] = value;
                        Preview();
                    }
                }
                if (branch.Argument && branch.Parser is ("minecraft:nbt_compound_tag" or "minecraft:nbt_tag"))
                {
                    var tags = new List<ItemTag>();
                    var singleValue = branch.Parser == "minecraft:nbt_tag";
                    var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
                    void UpdateNbt()
                    {
                        if (depth >= values.Count || !rows.Children.Contains(rowContainer))
                            return;
                        try
                        {
                            values[depth] = singleValue
                                ? tags.Count == 1 ? CommandGenerator.BuildNbtValue(tags[0]) : ""
                                : CommandGenerator.BuildNbtCompound(tags);
                            validation.Text = "";
                        }
                        catch (ArgumentException error)
                        {
                            values[depth] = "";
                            validation.Text = error.Message;
                        }
                        Preview();
                    }
                    row.Children.Add(ItemTagsPanel(getVersion(), tags, false, UpdateNbt, singleValue, branch.Name));
                    row.Children.Add(validation);
                    UpdateNbt();
                }
                else if (branch.Argument && branch.Parser is
                    ("minecraft:block_pos" or "minecraft:vec3" or "minecraft:column_pos" or "minecraft:vec2"))
                {
                    row.Children.Add(CoordinateEditor(
                        branch.Name, "", branch.Parser is "minecraft:column_pos" or "minecraft:vec2", SetValue
                    ));
                }
                else if (branch.Argument && CommandValueCatalog.OptionsForArgument(branch.Parser)
                    is { Count: > 0 } options)
                {
                    row.Children.Add(CommandOptionPicker(branch.Name, options, "", false, SetValue));
                }
                else if (branch.Argument && branch.Name == "criterion"
                    && path[0] is ("advancement" or "minecraft:advancement")
                    && path.IndexOf("advancement", 1) is >= 0 and var advancementDepth)
                {
                    row.Children.Add(CommandValueButton(
                        getVersion(), "Advancement criterion", "criterion", "", SetValue,
                        loadValues: ct => CommandValueCatalog.LoadAdvancementCriteria(
                            core, getVersion(), values[advancementDepth], ct
                        )
                    ));
                }
                else if (branch.Argument && CommandValueCatalog.RegistryForArgument(
                    branch.Parser, branch.Registry, branch.Name
                ) is { } registry)
                {
                    row.Children.Add(CommandValueButton(getVersion(), branch.Name, registry, "", value =>
                    {
                        if (depth >= values.Count || !rows.Children.Contains(rowContainer))
                            return;
                        if (registry == "advancement" && values[depth] != value)
                        {
                            while (rows.Children.Count > depth)
                                rows.Children.RemoveAt(rows.Children.Count - 1);
                            path.RemoveRange(depth + 1, path.Count - depth - 1);
                            values.RemoveRange(depth + 1, values.Count - depth - 1);
                            arguments.RemoveWhere(index => index > depth);
                            optionalLeafDepth = -1;
                            includeOptionalLeaf = false;
                            AppendChoices(depth + 1);
                        }
                        SetValue(value);
                    }));
                }
                else if (branch.Argument)
                {
                    var input = new TextBox { Header = Ui(branch.Name) };
                    var selectors = CommandValueCatalog.TargetSelectors(
                        branch.Parser, getVersion(), branch.SingleTarget, branch.PlayersOnly
                    );
                    if (selectors.Count != 0)
                        row.Children.Add(TargetSelectorPicker(input, selectors));
                    input.TextChanged += (_, _) => SetValue(input.Text ?? "");
                    row.Children.Add(input);
                }
                AppendChoices(depth + 1);
                Preview();
            }

            if (depth == 1 && path[0] is ("gamerule" or "minecraft:gamerule"))
            {
                var rules = choices.Select(branch => branch.Name).ToArray();
                row.Children.Add(CommandValueButton(
                    getVersion(), "Game rule", "gamerule", "", rule =>
                        ChooseBranch(Array.IndexOf(rules, rule), 1),
                    rules
                ));
                return;
            }

            if (optionalLeaf)
            {
                var include = new CheckBox { Content = Ui("Include optional argument") };
                row.Children.Add(include);
                void UpdateOptionalArgument()
                {
                    includeOptionalLeaf = include.IsChecked == true;
                    ChooseBranch(0, 1);
                    var editorBody = new StackPanel { Spacing = 6 };
                    while (row.Children.Count > 1)
                    {
                        var child = row.Children[1];
                        row.Children.RemoveAt(1);
                        editorBody.Children.Add(child);
                    }
                    row.Children.Add(new ContentControl
                    {
                        IsEnabled = includeOptionalLeaf,
                        Content = editorBody,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    });
                }
                include.Checked += (_, _) => UpdateOptionalArgument();
                include.Unchecked += (_, _) => UpdateOptionalArgument();
                UpdateOptionalArgument();
                return;
            }

            if (automatic)
            {
                if (!choices[0].Argument)
                {
                    row.Children.Add(Text(CommandGenerator.CommandOptionLabel(choices[0].Name, Ui, path[0])));
                }
                ChooseBranch(0, row.Children.Count);
                return;
            }

            var labels = choices.Select(branch => branch.Argument ? Ui(branch.Name) : CommandGenerator.CommandOptionLabel(branch.Name, Ui, path[0])).ToArray();
            var list = new BelowComboBox
            {
                ItemsSource = optional ? new[] { Ui("Omit argument") }.Concat(labels).ToArray() : labels,
                SelectedIndex = optional ? 0 : -1,
            };
            row.Children.Add(list);
            list.SelectionChanged += (_, _) =>
                ChooseBranch(list.SelectedIndex - (optional ? 1 : 0), 1);
        }
        void SetTree(CommandTree? selected)
        {
            tree = selected;
            rows.Children.Clear();
            optionalLeafDepth = -1;
            includeOptionalLeaf = false;
            arguments.Clear();
            path.Clear();
            values.Clear();
            if (tree != null && getCommand() is { } command)
            {
                path.Add(command);
                values.Add("");
                AppendChoices(1);
            }
            Preview();
        }

        async Task LoadVersion()
        {
            var key = getVersion();
            if (key == requestedVersion)
                return;
            requestedVersion = key;
            var current = ++revision;
            SetTree(null);
            if (key.Length == 0)
            {
                ShowStatus("");
                return;
            }
            ShowStatus(Ui("Loading Minecraft command catalog…"));
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var loaded = await CommandTree.LoadVanilla(
                    core.Http,
                    Path.Combine(store.Root, "command-reports"),
                    key,
                    timeout.Token
                );
                if (current != revision)
                    return;
                catalogLoaded(loaded);
                SetTree(loaded);
                ShowStatus(loaded == null
                    ? Ui("No command catalog is available for this version. Use a built-in form or the custom command editor.")
                    : "");
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or HttpRequestException
                            or OperationCanceledException
                            or System.Text.Json.JsonException
                            or UnauthorizedAccessException
                )
            {
                if (current == revision)
                    ShowStatus(Ui("Could not load the Minecraft command catalog.") + " " + ex.Message);
            }
        }
        onCommandChanged(() => SetTree(tree));
        onVersionChanged(async () => await LoadVersion());
        return editor;
    }

    FrameworkElement CoordinateEditor(string label, string initial, bool horizontal, Action<string> changed)
    {
        string[] axes = horizontal ? ["X", "Z"] : ["X", "Y", "Z"];
        var defaults = initial.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var grid = new Grid { ColumnSpacing = 8 };
        var inputs = new List<TextBox>();
        for (var index = 0; index < axes.Length; index++)
        {
            var axis = axes[index];
            var box = new TextBox { Header = axis, Text = defaults.ElementAtOrDefault(index) ?? "" };
            var field = box;
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(field, index);
            grid.Children.Add(field);
            inputs.Add(box);
        }
        foreach (var input in inputs)
            input.TextChanged += (_, _) =>
            {
                var values = inputs.Select(box => box.Text?.Trim() ?? "").ToArray();
                changed(values.All(value => value.Length != 0 && !value.Any(char.IsWhiteSpace))
                    ? string.Join(" ", values)
                    : "");
            };
        return new StackPanel { Spacing = 6, Children = { Text(label), grid } };
    }

    FrameworkElement TargetSelectorPicker(TextBox target, IReadOnlyList<string> selectors)
    {
        const string custom = "Custom target…";
        var customText = target.Text ?? "";
        var usingCustom = true;
        return CommandOptionPicker(
            "Target selector", selectors.Prepend(custom).ToArray(), custom, false, option =>
            {
                if (usingCustom)
                    customText = target.Text ?? "";
                usingCustom = option == custom;
                target.IsEnabled = usingCustom;
                target.Text = usingCustom ? customText : option;
            }
        );
    }

    FrameworkElement CommandOptionPicker(
        string label, IReadOnlyList<string> options, string current, bool optional, Action<string> changed
    )
    {
        var values = optional ? new[] { "" }.Concat(options).ToArray() : options.ToArray();
        var picker = new BelowComboBox
        {
            Header = Ui(label),
            ItemsSource = values.Select(value => value.Length == 0 ? Ui("Omit argument") : CommandGenerator.CommandOptionLabel(value, Ui)).ToArray(),
            SelectedIndex = Array.IndexOf(values, current),
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
                changed(values[picker.SelectedIndex]);
        };
        return picker;
    }

    Button CommandValueButton(
        string version, string label, string registry, string value, Action<string> selected,
        IReadOnlyList<string>? providedValues = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? loadValues = null,
        Func<string, bool>? validateCustom = null
    )
    {
        var fieldNameOnly = registry is "nbt" or "data_component_type";
        var displayName = fieldNameOnly || registry == "nbt_value"
            ? CommandValueCatalog.LocalizedChoice(value, registry, new Dictionary<string, string>(), Ui).Name
            : value;
        string Caption() => value.Length == 0 ? Ui(label)
            : fieldNameOnly ? displayName : $"{Ui(label)} · {displayName}";
        var button = new Button { Content = Caption() };
        button.Click += async (_, _) =>
        {
            var searchBox = new TextBox { PlaceholderText = Ui("Search values") };
            var list = new ListBox { Height = validateCustom == null ? 290 : 220 };
            var status = Text("");
            IReadOnlyList<CommandValueCatalog.Choice> entries = [];
            void Filter()
            {
                var query = searchBox.Text?.Trim() ?? "";
                var visible = entries.Where(entry =>
                    entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToArray();
                var selectedId = (list.SelectedItem as CommandValueCatalog.Choice)?.Id ?? value;
                list.ItemsSource = visible;
                list.SelectedItem = visible.FirstOrDefault(entry => entry.Id == selectedId);
                status.Text = visible.Length == 0 ? Ui("No matching values") : "";
            }
            searchBox.TextChanged += (_, _) => Filter();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            async Task Load()
            {
                searchBox.IsEnabled = false;
                status.Text = Ui("Loading available values…");
                try
                {
                    var ids = loadValues != null
                        ? await loadValues(timeout.Token)
                        : providedValues ?? await CommandValueCatalog.Load(core, version, registry, timeout.Token);
                    IReadOnlyDictionary<string, string> names = new Dictionary<string, string>();
                    if (registry is not ("nbt" or "nbt_value" or "criterion"))
                    {
                        try
                        {
                            names = await CommandValueCatalog.LoadLanguage(
                                core, version, store.Config.Settings.LauncherLanguage, timeout.Token
                            );
                        }
                        catch (Exception ex) when (ex is IOException or HttpRequestException
                            or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
                        {
                            // The registry remains usable when language assets are unavailable.
                        }
                    }
                    var available = validateCustom != null && value.Length != 0 && validateCustom(value)
                        ? ids.Prepend(value).Distinct(StringComparer.Ordinal)
                        : ids;
                    entries = available.Select(id => CommandValueCatalog.LocalizedChoice(id, registry, names, Ui))
                        .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();
                    Filter();
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException
                    or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    status.Text = Ui("Could not load values for this version.");
                }
                finally
                {
                    searchBox.IsEnabled = entries.Count != 0;
                }
            }
            var pickerBody = new StackPanel { Spacing = 8, Children = { searchBox, status, list } };
            if (validateCustom != null)
            {
                var custom = new TextBox { Header = Ui("Custom entry") };
                var add = new Button { Content = Ui("Add custom entry"), IsEnabled = false };
                custom.TextChanged += (_, _) => add.IsEnabled = custom.Text is { Length: > 0 } text
                    && validateCustom(text);
                add.Click += (_, _) =>
                {
                    var text = custom.Text ?? "";
                    if (!validateCustom(text))
                        return;
                    var entry = entries.FirstOrDefault(candidate => candidate.Id == text)
                        ?? CommandValueCatalog.LocalizedChoice(text, registry, new Dictionary<string, string>(), Ui);
                    if (!entries.Contains(entry))
                        entries = entries.Append(entry).ToArray();
                    searchBox.Text = "";
                    Filter();
                    list.SelectedItem = entry;
                    custom.Text = "";
                };
                pickerBody.Children.Add(custom);
                pickerBody.Children.Add(add);
            }
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui(label),
                PrimaryButtonText = Ui("Select"),
                CloseButtonText = Ui("Cancel"),
                IsPrimaryButtonEnabled = false,
                Content = pickerBody,
            };
            list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is CommandValueCatalog.Choice;
            dialog.Opened += async (_, _) => await Load();
            var result = await dialog.ShowAsync();
            var chosen = result == ContentDialogResult.Primary ? list.SelectedItem as CommandValueCatalog.Choice : null;
            timeout.Cancel();
            if (chosen == null)
                return;
            value = chosen.Id;
            displayName = chosen.Name;
            button.Content = Caption();
            selected(chosen.Id);
        };
        return button;
    }

    FrameworkElement ItemEnchantmentEditor(string version, List<ItemEnchantment> enchantments, Action changed)
    {
        var panel = new StackPanel { Spacing = 8 };
        var legacyChoices = CommandGenerator.LegacyEnchantmentChoices(version);
        void Draw()
        {
            panel.Children.Clear();
            foreach (var enchantment in enchantments)
            {
                var select = CommandValueButton(
                    version, "Choose enchantment", "enchantment", enchantment.Id, id =>
                    {
                        enchantment.Id = id;
                        changed();
                    },
                    legacyChoices
                );
                var level = new NumberBox
                {
                    Header = Ui("Enchantment level"), Minimum = 1,
                    Maximum = CommandGenerator.MaxEnchantmentLevel(version),
                    Value = enchantment.Level,
                };
                level.ValueChanged += (_, args) =>
                {
                    enchantment.Level = double.IsFinite(args.NewValue) && args.NewValue == Math.Truncate(args.NewValue)
                        ? (int)args.NewValue : 0;
                    changed();
                };
                var row = new StackPanel { Spacing = 6 };
                row.Children.Add(select);
                row.Children.Add(level);
                row.Children.Add(Action("Remove enchantment", (_, _) => { enchantments.Remove(enchantment); Draw(); changed(); }));
                panel.Children.Add(Card(row));
            }
            panel.Children.Add(Action("Add enchantment", (_, _) => { enchantments.Add(new ItemEnchantment()); Draw(); changed(); }));
        }
        Draw();
        return panel;
    }

    FrameworkElement ItemTagsPanel(string version, List<ItemTag> tags, bool components, Action changed, bool singleValue = false, string? title = null, bool itemEditor = false)
    {
        var panel = new StackPanel { Spacing = 8 };
        void Draw()
        {
            panel.Children.Clear();
            panel.Children.Add(Text(title ?? (components ? "Item components" : "Item NBT")));
            DrawNodes(panel, tags, keyed: !singleValue, fixedType: null, depth: 0);
        }
        void DrawNodes(
            StackPanel container,
            List<ItemTag> entries,
            bool keyed,
            ItemTagType? fixedType,
            int depth
        )
        {
            foreach (var entry in entries)
            {
                var row = new StackPanel { Spacing = 5 };
                if (keyed)
                {
                    var component = components && depth == 0;
                    row.Children.Add(CommandValueButton(
                        version, component ? "Component ID" : "NBT key",
                        component ? "data_component_type" : "nbt", entry.Key, value =>
                        {
                            entry.Key = value;
                            if (itemEditor && depth == 0)
                            {
                                if (CommandGenerator.IsItemEnchantmentField(value, components))
                                    entry.Enchantments ??= [new ItemEnchantment()];
                                else
                                    entry.Enchantments = null;
                                Draw();
                            }
                            changed();
                        }, validateCustom: component ? null : key =>
                            !string.IsNullOrWhiteSpace(key) && !key.Any(char.IsControl)
                    ));
                }
                if (itemEditor && depth == 0 && entry.Enchantments is { } enchantments)
                    row.Children.Add(ItemEnchantmentEditor(version, enchantments, changed));
                else
                {
                    var types = Enum.GetValues<ItemTagType>();
                    var type = new BelowComboBox
                    {
                        ItemsSource = types.Select(value =>
                        {
                            var key = "NBT type " + value;
                            var translated = Ui(key);
                            return translated == key ? value.ToString() : translated;
                        }).ToArray(),
                        SelectedIndex = Array.IndexOf(types, entry.Type),
                        IsEnabled = fixedType == null,
                    };
                    type.SelectionChanged += (_, _) =>
                    {
                        if (type.SelectedIndex < 0)
                            return;
                        entry.Type = types[type.SelectedIndex];
                        entry.Children.Clear();
                        Draw();
                        changed();
                    };
                    row.Children.Add(type);
                    if (CommandGenerator.IsContainer(entry.Type))
                    {
                        var nested = new StackPanel
                        {
                            Spacing = 6,
                            Margin = new Thickness(14, 0, 0, 0),
                        };
                        DrawNodes(
                            nested,
                            entry.Children,
                            entry.Type == ItemTagType.Compound,
                            CommandGenerator.RequiredElementType(entry.Type),
                            depth + 1
                        );
                        row.Children.Add(nested);
                    }
                    else
                    {
                        row.Children.Add(CommandValueButton(
                            version, "NBT value", "nbt_value", entry.Value, value =>
                            {
                                entry.Value = value;
                                changed();
                            },
                            CommandValueCatalog.NbtValueOptions(entry.Type, entry.Key, entry.Value),
                            validateCustom: value => CommandGenerator.IsValidNbtValue(entry.Type, value)
                        ));
                    }
                }
                row.Children.Add(
                    Action(
                        "Remove NBT field",
                        (_, _) =>
                        {
                            entries.Remove(entry);
                            Draw();
                            changed();
                        }
                    )
                );
                container.Children.Add(CompactTagRow(row));
            }
            if (depth < 16 && (!singleValue || depth != 0 || entries.Count == 0) && entries.Count < 64)
                container.Children.Add(
                    Action(
                        "Add NBT field",
                        (_, _) =>
                        {
                            if (entries.Count >= 64)
                                return;
                            entries.Add(
                                new ItemTag
                                {
                                    Type =
                                        fixedType
                                        ?? (
                                            keyed
                                                ? ItemTagType.String
                                                : entries.FirstOrDefault()?.Type
                                                    ?? ItemTagType.String
                                        ),
                                }
                            );
                            Draw();
                            changed();
                        }
                    )
                );
        }
        Border CompactTagRow(StackPanel row) => CompactCard(row);
        Draw();
        return panel;
    }
}
