using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// After a screenshot, the notch shows it small with "Send" and "Shelf"; click the picture to open it (the WPF app's
/// MainWindow.Screenshot.cs). The system's screenshots folder is watched; nothing is polled. Beam's "Send" and the
/// text reading (Windows OCR) are not ported.
/// </summary>
public sealed partial class NotchWindow
{
    const string ScreenshotGlyph = "";
    static readonly TimeSpan SameShot = TimeSpan.FromSeconds(4);

    readonly Border _taskThumb = new() { Width = 54, Height = 36, CornerRadius = new CornerRadius(6), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, IsVisible = false, Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true };
    readonly DispatcherTimer _shotSettle = new() { Interval = TimeSpan.FromMilliseconds(400) };
    FileSystemWatcher? _shots;
    string? _pendingShot, _shotPath;
    int _shotTries;
    DateTime _lastShotAt;

    void InitScreenshots()
    {
        ToolTip.SetTip(_taskThumb, "Open");
        _taskThumb.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (_shotPath is not { } path) return;
            HideTask();
            try { _shell.Open(path); }
            catch (Exception) { ShowPeek(ErrorGlyph, "Can't open the screenshot", "", null); }
        };
        _taskView.Children.Add(_taskThumb);
        // The tool writes the file in pieces: look once it has been quiet for a moment.
        _shotSettle.Tick += (_, _) => { _shotSettle.Stop(); CheckShot(); };
    }

    void StartScreenshots()
    {
        StopScreenshots();
        if (!_settings.Screenshots || _shell.ScreenshotsFolder() is not { } folder || !Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            watcher.Created += (_, e) => Dispatcher.UIThread.Post(() => SeenShot(e.FullPath));
            // Some tools write a hidden file first and rename it.
            watcher.Renamed += (_, e) => { if (e.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) Dispatcher.UIThread.Post(() => SeenShot(e.FullPath)); };
            watcher.EnableRaisingEvents = true;
            _shots = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            Diag.Log("Screenshots folder unavailable: " + ex.Message);
        }
    }

    void StopScreenshots()
    {
        _shots?.Dispose();
        _shots = null;
        _shotSettle.Stop();
        _pendingShot = null;
    }

    void SeenShot(string path)
    {
        if (Path.GetFileName(path).StartsWith('.')) return;
        _pendingShot = path;
        _shotTries = 0;
        _shotSettle.Stop();
        _shotSettle.Start();
    }

    void CheckShot()
    {
        if (_pendingShot is not { } path) return;
        if (!Readable(path))
        {
            if (++_shotTries < 10) _shotSettle.Start(); // still being written
            else _pendingShot = null;
            return;
        }
        _pendingShot = null;
        if (_shell.IsScreenshot(path)) ShowScreenshot(path);
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

    void ShowScreenshot(string path)
    {
        if (DateTime.UtcNow - _lastShotAt < SameShot || !_settings.Screenshots || _sharing || _taskVisible) return;
        if (Thumbnail(path) is not { } thumbnail) return;
        _lastShotAt = DateTime.UtcNow;
        _shotPath = path;
        Diag.Log("Screenshot taken");
        ShowTask(ScreenshotGlyph, ShareBlue, "Screenshot", "Saved in " + Path.GetFileName(Path.GetDirectoryName(path)), null,
                 ("Send", () => { if (_shotPath is { } shot) PickTarget(new[] { shot }); }),
                 ("Shelf", () => { HideTask(); if (_shotPath is { } shot) AddToShelf(new[] { shot }); }));
        _taskThumb.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
        _taskThumb.IsVisible = true;
        _taskIconCircle.IsVisible = false;
        _taskHide.Interval = TimeSpan.FromSeconds(6);
        _taskHide.Start();
    }

    /// <summary>Small: it's 36 pixels high in the notch.</summary>
    static Bitmap? Thumbnail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Bitmap.DecodeToHeight(stream, 80);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            Diag.Log("Screenshot preview: " + ex.Message);
            return null;
        }
    }

    void SetScreenshots(bool on)
    {
        _settings.Screenshots = on;
        _settings.Save();
        StartScreenshots();
    }
}
