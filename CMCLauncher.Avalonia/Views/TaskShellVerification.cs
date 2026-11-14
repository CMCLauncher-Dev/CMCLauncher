using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    async Task VerifyTaskShell(List<string> checks)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = session.Run("Task shell verification", _ => release.Task);
        try
        {
            await Navigate("home");
            if (
                pageContent
                    .GetVisualDescendants()
                    .OfType<ProgressBar>()
                    .Any(bar => Equals(bar.Tag, "home-launch-progress") && bar.IsEffectivelyVisible)
            )
                throw new IOException("A background task displayed game-launch progress on Home.");
            await Navigate("tasks");
            UpdateLayout();
            var pane = (Visual)drawer.Pane!;
            if (drawer.OpenPaneLength != 440 || pane is not Grid { RowDefinitions.Count: 3 })
                throw new IOException(
                    "Task drawer retains the old width or scrolling header layout."
                );
            var logs = pane.GetVisualDescendants()
                .OfType<Button>()
                .Single(button =>
                    global::Avalonia.Automation.AutomationProperties.GetName(button)
                    == Ui("Runtime logs")
                );
            if (
                Grid.GetRow(logs) != 2
                || logs.Width != 34
                || logs.Content is not global::Avalonia.Controls.Shapes.Path { Stroke: not null }
            )
                throw new IOException(
                    "Task drawer log action is oversized, misplaced or has an invisible icon."
                );
            var footerBars = this.GetVisualDescendants()
                .OfType<ProgressBar>()
                .Where(bar => bar.IsEffectivelyVisible)
                .Where(bar => !bar.GetVisualAncestors().Contains(pane))
                .Where(bar => !bar.GetVisualAncestors().Contains(pageContent))
                .ToArray();
            if (footerBars.Length != 0)
                throw new IOException("An obsolete global task progress bar is still visible.");
            if (!pane.GetVisualDescendants().OfType<ProgressBar>().Any())
                throw new IOException("Active task progress is missing from the drawer.");
            checks.Add("PASS active task progress stays in the drawer without a duplicate footer");
        }
        finally
        {
            release.TrySetResult();
            await operation;
            UpdateLayout();
            if (
                drawer.Pane is Visual pane
                && pane.GetVisualDescendants().OfType<ProgressBar>().Any(bar => bar.IsVisible)
            )
                throw new IOException("Completed task cards still show obsolete progress bars.");
            drawer.IsPaneOpen = false;
        }
        await Navigate("logs");
        var toolbar = pageContent
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(grid => Equals(grid.Tag, "logs-toolbar"));
        var view = pageContent
            .GetVisualDescendants()
            .OfType<TextBox>()
            .Single(box => Equals(box.Tag, "runtime-log-view"));
        if (
            toolbar.Children.Count != 3
            || toolbar.Children[0] is not ToggleSwitch
            || !view.IsReadOnly
            || !view.AcceptsReturn
        )
            throw new IOException("Logs page still uses the legacy toolbar or list presentation.");
        session.Log("INFO", "UI-audit live-log verification");
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (
            view.Text?.Contains("UI-audit live-log verification") != true
            && deadline.Elapsed < TimeSpan.FromSeconds(5)
        )
            await Task.Delay(25);
        if (view.Text?.Contains("UI-audit live-log verification") != true)
            throw new IOException("The readonly log view did not receive live events.");
        checks.Add("PASS current WinUI log toolbar and live readonly text presentation");
        var input = Input("Dialog theme verification");
        var pending = Dialog("Dialog theme verification", input, false);
        Window? dialog = null;
        try
        {
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => { },
                global::Avalonia.Threading.DispatcherPriority.Background
            );
            var lifetime =
                (global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    Application.Current!.ApplicationLifetime!;
            dialog = lifetime.Windows.Single(window =>
                window != this && window.Title == Ui("Dialog theme verification")
            );
            dialog.UpdateLayout();
            if (
                !ReferenceEquals(dialog.Background, surfaceBrush)
                || dialog.RequestedThemeVariant != ActualThemeVariant
            )
                throw new IOException("Owned dialogs did not inherit the current theme.");
            if (
                session.Store.Config.Settings.ColorPreset != "native"
                && !ReferenceEquals(input.Background, controlSurfaceBrush)
            )
                throw new IOException("Dialog input uses legacy control styling.");
        }
        finally
        {
            dialog?.Close(false);
            await pending;
        }
        checks.Add("PASS owned dialog background, theme variant and editable-control styling");
    }
}
