using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyFrpcParity(List<string> checks)
    {
        await Navigate("frpc");
        var controls = pageContent.GetVisualDescendants().OfType<Control>().ToArray();
        var create = controls.OfType<Expander>().Single(c => Equals(c.Tag, "frpc-new-panel"));
        var import = controls.OfType<Expander>().Single(c => Equals(c.Tag, "frpc-import-panel"));
        if (create.IsExpanded || import.IsExpanded)
            throw new IOException("FRP creation and import must start collapsed.");
        var editor = controls.OfType<TextBox>().Single(c => Equals(c.Tag, "frpc-editor"));
        editor.Text = "serverAddr = \"localhost\"";
        controls
            .OfType<Button>()
            .Single(c => Equals(c.Content, Ui("Save as")))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (
            !create.IsExpanded
            || !create.GetVisualDescendants().OfType<TextBox>().Any(c => c.Text == editor.Text)
        )
            throw new IOException("FRP Save as did not populate the creation editor.");
        var output = controls.OfType<TextBox>().Single(c => Equals(c.Tag, "frpc-output"));
        session.Log("GAME", "unrelated-frpc-test-line");
        session.Log("FRPC", "frpc-output-test-line");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (
            !output.IsReadOnly
            || output.Text?.Contains("frpc-output-test-line") != true
            || output.Text.Contains("unrelated-frpc-test-line")
        )
            throw new IOException("FRP output does not isolate its live process logs.");
        checks.Add("PASS FRP collapsed forms, Save as draft and live process-only output");
    }
}
