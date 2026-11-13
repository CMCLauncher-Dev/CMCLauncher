using Avalonia.Controls;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Button ConfirmationButton(
        string label,
        string prompt,
        Func<Task> confirmed,
        string confirmLabel = "Delete"
    )
    {
        var flyout = new Flyout();
        var button = Btn(label, () => Task.CompletedTask);
        flyout.Content = Stack(
            T(prompt),
            Row(
                DangerButton(
                    confirmLabel,
                    async () =>
                    {
                        flyout.Hide();
                        await confirmed();
                    }
                ),
                Btn(
                    "Cancel",
                    () =>
                    {
                        flyout.Hide();
                        return Task.CompletedTask;
                    }
                )
            )
        );
        button.Flyout = flyout;
        return button;
    }
}
