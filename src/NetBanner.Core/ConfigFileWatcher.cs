namespace NetBanner.Core;

/// <summary>
/// Raises <see cref="Changed"/> (debounced, on a background thread) when the config file is created,
/// changed, renamed or deleted. If its folder doesn't exist yet, checks every 30 seconds until it does.
/// </summary>
public sealed class ConfigFileWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FolderPoll = TimeSpan.FromSeconds(30);

    private readonly string _directory;
    private readonly string _fileName;
    private readonly Timer _debounce;
    private readonly Timer _folderPoll;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public event EventHandler? Changed;

    public ConfigFileWatcher(string path)
    {
        var full = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(full) ?? throw new ArgumentException("Config path has no folder.", nameof(path));
        _fileName = Path.GetFileName(full);
        _debounce = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        _folderPoll = new Timer(_ => TryAttach(raiseOnAttach: true));
    }

    public void Start() => TryAttach(raiseOnAttach: false);

    private void TryAttach(bool raiseOnAttach)
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            if (!Directory.Exists(_directory))
            {
                _folderPoll.Change(FolderPoll, Timeout.InfiniteTimeSpan);
                return;
            }

            try
            {
                var w = new FileSystemWatcher(_directory, _fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                w.Changed += OnEvent;
                w.Created += OnEvent;
                w.Deleted += OnEvent;
                w.Renamed += OnEvent;
                w.Error += (_, e) =>
                {
                    Log.Warn($"Config file watcher error: {e.GetException().Message}. Re-attaching.");
                    Reattach();
                };
                w.EnableRaisingEvents = true;
                _watcher = w;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not watch {_directory}: {ex.Message}. Retrying in {FolderPoll.TotalSeconds:0}s.");
                _folderPoll.Change(FolderPoll, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        if (raiseOnAttach)
        {
            OnEvent(this, EventArgs.Empty);
        }
    }

    private void Reattach()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        TryAttach(raiseOnAttach: true);
    }

    private void OnEvent(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
        }

        _debounce.Dispose();
        _folderPoll.Dispose();
    }
}
