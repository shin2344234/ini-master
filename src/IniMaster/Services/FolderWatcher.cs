using System.IO;
using System.Windows.Threading;

namespace IniMaster.Services;

/// Watches the plugin folders and reports changed paths on the UI thread,
/// after a short quiet period so a burst of writes arrives as one event.
public sealed class FolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;
    private readonly object _lock = new();
    private readonly Action<IReadOnlyCollection<string>> _onChanged;

    public FolderWatcher(Action<IReadOnlyCollection<string>> onChanged)
    {
        _onChanged = onChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Flush();
    }

    public void Watch(IEnumerable<string> folders)
    {
        Stop();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            w.Changed += OnEvent;
            w.Created += OnEvent;
            w.Deleted += OnEvent;
            w.Renamed += (_, e) => { Queue(e.OldFullPath); Queue(e.FullPath); };
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Queue(e.FullPath);

    private void Queue(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".ini" or ".asi" or ".inimeta")) return;
        lock (_lock) _pending.Add(path);
        _timer.Dispatcher.BeginInvoke(() => { _timer.Stop(); _timer.Start(); });
    }

    private void Flush()
    {
        _timer.Stop();
        List<string> paths;
        lock (_lock)
        {
            paths = _pending.ToList();
            _pending.Clear();
        }
        if (paths.Count > 0) _onChanged(paths);
    }

    public void Stop()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    public void Dispose() => Stop();
}
