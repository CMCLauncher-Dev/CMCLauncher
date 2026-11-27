using System.Diagnostics;

namespace CMCLauncher;

// Both frontends update the process list on their UI thread.
public sealed class RunningGames
{
    readonly List<Game> games = [];

    public bool Any => games.Count > 0;
    public IEnumerable<Game> Active => games.Where(game => !game.Process.HasExited);

    public bool Contains(Instance instance, string directory) =>
        games.Any(game => game.Instance.Id == instance.Id
            || DirectoryComparer.Equals(game.Directory, NormalizeDirectory(directory)));

    public Game Add(Instance instance, string directory, Process process)
    {
        var game = new Game(instance, NormalizeDirectory(directory), process);
        games.Add(game);
        return game;
    }

    public void Remove(Game game) => games.Remove(game);

    public void Stop(Game game)
    {
        if (games.Contains(game))
            game.Stop();
    }

    public static StringComparer DirectoryComparer { get; } = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    public static string NormalizeDirectory(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    public sealed class Game(Instance instance, string directory, Process process)
    {
        public Instance Instance { get; } = instance;
        public string Directory { get; } = directory;
        public Process Process { get; } = process;
        public string Label => $"{Instance.Name} · PID {Process.Id}";
        public bool StopRequested { get; private set; }

        internal void Stop()
        {
            if (StopRequested || Process.HasExited)
                return;
            try
            {
                Process.Kill(entireProcessTree: true);
                StopRequested = true;
            }
            catch (InvalidOperationException) when (Process.HasExited) { }
        }
    }
}
