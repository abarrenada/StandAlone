namespace StandAlone.Integration.Services;

public interface IFileWatcher
{
    void StartWatching(string directory, Action<string> onFileDetected, CancellationToken cancellationToken);
    void StopWatching();
}

public class DirectoryFileWatcher : IFileWatcher
{
    private FileSystemWatcher? _watcher;

    public void StartWatching(string directory, Action<string> onFileDetected, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        _watcher.Created += (sender, e) => onFileDetected(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
    }
}
