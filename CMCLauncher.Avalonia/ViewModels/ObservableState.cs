using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CMCLauncher.Avalonia.ViewModels;

public abstract class ObservableState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Changed(name);
        return true;
    }
}

public sealed class OperationState(string title) : ObservableState
{
    public string Title { get; } = title;
    string stage = "Processing…",
        state = "Running";
    double? progress;
    public string Stage
    {
        get => stage;
        set => Set(ref stage, value);
    }
    public string State
    {
        get => state;
        set => Set(ref state, value);
    }
    public double? Progress
    {
        get => progress;
        set
        {
            Set(ref progress, value);
            Changed(nameof(IsIndeterminate));
            Changed(nameof(Percent));
        }
    }
    public bool IsRunning { get; private set; } = true;
    DateTimeOffset? finished;
    public string Elapsed => ((finished ?? DateTimeOffset.Now) - Started).ToString(@"hh\:mm\:ss");

    public void Tick() => Changed(nameof(Elapsed));

    public void Finish()
    {
        finished = DateTimeOffset.Now;
        IsRunning = false;
        Changed(nameof(IsRunning));
        Changed(nameof(IsIndeterminate));
        Tick();
    }

    public bool IsIndeterminate => !Progress.HasValue && IsRunning;
    public double Percent => Progress ?? 0;
    public DateTimeOffset Started { get; } = DateTimeOffset.Now;
    public long Received { get; private set; }
    public bool HasReceived => Received > 0;
    public string ReceivedText => $"{Received / 1048576.0:N1} MiB";

    public void AddReceived(long bytes)
    {
        Received += bytes;
        Changed(nameof(HasReceived));
        Changed(nameof(ReceivedText));
    }
}
