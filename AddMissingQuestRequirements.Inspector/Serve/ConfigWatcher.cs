namespace AddMissingQuestRequirements.Inspector.Serve;

/// <summary>
/// Watches a directory tree for *.jsonc file saves and invokes a reload
/// delegate at most once per <paramref name="debounceMs"/> window.
/// The delegate runs on a thread-pool thread — not on the watcher thread —
/// so a long reload does not block further events.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Action _onReload;
    private readonly int _debounceMs;
    private readonly object _lock = new();
    private CancellationTokenSource? _pending;

    public ConfigWatcher(string rootPath, int debounceMs, Action onReload)
    {
        _onReload = onReload;
        _debounceMs = debounceMs;
        _fsw = new FileSystemWatcher(rootPath)
        {
            Filter = "*.jsonc",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _fsw.Changed += OnAny;
        _fsw.Created += OnAny;
        _fsw.Renamed += OnAny;
        _fsw.Deleted += OnAny;
    }

    public void Start()
    {
        _fsw.EnableRaisingEvents = true;
    }

    private void OnAny(object sender, FileSystemEventArgs e)
    {
        Schedule();
    }

    private void Schedule()
    {
        lock (_lock)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            var token = _pending.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _onReload();
                    }
                }
                catch (TaskCanceledException) { }
            });
        }
    }

    public void Dispose()
    {
        _fsw.EnableRaisingEvents = false;
        _fsw.Dispose();
        lock (_lock)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
    }
}
