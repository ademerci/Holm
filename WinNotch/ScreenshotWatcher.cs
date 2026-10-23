using System;
using System.IO;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Notices new pictures in the Screenshots folder (Win+Print Screen, and the Snipping Tool, which saves there by
/// default). Windows reports new files, so nothing is polled; a picture is reported once it can be read.
/// </summary>
internal sealed class ScreenshotWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly Dispatcher _ui;
    readonly DispatcherTimer _settle;
    string? _pending;
    int _tries;

    /// <summary>A new screenshot, ready to read. Raised on the UI thread.</summary>
    public event Action<string>? Taken;

    /// <exception cref="ArgumentException">The folder doesn't exist.</exception>
    public ScreenshotWatcher(string folder, Dispatcher ui)
    {
        _ui = ui;
        // The tool writes the file in pieces: look once it has been quiet for a moment.
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _settle!.Stop(); Check(); }, ui);
        _settle.Stop();
        _watcher = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        _watcher.Created += (_, e) => Seen(e.FullPath);
        _watcher.Renamed += (_, e) => { if (e.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) Seen(e.FullPath); };
        _watcher.EnableRaisingEvents = true;
    }

    void Seen(string path) => _ui.InvokeAsync(() =>
    {
        _pending = path;
        _tries = 0;
        _settle.Stop();
        _settle.Start();
    });

    void Check()
    {
        if (_pending is not { } path) return;
        if (!Readable(path))
        {
            if (++_tries < 10) _settle.Start(); // still being written
            else _pending = null;
            return;
        }
        _pending = null;
        Taken?.Invoke(path);
    }

    static bool Readable(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return file.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _settle.Stop();
        _pending = null;
    }
}
