using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// After a screenshot, the notch shows it small with "Send" and "Shelf"; click the picture to open it (the WPF app's
/// MainWindow.Screenshot.cs). The system's screenshots folder is watched, and on Windows a snip that only went to the
/// clipboard is kept as a copy for two days; nothing is polled.
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
            if (_taskGif is { } gif)
            {
                HideTask();
                OpenChat(gif.Friend);
                return;
            }
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

    // ───────────── screenshots that only went to the clipboard ─────────────

    /// <summary>What owns the clipboard right after a snip: Windows 11's Snipping Tool, Windows 10's snipping overlay.</summary>
    static readonly string[] SnippingTools = { "SnippingTool", "ScreenClippingHost", "ScreenSketch" };
    DispatcherTimer? _clipShot;

    /// <summary>Only pictures the snipping tools put there: an image copied in a browser is none of our business.</summary>
    void OnClipboardForScreenshot()
    {
        if (!_settings.Screenshots || _clipboardPlatform.OwnerProcess() is not { } owner) return;
        if (!SnippingTools.Contains(Path.GetFileNameWithoutExtension(owner), StringComparer.OrdinalIgnoreCase)) return;
        // The Snipping Tool usually saves the picture too: give that a moment before keeping a copy of the clipboard.
        if (_clipShot == null)
        {
            _clipShot = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _clipShot.Tick += (_, _) => { _clipShot.Stop(); _ = SaveClipboardShotAsync(); };
        }
        _clipShot.Stop();
        _clipShot.Start();
    }

    async Task SaveClipboardShotAsync()
    {
        if (DateTime.UtcNow - _lastShotAt < SameShot || Clipboard is not { } clipboard) return; // it was saved and has shown already
        Bitmap? image = null;
        for (int attempt = 0; attempt < 3 && image == null; attempt++)
        {
            try { image = await clipboard.TryGetBitmapAsync(); if (image == null) return; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { await Task.Delay(30); } // another app has the clipboard open
        }
        if (image == null) return;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "Holm", "Screenshots");
            Directory.CreateDirectory(folder);
            ForgetOldShots(folder);
            string path = Path.Combine(folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            using (var file = File.Create(path)) image.Save(file, new PngBitmapEncoderOptions());
            ShowScreenshot(path, saved: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Diag.Log("Screenshot copy: " + ex.Message);
        }
        finally { image.Dispose(); }
    }

    /// <summary>Copies of clipboard screenshots go after two days, unless they're on the shelf.</summary>
    void ForgetOldShots(string folder)
    {
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("Screenshot *.png"))
        {
            if (DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromDays(2)) continue;
            if (_settings.Shelf.Contains(file.FullName, StringComparer.OrdinalIgnoreCase)) continue;
            try { file.Delete(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    void StopScreenshots()
    {
        _clipShot?.Stop();
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

    void ShowScreenshot(string path, bool saved = true)
    {
        if (DateTime.UtcNow - _lastShotAt < SameShot)
        {
            if (saved && _shotPath != null) _shotPath = path; // the same picture, saved after all: use the real file
            return;
        }
        if (!_settings.Screenshots || _busy || _sharing || _taskVisible || _hiddenForFullscreen || _installingUpdate || PhoneShowing) return;
        if (Thumbnail(path) is not { } thumbnail) return;
        _lastShotAt = DateTime.UtcNow;
        _shotPath = path;
        Achieve(AchievementBook.Screenshots);
        Diag.Log("Screenshot taken");
        ShowTask(ScreenshotGlyph, ShareBlue, "Screenshot", saved ? "Saved in " + Path.GetFileName(Path.GetDirectoryName(path)) : "Copied – a copy is kept for two days", null,
                 ("Send", () => { if (_shotPath is { } shot) PickTarget(new[] { shot }); }),
                 ("Shelf", () => { HideTask(); if (_shotPath is { } shot) AddToShelf(new[] { shot }); }));
        _taskThumb.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
        _taskThumb.IsVisible = true;
        _taskIconCircle.IsVisible = false;
        if (_settings.ScreenshotText && _screen.CanReadText) ShowTaskExtra("Copy text", () => _ = CopyScreenshotTextAsync());
        _taskHide.Interval = TimeSpan.FromSeconds(6);
        _taskHide.Start();
    }

    /// <summary>Reads the text in the screenshot on this computer and puts it on the clipboard. The text is never logged.</summary>
    async Task CopyScreenshotTextAsync()
    {
        if (_shotPath is not { } path) return;
        _taskHide.Stop();
        _taskExtra.Label = "Reading…";
        _taskExtra.IsEnabled = false;
        string subtitle;
        try
        {
            string? text = await _screen.ReadTextAsync(path);
            int lines = text is { Length: > 0 } ? text.Split('\n').Length : 0;
            subtitle = text == null ? (OperatingSystem.IsWindows() ? "Windows can't read text: add a language with OCR" : "Install tesseract to read text from pictures")
                     : text.Length == 0 ? "No text found"
                     : await TrySetClipboardAsync(text) ? "Text copied · " + (lines == 1 ? "1 line" : $"{lines} lines")
                     : "Clipboard busy, try again";
        }
        catch (Exception ex)
        {
            Diag.Log("Screenshot text: " + ex.GetType().Name);
            subtitle = "Couldn't read the text";
        }
        if (!_taskVisible || _shotPath != path) return; // gone or another screenshot meanwhile
        _taskSubtitle.Text = subtitle;
        _taskSubtitle.IsVisible = true;
        _taskExtra.Label = subtitle.StartsWith("Text copied", StringComparison.Ordinal) ? "Copied" : "Copy text";
        _taskExtra.IsEnabled = !subtitle.StartsWith("Text copied", StringComparison.Ordinal);
        _taskHide.Interval = TimeSpan.FromSeconds(4);
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
        FollowClipboard(); // a snip that only goes to the clipboard is seen through it
    }
}
