namespace CMCLauncher.Avalonia.ViewModels;

public sealed class ExportNode(string path, string label, bool selected) : ObservableState
{
    bool isSelected = selected;
    public string Path { get; } = path;
    public string Label { get; } = label;
    public List<ExportNode> Children { get; } = [];
    public bool IsFile { get; set; }
    public bool Selected
    {
        get => isSelected;
        set
        {
            if (Set(ref isSelected, value))
                foreach (var child in Children)
                    child.Selected = value;
        }
    }

    public IEnumerable<string> Files() =>
        IsFile
            ? Selected
                ? [Path]
                : []
            : Children.SelectMany(c => c.Files());
}
