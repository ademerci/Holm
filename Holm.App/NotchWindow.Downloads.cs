using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;
using WinNotch.Notify;

namespace Holm.App;

/// <summary>
/// Downloads (the WPF app's MainWindow.Downloads.cs): while a browser saves a file into Downloads, the notch shows
/// its name, size and speed; when it's done, it offers to open the file or show it in its folder. The watcher is
/// the WPF app's own (linked); only the folder and the file manager are the system's.
/// </summary>
public sealed partial class NotchWindow
{
    const string DownloadGlyph = "", CheckGlyph = "";
    static readonly IBrush Green = Brush("#FF30D158");

    readonly Grid _downloadView = new() { Margin = new Thickness(15, 0, 16, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
    readonly TextBlock _downloadName = DownloadNameText(), _downloadSize = DownloadSizeText();
    readonly Grid _downloadRow = new() { Margin = new Thickness(2, 12, 2, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), IsVisible = false };
    readonly TextBlock _downloadRowName = DownloadNameText(), _downloadRowSize = DownloadSizeText();
    readonly DispatcherTimer _downloadPeekTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    IShellPlatform _shell = null!;
    DownloadWatcher? _downloads;
    Download? _download; // the one shown: the newest that's still running
    bool _downloadPeek;  // just started: shown for a moment even over music

    static TextBlock DownloadNameText() => new()
    {
        Margin = new Thickness(10, 0, 10, 0), FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static TextBlock DownloadSizeText() => new() { FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };

    static TextBlock DownloadIcon() => new() { Text = DownloadGlyph, FontFamily = IconFont, FontSize = 14, Foreground = ShareBlue, VerticalAlignment = VerticalAlignment.Center };

    void InitDownloads(IPlatformServices platform)
    {
        _shell = platform.Shell;
        _downloadPeekTimer.Tick += (_, _) => { _downloadPeekTimer.Stop(); _downloadPeek = false; ApplyState(animate: true); };
        foreach (var (grid, name, size) in new[] { (_downloadView, _downloadName, _downloadSize), (_downloadRow, _downloadRowName, _downloadRowSize) })
        {
            grid.Children.Add(DownloadIcon());
            Grid.SetColumn(name, 1);
            Grid.SetColumn(size, 2);
            grid.Children.Add(name);
            grid.Children.Add(size);
        }
    }

    void StartDownloads()
    {
        if (!_settings.Downloads || _downloads != null) return;
        try
        {
            string folder = _shell.DownloadsFolder();
            if (!Directory.Exists(folder)) return;
            var watcher = new DownloadWatcher(folder);
            // Events come on a worker thread; a watcher switched off meanwhile is ignored.
            watcher.Started += d => Dispatcher.UIThread.Post(() => { if (_downloads == watcher) OnDownloadStarted(d); });
            watcher.Updated += _ => Dispatcher.UIThread.Post(() => { if (_downloads == watcher) RefreshDownload(); });
            watcher.Finished += (d, file) => Dispatcher.UIThread.Post(() => { if (_downloads == watcher) OnDownloadFinished(d, file); });
            watcher.Cancelled += d => Dispatcher.UIThread.Post(() => { if (_downloads == watcher) OnDownloadGone(d); });
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

    void SetDownloads(bool on)
    {
        _settings.Downloads = on;
        _settings.Save();
        if (on) StartDownloads(); else StopDownloads();
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
        string name = Path.GetFileName(file);
        // A card already showing keeps its place; a short note will do then.
        if (_taskVisible)
        {
            ShowPeek(DownloadGlyph, "Downloaded " + name, "", null);
            return;
        }
        if (SecurityGuards.IsDangerousOpenExtension(file))
        {
            ShowTask(CheckGlyph, Green, "Downloaded", $"{name} · {SizeText(download.Bytes)}", null,
                     ("Show", () => { HideTask(); Try(() => _shell.Reveal(file)); }),
                     ("OK", HideTask));
        }
        else
        {
            ShowTask(CheckGlyph, Green, "Downloaded", $"{name} · {SizeText(download.Bytes)}", null,
                     ("Open", () => { HideTask(); Try(() => _shell.Open(file)); }),
                     ("Show", () => { HideTask(); Try(() => _shell.Reveal(file)); }));
        }
        _taskHide.Interval = TimeSpan.FromSeconds(6);
        _taskHide.Start();
    }

    static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { Diag.Error(ex); }
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
            _downloadName.Text = _downloadRowName.Text = running.Count > 1 ? $"{running.Count} downloads"
                                                       : shown.Name.Length > 0 ? shown.Name : "Downloading…";
            _downloadSize.Text = _downloadRowSize.Text = speed >= 1024 ? $"{SizeText(bytes)} · {SizeText((long)speed)}/s" : SizeText(bytes);
        }
        _downloadRow.IsVisible = _download != null;
        ApplyState(animate: true);
    }

    /// <summary>"12.4 MB", as the WPF app writes sizes.</summary>
    internal static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
