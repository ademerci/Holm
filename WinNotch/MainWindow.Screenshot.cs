using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// After a screenshot, the notch shows it small with "Send" (to a friend, someone nearby, your phone or with a code –
/// click instead of drag) and "Shelf". Click the picture to open it.
/// </summary>
public partial class MainWindow
{
    /// <summary>What owns the clipboard right after a snip: Windows 11's Snipping Tool, Windows 10's snipping overlay.</summary>
    static readonly string[] SnippingTools = { "SnippingTool", "ScreenClippingHost", "ScreenSketch" };
    static readonly TimeSpan SameShot = TimeSpan.FromSeconds(4);

    ScreenshotWatcher? _shots;
    DispatcherTimer _clipShot = null!, _pickClose = null!;
    DateTime _lastShotAt;
    string? _shotPath;
    string[]? _pickFiles; // the share view was opened to pick where these go, by clicking

    void InitScreenshots()
    {
        // The Snipping Tool usually saves the picture too: give that a moment before keeping a copy of the clipboard.
        _clipShot = MakeTimer(1500, SaveClipboardShot, oneShot: true);
        _pickClose = MakeTimer(900, EndShare, oneShot: true);
    }

    void StartScreenshots()
    {
        StopScreenshots();
        if (!_settings.Screenshots || Native.ScreenshotsFolder() is not { } folder || !Directory.Exists(folder)) return;
        try
        {
            _shots = new ScreenshotWatcher(folder, Dispatcher);
            _shots.Taken += path => ShowScreenshot(path, saved: true);
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
        _clipShot.Stop();
    }

    /// <summary>Only pictures the snipping tools put there: an image copied in a browser is none of our business.</summary>
    void OnClipboardForScreenshot()
    {
        if (!_settings.Screenshots) return;
        string owner = Native.ProcessNameOf(Native.GetClipboardOwner());
        if (!SnippingTools.Contains(owner, StringComparer.OrdinalIgnoreCase)) return;
        _clipShot.Stop();
        _clipShot.Start();
    }

    void SaveClipboardShot()
    {
        if (DateTime.UtcNow - _lastShotAt < SameShot) return; // it was saved and has shown already
        BitmapSource? image = null;
        for (int attempt = 0; attempt < 3 && image == null; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsImage()) return;
                image = Clipboard.GetImage();
            }
            catch (COMException) { Thread.Sleep(30); } // another app has the clipboard open
        }
        if (image == null) return;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "Holm", "Screenshots");
            Directory.CreateDirectory(folder);
            ForgetOldShots(folder);
            string path = Path.Combine(folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            using (var file = File.Create(path))
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(image));
                png.Save(file);
            }
            ShowScreenshot(path, saved: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Diag.Log("Screenshot copy: " + ex.Message);
        }
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

    void ShowScreenshot(string path, bool saved)
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
        Diag.Log("Screenshot taken");
        ShowTask("", ShareBlue, "Screenshot", saved ? "Saved in Screenshots" : "Copied – a copy is kept for two days", null,
                 ("Send", () => { if (_shotPath is { } shot) PickTarget(new[] { shot }); }),
                 ("Shelf", () => { HideTask(); if (_shotPath is { } shot) AddToShelf(new[] { shot }); }));
        ShowTaskPicture(thumbnail);
        _taskHide.Interval = TimeSpan.FromSeconds(6);
        _taskHide.Start();
    }

    void ShowTaskPicture(ImageSource picture)
    {
        var brush = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        TaskThumb.Background = brush;
        TaskThumb.Visibility = Visibility.Visible;
        TaskIconCircle.Visibility = Visibility.Collapsed;
    }

    static BitmapSource? Thumbnail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 80; // small: it's 36 pixels high in the notch
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Diag.Log("Screenshot preview: " + ex.Message);
            return null;
        }
    }

    void TaskThumb_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_shotPath is not { } path) return;
        HideTask();
        if (!TryOpen(path)) ShowPeek("", "Can't open the screenshot", "", null, false, null);
    }

    /// <summary>The share view, to click where the files go instead of dropping them; it closes when the mouse leaves.</summary>
    void PickTarget(string[] files)
    {
        HideTask();
        _pickFiles = files;
        _dragFiles = files;
        _sharing = true;
        SetExpanded(false);
        RefreshShareTargets();
        ApplyState();
        if (!Island.IsMouseOver) _pickClose.Start();
        _ = RefreshPeersAsync();
        _friends.PingAll();
    }

    void MenuScreenshots_Click(object sender, RoutedEventArgs e)
    {
        _settings.Screenshots = MenuScreenshots.IsChecked;
        _settings.Save();
        StartScreenshots();
    }
}
