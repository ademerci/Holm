using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace WinNotch;

/// <summary>
/// A friend's stream: their screen in a dark window with a LIVE badge. Double-click for full screen, Esc or the cross
/// to stop watching (closing tells the streamer, whose notch stops sending to you).
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
    WindowState _before = WindowState.Normal;

    /// <summary>Fires when the window closes: stop watching.</summary>
    public CancellationToken Stopped => _closed.Token;

    public bool GotFrame { get; private set; }

    public StreamViewerWindow(string friend, (string Text, Brush Fill) face)
    {
        Title = $"{friend}'s screen";
        Width = 1100;
        Height = 700;
        MinWidth = 420;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = HubTheme.Frozen(Color.FromRgb(0x10, 0x10, 0x12));
        Foreground = Brushes.White;
        FontFamily = HubTheme.Text;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        // A thin frame that still resizes and snaps like any window.
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });

        var bar = new Grid { Height = 44, Background = HubTheme.Frozen(Color.FromRgb(0x18, 0x18, 0x1A)) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(HubTheme.Face(face.Text, face.Fill, 26));
        who.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
        var live = new Border
        {
            Background = HubTheme.Red, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "LIVE", FontSize = 11, FontWeight = FontWeights.Bold },
        };
        who.Children.Add(live);
        _rate = new TextBlock { Foreground = HubTheme.Faint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        who.Children.Add(_rate);
        bar.Children.Add(who);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        buttons.Children.Add(BarButton("", "Full screen (double-click)", ToggleFullScreen));
        buttons.Children.Add(BarButton("", "Stop watching (Esc)", Close));
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);

        _state = new TextBlock
        {
            Text = $"Connecting to {friend}…", Foreground = HubTheme.Dim, FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var stage = new Grid();
        stage.Children.Add(_state);
        stage.Children.Add(_picture);
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(stage);
        Content = layout;

        stage.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleFullScreen(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None && Topmost) ToggleFullScreen(); else Close(); }
            else if (e.Key == Key.F11) ToggleFullScreen();
        };
        Closed += (_, _) => _closed.Cancel();
    }

    Border BarButton(string glyph, string tip, Action click)
    {
        var button = new Border
        {
            Width = 34, Height = 30, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = tip, Child = HubTheme.Glyph(glyph, 12), Margin = new Thickness(2, 0, 0, 0),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.MouseEnter += (_, _) => button.Background = HubTheme.Card;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, _) => click();
        return button;
    }

    void ToggleFullScreen()
    {
        if (Topmost)
        {
            Topmost = false;
            WindowState = _before;
        }
        else
        {
            _before = WindowState;
            Topmost = true;
            WindowState = WindowState.Normal; // so maximizing covers the taskbar too
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>A frame from the stream (any thread). Decoded off the UI thread; one at a time, a busy decoder skips frames.</summary>
    public void Show(byte[] jpeg)
    {
        if (Interlocked.CompareExchange(ref _decoding, 1, 0) != 0) return;
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > MaxSide || frame.PixelHeight > MaxSide) return;
            frame.Freeze();
            Dispatcher.InvokeAsync(() =>
            {
                _picture.Source = frame;
                _state.Visibility = Visibility.Collapsed;
                GotFrame = true;
                _frames++;
                var span = DateTime.UtcNow - _rateSince;
                if (span >= TimeSpan.FromSeconds(2))
                {
                    _rate.Text = $"{_frames / span.TotalSeconds:0} fps · {frame.PixelWidth}×{frame.PixelHeight}";
                    _frames = 0;
                    _rateSince = DateTime.UtcNow;
                }
            });
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException or InvalidOperationException)
        {
            Diag.Log("Stream frame unreadable: " + ex.Message);
        }
        finally { Interlocked.Exchange(ref _decoding, 0); }
    }

    /// <summary>The stream ended: say so over the last picture.</summary>
    public void Ended(string why)
    {
        _state.Text = why;
        _state.Visibility = Visibility.Visible;
        _picture.Opacity = 0.25;
        _rate.Text = "";
    }
}
