using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The clipboard history in the open notch: the last 20 texts and pictures you copied, a click puts one back.
/// What password managers mark as private (<see cref="ClipboardPrivacy"/>) is never read, let alone kept. The list
/// lives in memory, pictures in a temp folder emptied when Holm starts and quits; nothing of it is logged or sent.
/// </summary>
public partial class MainWindow
{
    const string ClipboardGlyph = "";
    const int MaxClipPixels = 40_000_000;
    static readonly string ClipFolder = Path.Combine(Path.GetTempPath(), "Holm", "Clipboard");

    readonly ClipboardHistory _clips = new();
    bool _clipsPaused;   // posing for screenshots: your clipboard must not end up in a picture
    uint _restoredSeq;   // the clipboard change we made ourselves by putting an entry back
    string? _restoredKey;

    void InitClipboardHistory() => ForgetClipFiles();

    /// <summary>What apps say about the content on the clipboard, asked without opening it.</summary>
    static ClipVerdict ClipboardVerdict()
    {
        bool Has(string format) => Native.IsClipboardFormatAvailable(Native.RegisterClipboardFormat(format));
        if (Has(ClipboardPrivacy.ExcludeFormat)) return ClipVerdict.Private; // without opening the clipboard at all
        bool? history = null;
        if (Has(ClipboardPrivacy.HistoryFormat))
        {
            history = false; // unreadable counts as "keep it out"
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.GetData(ClipboardPrivacy.HistoryFormat) is MemoryStream { Length: >= 4 } dword)
                        history = BitConverter.ToInt32(dword.ToArray(), 0) != 0;
                    break;
                }
                catch (COMException) { Thread.Sleep(30); }
            }
        }
        return ClipboardPrivacy.Judge(false, Has(ClipboardPrivacy.ViewerIgnoreFormat), history);
    }

    void OnClipboardForHistory()
    {
        if (!_settings.ClipboardHistory || _clipsPaused) return;
        uint seq = Native.GetClipboardSequenceNumber();
        // Let the app that copied finish with the clipboard first.
        Dispatcher.InvokeAsync(() => CaptureClip(seq), DispatcherPriority.Background);
    }

    void CaptureClip(uint seq)
    {
        if (!_settings.ClipboardHistory || _clipsPaused) return;
        if (seq == _restoredSeq && _restoredKey != null)
        {
            // Our own doing: the entry moves up, it isn't copied again (a picture would come back without its alpha).
            _clips.Bump(_restoredKey, DateTime.UtcNow);
            ShowClipRows();
            return;
        }
        if (ClipboardVerdict() != ClipVerdict.Keep) return;

        string? text = ReadClipboardText();
        if (text != null)
        {
            if (!ClipboardHistory.Worth(text)) return;
            string key = ClipboardHistory.TextKey(text);
            if (!_clips.Bump(key, DateTime.UtcNow))
                ForgetClips(_clips.Add(new ClipEntry { Key = key, Text = text, CopiedAt = DateTime.UtcNow }));
            ShowClipRows();
            return;
        }
        if (ReadClipboardImage() is { } image) _ = KeepImageAsync(image);
    }

    static BitmapSource? ReadClipboardImage()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return Clipboard.ContainsImage() ? Clipboard.GetImage() : null; }
            catch (COMException) { Thread.Sleep(30); } // another app has it open
        }
        return null;
    }

    /// <summary>Hashes the picture, writes it as a PNG and makes a small copy for the list, off the UI thread.</summary>
    async Task KeepImageAsync(BitmapSource image)
    {
        int width = image.PixelWidth, height = image.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > MaxClipPixels) return;
        var pixels = new byte[width * height * 4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, width * 4, 0);
        string key = ClipboardHistory.ImageKey(pixels, width, height);
        if (_clips.Bump(key, DateTime.UtcNow)) { ShowClipRows(); return; }

        ClipEntry entry;
        try
        {
            entry = await Task.Run(() =>
            {
                var full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                full.Freeze();
                Directory.CreateDirectory(ClipFolder);
                string path = Path.Combine(ClipFolder, Guid.NewGuid().ToString("N") + ".png");
                using (var file = File.Create(path))
                {
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(full));
                    png.Save(file);
                }
                double scale = Math.Min(1, 72.0 / Math.Max(width, height));
                var thumb = new WriteableBitmap(new TransformedBitmap(full, new ScaleTransform(scale, scale))); // a copy: the full picture can go
                thumb.Freeze();
                return new ClipEntry { Key = key, ImagePath = path, Width = width, Height = height, Thumb = thumb, CopiedAt = DateTime.UtcNow };
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Diag.Log("Clipboard picture: " + ex.GetType().Name); // never what it shows
            return;
        }
        if (!_settings.ClipboardHistory || _clipsPaused) { ForgetClips(new List<ClipEntry> { entry }); return; }
        if (_clips.Bump(key, DateTime.UtcNow)) ForgetClips(new List<ClipEntry> { entry }); // copied twice meanwhile
        else ForgetClips(_clips.Add(entry));
        ShowClipRows();
    }

    /// <summary>Puts an entry back on the clipboard, ready for Ctrl+V in your app.</summary>
    void RestoreClip(ClipEntry entry)
    {
        bool done = false;
        if (entry.Text != null) done = TrySetClipboard(entry.Text);
        else if (entry.ImagePath != null && File.Exists(entry.ImagePath))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; // the file stays deletable
                image.UriSource = new Uri(entry.ImagePath);
                image.EndInit();
                image.Freeze();
                for (int attempt = 0; attempt < 3 && !done; attempt++)
                {
                    try { Clipboard.SetImage(image); done = true; }
                    catch (COMException) { Thread.Sleep(30); }
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException) { Diag.Log("Clipboard picture: " + ex.GetType().Name); }
        }
        if (done)
        {
            _restoredSeq = Native.GetClipboardSequenceNumber();
            _restoredKey = entry.Key;
        }
        ClipHint.Text = done ? "Copied – paste it with Ctrl+V" : "Clipboard busy, try again";
    }

    /// <summary>Deletes the picture files of entries that left the list.</summary>
    static void ForgetClips(List<ClipEntry> gone)
    {
        foreach (var entry in gone)
            if (entry.ImagePath != null)
                try { File.Delete(entry.ImagePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void ForgetClipFiles()
    {
        try { if (Directory.Exists(ClipFolder)) Directory.Delete(ClipFolder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void ClearClips()
    {
        ForgetClips(_clips.Clear());
        ForgetClipFiles();
        ShowClipRows();
    }

    // ───────────── the page ─────────────

    void ShowClipboardPage()
    {
        ClipHint.Text = "";
        ShowClipRows();
    }

    void ShowClipRows()
    {
        if (_tool != Tool.Clipboard) return;
        ClipRows.Children.Clear();
        var now = DateTime.UtcNow;
        foreach (var entry in _clips.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (entry.Thumb is ImageSource thumb)
            {
                var picture = new StackPanel { Orientation = Orientation.Horizontal };
                picture.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), ClipToBounds = true, Height = 28, MaxWidth = 64, HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = thumb, Stretch = Stretch.Uniform, Height = 28 },
                });
                picture.Children.Add(new TextBlock { Text = $"Picture · {entry.Width} × {entry.Height}", FontSize = 12, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
                content.Children.Add(picture);
            }
            else
                content.Children.Add(new TextBlock { Text = ClipboardHistory.Preview(entry.Text ?? ""), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var when = new TextBlock { Text = ClipboardHistory.Ago(entry.CopiedAt, now), FontSize = 11, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(when, 1);
            content.Children.Add(when);

            var pick = new Button { Style = (Style)FindResource("RowButton"), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Copy again" };
            pick.Click += (_, _) => RestoreClip(entry);
            row.Children.Add(pick);

            var remove = new Button { Style = (Style)FindResource("NotchButton"), Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove" };
            remove.Content = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, Foreground = DimBrush };
            string key = entry.Key;
            remove.Click += (_, _) =>
            {
                if (_clips.Remove(key) is { } gone) ForgetClips(new List<ClipEntry> { gone });
                ShowClipRows();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            if (entry.Text is { } text && _settings.Beam && _settings.BeamText)
            {
                // Beam it to a friend, someone nearby or your phone.
                var beam = new Button { Style = (Style)FindResource("NotchButton"), Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Beam this text" };
                beam.Content = new TextBlock { Text = "\uE724", FontFamily = IconFont, FontSize = 10, Foreground = DimBrush };
                beam.Click += (_, _) => BeamText(text);
                Grid.SetColumn(beam, 1);
                row.Children.Add(beam);
            }
            ClipRows.Children.Add(row);
        }
        ClipEmpty.Visibility = _clips.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipClearBtn.Visibility = _clips.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClipClear_Click(object sender, RoutedEventArgs e) => ClearClips();

    void ClipboardTool_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Clipboard);

    void MenuClipboardHistory_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClipboardHistory = MenuClipboardHistory.IsChecked;
        _settings.Save();
        if (!_settings.ClipboardHistory) ClearClips();
        RefreshToolBar();
    }
}
