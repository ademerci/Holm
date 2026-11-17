using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The clipboard history in the open notch: the last 20 texts and pictures you copied, a click puts one back.
/// What password managers mark as private is never read, let alone kept (<see cref="ClipboardFormats"/> knows the
/// markers of each system). The list lives in memory, pictures in a temp folder emptied when Holm starts and quits;
/// nothing of it is logged or sent. The WPF app's MainWindow.Clipboard.cs, on Avalonia's clipboard.
/// </summary>
public sealed partial class NotchWindow
{
    const string ClipboardGlyph = "";
    const int MaxClipPixels = 40_000_000;
    static readonly string ClipFolder = Path.Combine(Path.GetTempPath(), "Holm", "Clipboard");
    const string PasteKeys = "Ctrl+V";

    readonly ClipboardHistory _clips = new();
    readonly StackPanel _clipboardPage = new() { IsVisible = false };
    readonly StackPanel _clipRows = new();
    readonly TextBlock _clipEmpty = new()
    {
        Text = "Copy a text or a picture: the last 20 show up here. Passwords never do.",
        FontSize = 13, Foreground = Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 8),
    };
    readonly TextBlock _clipHint = new() { FontSize = 11, Foreground = Dim, Margin = new Thickness(4, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Dictionary<string, Bitmap> _clipThumbs = new();
    NotchButton _clipboardToolBtn = null!, _clipClearBtn = null!;
    IClipboardPlatform _clipboardPlatform = null!;
    /// <summary>The entry we just put back ourselves: the change that follows is ours, not a new copy.</summary>
    string? _restoringKey;
    bool _clipCapturePending;

    void InitClipboard(IPlatformServices platform)
    {
        ForgetClipFiles();
        _clipboardPlatform = platform.Clipboard;
        _clipboardPlatform.Changed += () => Dispatcher.UIThread.Post(OnClipboardChanged);
        _wipeTimer.Tick += (_, _) => WipePassword();
    }

    void StartClipboard()
    {
        FollowClipboard();
    }

    Control BuildClipboardPage()
    {
        var header = new Grid { Margin = new Thickness(2, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = "Clipboard", FontSize = 11, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
        _clipClearBtn = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 11, Foreground = Dim })
        {
            Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(_clipClearBtn, "Clear the list");
        _clipClearBtn.Click += ClearClips;
        header.Children.Add(_clipClearBtn);
        _clipboardPage.Children.Add(header);
        _clipboardPage.Children.Add(_clipEmpty);
        _clipboardPage.Children.Add(new ScrollViewer
        {
            MaxHeight = 236, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, Content = _clipRows,
        });
        _clipboardPage.Children.Add(_clipHint);
        return _clipboardPage;
    }

    void OnClipboardChanged()
    {
        uint sequence = ++_clipSequence;
        OnClipboardForScreenshot();
        if (_settings.ClearPasswords)
            DispatcherTimer.RunOnce(() => _ = CheckPasswordAsync(sequence), TimeSpan.FromMilliseconds(120));
        if (_settings.Beam) DispatcherTimer.RunOnce(() => _ = OnClipboardForBeamAsync(), TimeSpan.FromMilliseconds(150));
        if (!_settings.ClipboardHistory || _clipCapturePending) return;
        _clipCapturePending = true;
        // Let the app that copied finish with the clipboard first.
        DispatcherTimer.RunOnce(() => { _clipCapturePending = false; _ = CaptureClipAsync(); }, TimeSpan.FromMilliseconds(120));
    }

    async Task CaptureClipAsync()
    {
        if (!_settings.ClipboardHistory || Clipboard is not { } clipboard) return;
        if (_restoringKey is { } ours)
        {
            // Our own doing: the entry moves up, it isn't copied again (a picture would come back without its alpha).
            _restoringKey = null;
            _clips.Bump(ours, DateTime.UtcNow);
            ShowClipRows();
            return;
        }
        try
        {
            if (await PrivacyOfClipboardAsync(clipboard) != ClipPrivacy.Keep) return;

            if (await clipboard.TryGetTextAsync() is { } text)
            {
                if (!ClipboardHistory.Worth(text)) return;
                string key = ClipboardHistory.TextKey(text);
                if (!_clips.Bump(key, DateTime.UtcNow))
                    ForgetClips(_clips.Add(new ClipEntry { Key = key, Text = text, CopiedAt = DateTime.UtcNow }));
                ShowClipRows();
                return;
            }
            if (await clipboard.TryGetBitmapAsync() is { } image) await KeepImageAsync(image);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diag.Log("Clipboard: " + ex.GetType().Name); // never what it holds
        }
    }

    /// <summary>What the formats on the clipboard say, asked before anything else is read.</summary>
    static async Task<ClipPrivacy> PrivacyOfClipboardAsync(IClipboard clipboard)
    {
        var formats = await clipboard.GetDataFormatsAsync();
        var names = formats.Select(f => f.Identifier).ToList();
        bool? history = null;
        const string HistoryFormat = "CanIncludeInClipboardHistory";
        if (names.Contains(HistoryFormat))
        {
            history = false; // unreadable counts as "keep it out"
            var value = await clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(HistoryFormat));
            if (value is { Length: >= 4 }) history = BitConverter.ToInt32(value, 0) != 0;
        }
        return ClipboardFormats.Judge(names, history);
    }

    /// <summary>Hashes the picture, writes it as a PNG and makes a small copy for the list.</summary>
    async Task KeepImageAsync(Bitmap image)
    {
        int width = image.PixelSize.Width, height = image.PixelSize.Height;
        if (width <= 0 || height <= 0 || (long)width * height > MaxClipPixels) { image.Dispose(); return; }
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { handle.Free(); }
        string key = ClipboardHistory.ImageKey(pixels, width, height);
        if (_clips.Bump(key, DateTime.UtcNow)) { image.Dispose(); ShowClipRows(); return; }

        ClipEntry entry;
        Bitmap thumb;
        try
        {
            (entry, thumb) = await Task.Run(() =>
            {
                Directory.CreateDirectory(ClipFolder);
                string path = Path.Combine(ClipFolder, Guid.NewGuid().ToString("N") + ".png");
                using (var file = File.Create(path)) image.Save(file, new PngBitmapEncoderOptions());
                double scale = Math.Min(1, 72.0 / Math.Max(width, height));
                var small = image.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale))));
                return (new ClipEntry { Key = key, ImagePath = path, Width = width, Height = height, CopiedAt = DateTime.UtcNow }, small);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Diag.Log("Clipboard picture: " + ex.GetType().Name);
            return;
        }
        finally { image.Dispose(); }
        _clipThumbs[key] = thumb;
        if (!_settings.ClipboardHistory) { ForgetClips(new List<ClipEntry> { entry }); return; }
        if (_clips.Bump(key, DateTime.UtcNow)) ForgetClips(new List<ClipEntry> { entry }); // copied twice meanwhile
        else ForgetClips(_clips.Add(entry));
        ShowClipRows();
    }

    /// <summary>Puts an entry back on the clipboard, ready to paste in your app.</summary>
    async void RestoreClip(ClipEntry entry)
    {
        bool done = false;
        try
        {
            if (Clipboard is { } clipboard)
            {
                if (entry.Text != null)
                {
                    _restoringKey = entry.Key;
                    await clipboard.SetTextAsync(entry.Text);
                    done = true;
                }
                else if (entry.ImagePath != null && File.Exists(entry.ImagePath))
                {
                    using var image = new Bitmap(entry.ImagePath);
                    _restoringKey = entry.Key;
                    await clipboard.SetBitmapAsync(image);
                    await clipboard.FlushAsync(); // the picture stays on the clipboard once this one is gone
                    done = true;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diag.Log("Clipboard restore: " + ex.GetType().Name);
            _restoringKey = null;
        }
        _clipHint.Text = done ? $"Copied – paste it with {PasteKeys}" : "Clipboard busy, try again";
    }

    /// <summary>Deletes the picture files of entries that left the list.</summary>
    void ForgetClips(List<ClipEntry> gone)
    {
        foreach (var entry in gone)
        {
            if (_clipThumbs.Remove(entry.Key, out var thumb) && !_clips.Items.Any(e => e.Key == entry.Key)) thumb.Dispose();
            if (entry.ImagePath != null && !_clips.Items.Any(e => e.ImagePath == entry.ImagePath))
                try { File.Delete(entry.ImagePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static void ForgetClipFiles()
    {
        try { if (Directory.Exists(ClipFolder)) Directory.Delete(ClipFolder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void ClearClips()
    {
        ForgetClips(_clips.Clear());
        foreach (var thumb in _clipThumbs.Values) thumb.Dispose();
        _clipThumbs.Clear();
        ForgetClipFiles();
        ShowClipRows();
    }

    void ShowClipboardPage()
    {
        _clipHint.Text = "";
        ShowClipRows();
    }

    void ShowClipRows()
    {
        if (_tool != Tool.Clipboard) return;
        _clipRows.Children.Clear();
        var now = DateTime.UtcNow;
        foreach (var entry in _clips.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            if (_clipThumbs.TryGetValue(entry.Key, out var thumb))
            {
                var picture = new StackPanel { Orientation = Orientation.Horizontal };
                picture.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), ClipToBounds = true, Height = 28, MaxWidth = 64, HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = thumb, Stretch = Stretch.Uniform, Height = 28 },
                });
                picture.Children.Add(new TextBlock { Text = $"Picture · {entry.Width} × {entry.Height}", FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
                content.Children.Add(picture);
            }
            else
                content.Children.Add(new TextBlock { Text = ClipboardHistory.Preview(entry.Text ?? ""), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var when = new TextBlock { Text = ClipboardHistory.Ago(entry.CopiedAt, now), FontSize = 11, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(when, 1);
            content.Children.Add(when);

            var pick = NotchButton.Row(content);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(pick, "Copy again");
            pick.Click += () => RestoreClip(entry);
            row.Children.Add(pick);

            var remove = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, Foreground = Dim })
            {
                Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0),
            };
            ToolTip.SetTip(remove, "Remove");
            string key = entry.Key;
            remove.Click += () =>
            {
                if (_clips.Remove(key) is { } gone) ForgetClips(new List<ClipEntry> { gone });
                ShowClipRows();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            _clipRows.Children.Add(row);
        }
        _clipEmpty.IsVisible = _clips.Items.Count == 0;
        _clipClearBtn.IsVisible = _clips.Items.Count > 0;
        if (_state == NotchState.Expanded) ApplyState(animate: true);
    }

    /// <summary>The switch in the context menu: off forgets everything and stops listening.</summary>
    void SetClipboardHistory(bool on)
    {
        _settings.ClipboardHistory = on;
        _settings.Save();
        FollowClipboard();
        if (!on) ClearClips();
        RefreshToolBar();
    }
}
