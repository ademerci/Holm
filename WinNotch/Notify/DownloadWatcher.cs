using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WinNotch.Notify;

/// <summary>A download a browser is writing into the folder.</summary>
public sealed class Download
{
    /// <summary>The browser's working file (…crdownload, …part), renamed when the download is done.</summary>
    public string TempPath = "";
    /// <summary>The file's name once the browser knows it; empty while Chrome still calls it "Unconfirmed 123456".</summary>
    public string Name = "";
    public long Bytes;
    public double BytesPerSecond;
    public DateTime Started;
    internal DateTime? DeletedAt;
    internal long LastBytes;
    internal DateTime LastSample;
}

/// <summary>
/// Downloads in a folder (Downloads), as browsers write them: Chrome, Edge, Brave, Opera and Vivaldi into
/// "name.crdownload", Firefox into "name.part". Event driven; the sizes are only measured while a download runs.
/// Events come on a worker thread.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    static readonly string[] TempExtensions = { ".crdownload", ".part", ".partial", ".opdownload", ".download" };

    readonly string _folder;
    readonly TimeSpan _tick;
    readonly object _gate = new();
    readonly Dictionary<string, Download> _active = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? _watcher;
    Timer? _timer;
    bool _disposed;

    public event Action<Download>? Started, Updated, Cancelled;
    /// <summary>A download is complete; the second argument is the finished file.</summary>
    public event Action<Download, string>? Finished;

    public DownloadWatcher(string folder, TimeSpan? tick = null)
    {
        _folder = folder;
        _tick = tick ?? TimeSpan.FromMilliseconds(500);
    }

    public IReadOnlyList<Download> Active
    {
        get { lock (_gate) return _active.Values.Where(d => d.DeletedAt == null).ToList(); }
    }

    public void Start()
    {
        if (_watcher != null) return;
        _watcher = new FileSystemWatcher(_folder)
        {
            NotifyFilter = NotifyFilters.FileName,
            IncludeSubdirectories = false,
            InternalBufferSize = 16 * 1024,
        };
        _watcher.Created += (_, e) => OnCreated(e.FullPath);
        _watcher.Renamed += (_, e) => OnRenamed(e.OldFullPath, e.FullPath);
        _watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    public static bool IsTemp(string path)
        => TempExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>The name without the browser's extension; empty for Chrome's placeholder "Unconfirmed 123456".</summary>
    public static string NameOf(string tempPath)
    {
        string name = Path.GetFileName(tempPath);
        foreach (var x in TempExtensions)
            if (name.EndsWith(x, StringComparison.OrdinalIgnoreCase)) { name = name[..^x.Length]; break; }
        // Chrome's placeholder has no extension and ends in a number ("Unconfirmed 123456", "Nicht bestätigt 123456").
        int space = name.LastIndexOf(' ');
        if (!name.Contains('.') && space > 0 && name[(space + 1)..].All(char.IsAsciiDigit)) return "";
        return name;
    }

    void OnCreated(string path)
    {
        if (!IsTemp(path)) return;
        Download? started = null;
        lock (_gate)
        {
            if (_disposed || _active.ContainsKey(path)) return;
            var now = DateTime.UtcNow;
            started = new Download { TempPath = path, Name = NameOf(path), Started = now, LastSample = now };
            _active[path] = started;
            EnsureTimer();
        }
        Started?.Invoke(started);
    }

    void OnRenamed(string from, string to)
    {
        bool wasTemp = IsTemp(from), isTemp = IsTemp(to);
        if (!wasTemp && isTemp) { OnCreated(to); return; }
        if (!wasTemp) return;

        Download? download;
        lock (_gate)
        {
            if (_disposed || !_active.Remove(from, out download)) download = null;
            if (download != null && isTemp)
            {
                // Chrome: "Unconfirmed 123456.crdownload" becomes "photo.jpg.crdownload" once the name is known.
                download.TempPath = to;
                download.Name = NameOf(to);
                _active[to] = download;
            }
        }
        if (download == null)
        {
            if (isTemp) OnCreated(to);
            return;
        }
        if (isTemp) { Updated?.Invoke(download); return; }
        Complete(download, to);
    }

    void OnDeleted(string path)
    {
        if (!IsTemp(path)) return;
        lock (_gate)
        {
            // Cancelled, or a browser that copies the finished file instead of renaming it: the tick decides.
            if (_active.TryGetValue(path, out var download)) download.DeletedAt = DateTime.UtcNow;
        }
    }

    void Complete(Download download, string file)
    {
        try { download.Bytes = new FileInfo(file).Length; } catch { }
        if (download.Name.Length == 0) download.Name = Path.GetFileName(file);
        Finished?.Invoke(download, file);
    }

    void EnsureTimer() => _timer ??= new Timer(_ => Tick(), null, _tick, _tick);

    void Tick()
    {
        var updated = new List<Download>();
        var finished = new List<(Download, string)>();
        var cancelled = new List<Download>();
        lock (_gate)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            foreach (var download in _active.Values.ToList())
            {
                if (download.DeletedAt is { } deleted)
                {
                    if (now - deleted < TimeSpan.FromSeconds(1)) continue;
                    _active.Remove(download.TempPath);
                    string final = Path.Combine(Path.GetDirectoryName(download.TempPath)!, Path.GetFileName(download.TempPath)[..^Path.GetExtension(download.TempPath).Length]);
                    bool done = File.Exists(final) && new FileInfo(final).Length > 0 && File.GetLastWriteTimeUtc(final) >= download.Started.AddSeconds(-2);
                    if (done) finished.Add((download, final)); else cancelled.Add(download);
                    continue;
                }
                long bytes;
                try { bytes = new FileInfo(download.TempPath).Length; }
                catch { continue; }
                double seconds = (now - download.LastSample).TotalSeconds, before = download.BytesPerSecond;
                if (seconds > 0)
                {
                    double rate = Math.Max(0, bytes - download.LastBytes) / seconds;
                    // Smooth it, so the number doesn't jump around with every sample.
                    download.BytesPerSecond = download.LastBytes == 0 && download.BytesPerSecond == 0 ? rate : 0.6 * download.BytesPerSecond + 0.4 * rate;
                }
                // Stalled: let the speed run down to nothing instead of showing the last one forever.
                if (bytes == download.Bytes && download.BytesPerSecond < 1024) download.BytesPerSecond = 0;
                download.LastBytes = bytes;
                download.LastSample = now;
                if (bytes != download.Bytes || Math.Abs(before - download.BytesPerSecond) >= 1024)
                {
                    download.Bytes = bytes;
                    updated.Add(download);
                }
            }
            if (_active.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
        foreach (var download in updated) Updated?.Invoke(download);
        foreach (var (download, file) in finished) Complete(download, file);
        foreach (var download in cancelled) Cancelled?.Invoke(download);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _active.Clear();
        }
        _watcher?.Dispose();
        _watcher = null;
    }
}
