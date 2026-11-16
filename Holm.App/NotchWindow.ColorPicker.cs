using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The colour picker: the button in the open notch closes it, the screen is read once, the next click picks a
/// pixel, its hex code goes on the clipboard and the notch shows the colour for a moment (ColorPickerWindow).
/// </summary>
public sealed partial class NotchWindow
{
    const string SwatchGlyph = ""; // a filled circle
    const string EyedropperGlyph = "";

    NotchButton _colorPickerBtn = null!;
    IScreenPlatform _screen = null!;
    bool _picking;

    void InitColorPicker(IPlatformServices platform)
    {
        _screen = platform.Screen;
#if !HOLM_WINDOWS
        Holm.Platform.MacOS.MacScreenPlatform.DecodePng = DecodePng;
#endif
    }

    /// <summary>Not a page: it sits in the tool row after the mixer, as in the WPF app.</summary>
    void BuildColorPickerButton()
    {
        _colorPickerBtn = new NotchButton(new Avalonia.Controls.TextBlock { Text = EyedropperGlyph, FontFamily = IconFont, FontSize = 14, Foreground = Dim })
        {
            Width = 32, Height = 32, Margin = new Thickness(2, 0),
        };
        Avalonia.Controls.ToolTip.SetTip(_colorPickerBtn, "Pick a color from the screen (Esc cancels)");
        _colorPickerBtn.Click += () => _ = PickColorAsync();
        _toolBar.Children.Add(_colorPickerBtn);
    }

    async Task PickColorAsync()
    {
        if (_picking) return;
        _picking = true;
        try
        {
            // Close first, and give the notch a moment to shrink, so what was under it can be picked too.
            EndTyping();
            _hovered = false;
            ApplyState(animate: false);
            await Task.Delay(250);

            var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (screen == null) return;
            var bounds = screen.Bounds;
            var shot = await _screen.CaptureAsync(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            if (shot == null || shot.Width <= 0 || shot.Height <= 0)
            {
                ShowPeek(SwatchGlyph, "Can't pick colors right now", "", null);
                return;
            }
            var picker = new ColorPickerWindow(shot, bounds, screen.Scaling);
            var done = new TaskCompletionSource<(string? Hex, Color Color)>();
            picker.Done += (hex, color) => done.TrySetResult((hex, color));
            picker.Show();
            var (picked, swatch) = await done.Task;
            if (picked == null) return;
            bool copied = false;
            try
            {
                if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(picked); copied = true; }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Color picker clipboard: " + ex.GetType().Name); }
            ShowPeek(SwatchGlyph, copied ? picked : picked + " (clipboard busy)", "copied", Dim, seconds: 4,
                glyphBrush: new SolidColorBrush(swatch).ToImmutable());
        }
        finally { _picking = false; }
    }

    /// <summary>For macOS, whose screen arrives as a PNG: its pixels as BGRA.</summary>
    static ScreenImage? DecodePng(byte[] png)
    {
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(png));
            var size = bitmap.PixelSize;
            using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            var bgra = new byte[size.Width * size.Height * 4];
            using (var buffer = target.Lock())
            {
                bitmap.CopyPixels(buffer);
                for (int row = 0; row < size.Height; row++)
                    Marshal.Copy(buffer.Address + row * buffer.RowBytes, bgra, row * size.Width * 4, size.Width * 4);
            }
            return new ScreenImage(size.Width, size.Height, bgra);
        }
        catch { return null; }
    }
}
