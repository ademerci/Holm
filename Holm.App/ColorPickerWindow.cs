using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Picks one colour from the screen, the same way on every system: the screen is read once, shown unchanged in a
/// window over it, and a magnifier beside the pointer shows the pixels around it. A click picks, Escape or a right
/// click cancels; the arrow keys move one pixel (ten with Shift) and Enter picks, for the pixel a hand can't hit.
/// What is picked is exactly what the screen showed when the picker opened.
/// </summary>
sealed class ColorPickerWindow : Window
{
    const int Zoom = 10;

    readonly ScreenImage _shot;
    readonly PixelRect _bounds;
    readonly WriteableBitmap _zoomed = new(new PixelSize(ColorPick.Span, ColorPick.Span), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    readonly Image _zoomImage;
    readonly Border _loupe;
    readonly Border _swatch = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) };
    readonly TextBlock _hex = new() { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Canvas _canvas = new();
    /// <summary>The pixel under the pointer, in the picture's pixels.</summary>
    int _x, _y;
    bool _done, _activated;

    /// <summary>The colour picked as "#RRGGBB" with its colour, or null when cancelled. Raised once.</summary>
    public event Action<string?, Color>? Done;

    public ColorPickerWindow(ScreenImage shot, PixelRect bounds, double scaling)
    {
        _shot = shot;
        _bounds = bounds;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = bounds.Position;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        if (OperatingSystem.IsLinux()) WindowState = WindowState.FullScreen;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        FontFamily = Fonts.Text;
        Fonts.ApplyRendering(this);

        var full = new WriteableBitmap(new PixelSize(shot.Width, shot.Height), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = full.Lock())
        {
            int rowBytes = shot.Width * 4;
            for (int y = 0; y < shot.Height; y++)
                Marshal.Copy(shot.Bgra, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }
        var screen = new Image { Source = full, Width = Width, Height = Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(screen, Avalonia.Media.Imaging.BitmapInterpolationMode.None);

        _zoomImage = new Image { Source = _zoomed, Width = ColorPick.Span * Zoom, Height = ColorPick.Span * Zoom };
        RenderOptions.SetBitmapInterpolationMode(_zoomImage, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        var center = new Border
        {
            Width = Zoom + 2, Height = Zoom + 2, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 0) };
        label.Children.Add(_swatch);
        label.Children.Add(_hex);
        var stack = new StackPanel();
        var zoomGrid = new Grid();
        zoomGrid.Children.Add(_zoomImage);
        zoomGrid.Children.Add(center);
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = zoomGrid });
        stack.Children.Add(label);
        _loupe = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x10, 0x12)), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8), Child = stack, IsHitTestVisible = false,
        };
        _canvas.Children.Add(screen);
        _canvas.Children.Add(_loupe);
        Content = _canvas;

        PointerMoved += (_, e) => MoveTo(e.GetPosition(this));
        PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) Finish(pick: false);
            else if (point.Properties.IsLeftButtonPressed) { MoveTo(point.Position); Finish(pick: true); }
        };
        KeyDown += OnKeyDown;
        Activated += (_, _) => _activated = true;
        Deactivated += (_, _) => { if (_activated) Finish(pick: false); };
        Opened += (_, _) =>
        {
            Activate();
            Focus();
            _x = shot.Width / 2;
            _y = shot.Height / 2;
            Redraw();
        };
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape) { Finish(pick: false); return; }
        if (e.Key is Key.Enter or Key.Return) { Finish(pick: true); return; }
        int n = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-n, 0), Key.Right => (n, 0), Key.Up => (0, -n), Key.Down => (0, n), _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return;
        _x = Math.Clamp(_x + dx, 0, _shot.Width - 1);
        _y = Math.Clamp(_y + dy, 0, _shot.Height - 1);
        Redraw();
    }

    void MoveTo(Point position)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        _x = Math.Clamp((int)(position.X / Bounds.Width * _shot.Width), 0, _shot.Width - 1);
        _y = Math.Clamp((int)(position.Y / Bounds.Height * _shot.Height), 0, _shot.Height - 1);
        Redraw();
    }

    void Redraw()
    {
        if (_done) return;
        using (var buffer = _zoomed.Lock())
        {
            var row = new byte[ColorPick.Span * 4];
            for (int dy = 0; dy < ColorPick.Span; dy++)
            {
                for (int dx = 0; dx < ColorPick.Span; dx++)
                {
                    int sx = _x - ColorPick.Reach + dx, sy = _y - ColorPick.Reach + dy;
                    int i = dx * 4;
                    if (sx < 0 || sy < 0 || sx >= _shot.Width || sy >= _shot.Height) { row[i] = row[i + 1] = row[i + 2] = 0; row[i + 3] = 0xFF; continue; }
                    Array.Copy(_shot.Bgra, (sy * _shot.Width + sx) * 4, row, i, 4);
                }
                Marshal.Copy(row, 0, buffer.Address + dy * buffer.RowBytes, row.Length);
            }
        }
        _zoomImage.InvalidateVisual();
        var (r, g, b) = Pixel();
        _swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        _hex.Text = ColorPick.Hex(r, g, b);

        // Beside the pointer, flipped to the other side near an edge (ColorPick.LoupeAt, in this window's units).
        _loupe.Measure(Size.Infinity);
        var size = _loupe.DesiredSize;
        double px = (_x + 0.5) / _shot.Width * Bounds.Width, py = (_y + 0.5) / _shot.Height * Bounds.Height;
        var area = new ColorPick.Area(0, 0, (int)Bounds.Width, (int)Bounds.Height);
        var (lx, ly) = ColorPick.LoupeAt((int)px, (int)py, (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), area);
        Canvas.SetLeft(_loupe, lx);
        Canvas.SetTop(_loupe, ly);
    }

    (byte R, byte G, byte B) Pixel() => ColorPick.At(_shot.Bgra, _shot.Width * 4, _x, _y);

    void Finish(bool pick)
    {
        if (_done) return;
        _done = true;
        var (r, g, b) = Pixel();
        Close();
        if (pick) Done?.Invoke(ColorPick.Hex(r, g, b), Color.FromRgb(r, g, b));
        else Done?.Invoke(null, default);
    }
}
