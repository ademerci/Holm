using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using WinNotch.Notify;

namespace WinNotch;

/// <summary>
/// Downloads: while a browser saves a file into Downloads, the notch shows its name, size and speed;
/// when it's done, it offers to open the file or show it in Explorer.
/// </summary>
public partial class MainWindow
{
    DownloadWatcher? _downloads;
    Download? _download; // the one shown: the newest that's still running
    bool _downloadPeek;  // just started: shown for a moment even over music
    DispatcherTimer _downloadPeekTimer = null!;

    void InitDownloads()
    {
        _downloadPeekTimer = MakeTimer(4000, () => { _downloadPeek = false; ApplyState(); }, oneShot: true);
    }

    void StartDownloads()
    {
        if (!_settings.Downloads || _downloads != null) return;
        try
        {
            var watcher = new DownloadWatcher(Native.DownloadsFolder());
            // Events come on a worker thread; a watcher switched off meanwhile is ignored.
            watcher.Started += d => Dispatcher.InvokeAsync(() => { if (_downloads == watcher) OnDownloadStarted(d); });
            watcher.Updated += _ => Dispatcher.InvokeAsync(() => { if (_downloads == watcher) RefreshDownload(); });
            watcher.Finished += (d, file) => Dispatcher.InvokeAsync(() => { if (_downloads == watcher) OnDownloadFinished(d, file); });
            watcher.Cancelled += d => Dispatcher.InvokeAsync(() => { if (_downloads == watcher) OnDownloadGone(d); });
            watcher.Start();
            _downloads = watcher;
        }
        catch (Exception ex) { Diag.Log("Downloads can't be watched: " + ex.Message); }
    }

    void StopDownloads()
    {
        _downloads?.Dispose();
        _downloads = null;
        _downloadPeekTimer.Stop();
        _downloadPeek = false;
        _download = null;
        RefreshDownload();
    }

    void OnDownloadStarted(Download download)
    {
        Diag.Log("Download started");
        _download = download;
        _downloadPeek = true;
        _downloadPeekTimer.Stop();
        _downloadPeekTimer.Start();
        RefreshDownload();
    }

    void OnDownloadFinished(Download download, string file)
    {
        OnDownloadGone(download);
        Diag.Log("Download finished");
        CheckDiskSoon(); // a big one may have filled the drive
        if (_hiddenForFullscreen) return;
        string name = Path.GetFileName(file);
        // A Beam question or transfer keeps the task view; a short note will do then.
        if (_busy || _taskVisible || _sharing)
        {
            ShowPeek("", "Downloaded " + name, "", null, false, null);
            return;
        }
        ShowTask("", GreenBrush, "Downloaded", $"{name} · {Size(download.Bytes)}", null,
                 ("Open", () => { HideTask(); TryOpen(file); }),
                 ("Show", () => { HideTask(); try { Shell.Reveal(file); } catch (Exception ex) { Diag.Error(ex); } }));
        _taskHide.Interval = TimeSpan.FromSeconds(6);
        _taskHide.Start();
    }

    void OnDownloadGone(Download download)
    {
        if (ReferenceEquals(_download, download)) _download = null;
        RefreshDownload();
    }

    /// <summary>Name, size and speed in the notch and in the open notch.</summary>
    void RefreshDownload()
    {
        var running = _downloads?.Active ?? Array.Empty<Download>();
        _download ??= running.OrderBy(d => d.Started).LastOrDefault();
        if (_download == null)
        {
            _downloadPeek = false;
            _downloadPeekTimer.Stop();
        }
        else
        {
            var shown = _download;
            long bytes = running.Count > 1 ? running.Sum(d => d.Bytes) : shown.Bytes;
            double speed = running.Count > 1 ? running.Sum(d => d.BytesPerSecond) : shown.BytesPerSecond;
            DownloadName.Text = DownloadRowName.Text = running.Count > 1 ? $"{running.Count} downloads"
                                                     : shown.Name.Length > 0 ? shown.Name : "Downloading…";
            DownloadSize.Text = DownloadRowSize.Text = speed >= 1024 ? $"{Size(bytes)} · {Size((long)speed)}/s" : Size(bytes);
        }
        var visibility = _download != null ? Visibility.Visible : Visibility.Collapsed;
        if (DownloadRow.Visibility != visibility) DownloadRow.Visibility = visibility;
        ApplyState();
    }
}
