using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A friend's stream (the WPF app's StreamViewerWindow): their screen in a dark window with a LIVE badge. Double-click
/// or F11 for full screen, Esc or the cross to stop watching (closing tells the streamer, whose notch stops sending to you).
/// </summary>
internal sealed class StreamViewerWindow : Window
{
    /// <summary>A picture bigger than this on a side isn't shown: no screen is.</summary>
    const int MaxSide = 8192;

    readonly Image _picture = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _state, _rate;
    readonly CancellationTokenSource _closed = new();
    int _frames, _decoding;
    DateTime _rateSince = DateTime.UtcNow;
    Bitmap? _shown;

    /// <summary>Fires when the window closes: stop watching.</summary>
    public CancellationToken Stopped => _closed.Token;

    public bool GotFrame { get; private set; }

    public StreamViewerWindow(string friend, (string Text, IBrush Fill) face)
    {
        Title = $"{friend}'s screen";
        Width = 1100;
        Height = 700;
        MinWidth = 420;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12));
        Foreground = Brushes.White;
        // A thin frame that still resizes like any window.
        WindowDecorations = WindowDecorations.BorderOnly;
        CanResize = true;

        var bar = new Grid { Height = 44, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1A)), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(HubTheme.Face(face.Text, face.Fill, 26));
        who.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
        who.Children.Add(new Border
        {
            Background = HubTheme.Red, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "LIVE", FontSize = 11, FontWeight = FontWeight.Bold },
        });
        _rate = new TextBlock { Foreground = HubTheme.Faint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        who.Children.Add(_rate);
        bar.Children.Add(who);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        buttons.Children.Add(BarButton("", "Full screen (double-click)", ToggleFullScreen));
        buttons.Children.Add(BarButton("", "Stop watching (Esc)", Close));
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        // The bar moves the window, as a title bar does.
        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleFullScreen();
            else if (WindowState == WindowState.Normal) BeginMoveDrag(e);
        };

        _state = new TextBlock
        {
            Text = $"Connecting to {friend}…", Foreground = HubTheme.Dim, FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapInterpolationMode(_picture, BitmapInterpolationMode.HighQuality);
        var stage = new Grid { Background = Brushes.Transparent };
        stage.Children.Add(_state);
        stage.Children.Add(_picture);
        stage.DoubleTapped += (_, _) => ToggleFullScreen();

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(stage);
        Content = layout;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { if (WindowState == WindowState.FullScreen) ToggleFullScreen(); else Close(); }
            else if (e.Key == Key.F11) ToggleFullScreen();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _closed.Cancel();
            _picture.Source = null;
            _shown?.Dispose();
        };
    }

    static Border BarButton(string glyph, string tip, Action click)
    {
        var button = new Border
        {
            Width = 34, Height = 30, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = HubTheme.Glyph(glyph, 12), Margin = new Thickness(2, 0, 0, 0),
        };
        ToolTip.SetTip(button, tip);
        button.PointerEntered += (_, _) => button.Background = HubTheme.Card;
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) => e.Handled = true; // not a drag of the bar
        button.PointerReleased += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    void ToggleFullScreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    /// <summary>A frame from the stream (any thread). Decoded off the UI thread; one at a time, a busy decoder skips frames.</summary>
    public void ShowFrame(byte[] jpeg)
    {
        if (Interlocked.CompareExchange(ref _decoding, 1, 0) != 0) return;
        try
        {
            var frame = SafeBitmap.SafeBitmapDecode(jpeg, MaxSide);
            if (frame == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed.IsCancellationRequested) { frame.Dispose(); return; }
                var old = _shown;
                _picture.Source = _shown = frame;
                old?.Dispose();
                _state.IsVisible = false;
                GotFrame = true;
                _frames++;
                var span = DateTime.UtcNow - _rateSince;
                if (span >= TimeSpan.FromSeconds(2))
                {
                    _rate.Text = $"{_frames / span.TotalSeconds:0} fps · {frame.PixelSize.Width}×{frame.PixelSize.Height}";
                    _frames = 0;
                    _rateSince = DateTime.UtcNow;
                }
            });
        }
        catch (Exception ex) when (ex is OutOfMemoryException or NotSupportedException or ArgumentException or IOException or InvalidOperationException or NullReferenceException)
        {
            Diag.Log("Stream frame unreadable: " + ex.Message);
        }
        finally { Interlocked.Exchange(ref _decoding, 0); }
    }

    /// <summary>The stream ended: say so over the last picture.</summary>
    public void Ended(string why) => Dispatcher.UIThread.Post(() =>
    {
        _state.Text = why;
        _state.IsVisible = true;
        _picture.Opacity = 0.25;
        _rate.Text = "";
    });
}

/// <summary>The red LIVE badge under the notch while streaming, with who's watching and Stop. Never in the stream itself.</summary>
internal sealed class StreamBadge : Window
{
    readonly TextBlock _who = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };

    public StreamBadge(Action stop)
    {
        Title = "Holm LIVE";
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Foreground = Brushes.White;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Background = HubTheme.Red, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "● LIVE", FontSize = 11, FontWeight = FontWeight.Bold },
        });
        row.Children.Add(_who);
        var stopButton = new Border
        {
            Background = HubTheme.Selected, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "Stop", FontSize = 12, FontWeight = FontWeight.SemiBold },
        };
        stopButton.PointerReleased += (_, _) => stop();
        row.Children.Add(stopButton);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x14, 0x16)), CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x45, 0x3A)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 5, 6, 5), Child = row,
        };
    }

    public void ShowViewers(IReadOnlyList<string> names)
    {
        _who.Text = names.Count switch
        {
            0 => "Waiting for viewers…",
            1 => $"{names[0]} is watching",
            _ => $"{names.Count} watching",
        };
        ToolTip.SetTip(this, string.Join(", ", names));
    }
}

/// <summary>
/// Bounded bitmap decoding: checks encoded image dimensions via <see cref="SKCodec"/> before allocating pixel buffers,
/// rejecting pixel bombs and handling <see cref="OutOfMemoryException"/> safely.
/// </summary>
internal static class SafeBitmap
{
    /// <summary>Allows full 8K (7680×4320 = 33.18 MP) while capping RGBA allocation around ~134 MB.</summary>
    public const long DefaultMaxPixels = 33_554_432;

    public static Bitmap? SafeBitmapDecode(byte[] bytes, int maxSide, long maxPixels = DefaultMaxPixels)
    {
        if (bytes == null || bytes.Length == 0 || maxSide <= 0 || maxPixels <= 0) return null;
        try
        {
            using (var header = new MemoryStream(bytes, writable: false))
            using (var codec = SKCodec.Create(header))
            {
                if (codec == null) return null;
                var info = codec.Info;
                if (info.Width <= 0 || info.Height <= 0 || info.Width > maxSide || info.Height > maxSide) return null;
                if ((long)info.Width * info.Height > maxPixels) return null;
            }
            using var ms = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(ms);
            if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0
                || bitmap.PixelSize.Width > maxSide || bitmap.PixelSize.Height > maxSide
                || (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > maxPixels)
            {
                bitmap.Dispose();
                return null;
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or NotSupportedException or ArgumentException or IOException or InvalidOperationException or NullReferenceException)
        {
            Diag.Log("Image decode rejected: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }
}
