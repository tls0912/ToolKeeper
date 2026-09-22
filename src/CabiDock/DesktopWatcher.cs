namespace CabiDock;

public sealed record DesktopChange(WatcherChangeTypes Kind, string FullPath, string? OldFullPath = null);

/// <summary>The controller marshals notifications to its UI thread before changing saved state.</summary>
public sealed class DesktopWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    public event Action<DesktopChange>? Changed;
    public event Action<string>? Error;
    public bool NeedsRestart { get; private set; }

    public void Start(IEnumerable<string> roots)
    {
        DisposeWatchers();
        NeedsRestart = false;
        foreach (var root in roots)
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                    InternalBufferSize = 32768
                };
                watcher.Created += (_, e) => Changed?.Invoke(new(e.ChangeType, e.FullPath));
                watcher.Deleted += (_, e) => Changed?.Invoke(new(e.ChangeType, e.FullPath));
                watcher.Changed += (_, e) => Changed?.Invoke(new(e.ChangeType, e.FullPath));
                watcher.Renamed += (_, e) => Changed?.Invoke(new(e.ChangeType, e.FullPath, e.OldFullPath));
                watcher.Error += (_, e) => { NeedsRestart = true; Error?.Invoke(e.GetException().Message); };
                _watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                NeedsRestart = true;
                Error?.Invoke(ex.Message);
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    public void Dispose() => DisposeWatchers();
}
