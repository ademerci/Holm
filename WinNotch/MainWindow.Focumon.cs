using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Focumon in the notch: your monster and the timer, in the closed bar and in the open top bar next to the sleep timer.
/// The numbers come from the Focumon browser extension (extension/focumon in the repository), which tells the page's
/// state to <see cref="FocumonLink"/> on this PC. A click on the open chip opens Focumon. The closed notch ticks by
/// itself between two messages.
/// </summary>
public partial class MainWindow
{
    static readonly HttpClient SpriteHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    const long MaxSpriteBytes = 512 * 1024;

    FocumonLink? _focumonLink;
    FocumonStatus? _focumon;
    string? _spriteUrl;
    ImageBrush? _spriteBrush;
    DispatcherTimer? _focumonTick;
    bool _focumonWasShowing;

    /// <summary>A live focus/break session the closed notch should show instead of weather.</summary>
    bool HasFocumon
    {
        get
        {
            if (!_settings.Focumon || _focumon is not { } status) return false;
            if (status.IsStale(DateTime.UtcNow)) { _focumon = null; return false; }
            return status.Phase is FocumonPhase.Focus or FocumonPhase.Break;
        }
    }

    void StartFocumon()
    {
        StopFocumon();
        if (!_settings.Focumon) return;
        _focumonLink = FocumonLink.TryStart();
        if (_focumonLink == null) return;
        _focumonLink.Updated += status => Dispatcher.InvokeAsync(() => OnFocumon(status));
        _focumonLink.Gone += () => Dispatcher.InvokeAsync(() => OnFocumon(null));
    }

    void StopFocumon()
    {
        _focumonLink?.Dispose();
        _focumonLink = null;
        _focumon = null;
        _focumonTick?.Stop();
        RefreshFocumon();
    }

    void OnFocumon(FocumonStatus? status)
    {
        if (!_settings.Focumon) return;
        _focumon = status;
        if (status?.ImageUrl is { } url)
        {
            if (url != _spriteUrl) _ = LoadSpriteAsync(url);
        }
        else
        {
            _spriteUrl = null;
            _spriteBrush = null;
        }
        RefreshFocumon();
    }

    /// <summary>The chip in the open bar and the closed Focumon / compact views; called every half second while live.</summary>
    void RefreshFocumon()
    {
        var now = DateTime.UtcNow;
        if (_focumon is { } stale && stale.IsStale(now)) _focumon = null;
        bool show = _settings.Focumon && _focumon is not null;
        if (show != _focumonWasShowing)
        {
            _focumonWasShowing = show;
            if (!_expanded) ApplyState();
        }

        if (!show)
        {
            FocumonChip.Visibility = Visibility.Collapsed;
            CompactFocumon.Visibility = Visibility.Collapsed;
            _focumonTick?.Stop();
            if (_info?.IsPlaying == true && _timer == null)
            {
                EqPanel.Visibility = Visibility.Visible;
                UpdateEqualizer();
            }
            return;
        }

        var status = _focumon!;
        bool known = _spriteBrush != null && status.ImageUrl == _spriteUrl;
        Brush sprite = known ? _spriteBrush! : (Brush)FindResource("ArtFallback");
        string initial = known || status.Monster.Length == 0 ? "" : status.Monster[..1].ToUpperInvariant();
        string name = status.Monster.Length > 0 ? status.Monster : "Focumon";
        string? time = status.Clock(now) is { } clock ? Focumon.Format(clock) : null;
        Brush timeBrush = status.Phase == FocumonPhase.Break ? TimerOrange : Brushes.White;

        PaintFocumon(FocumonChip, FocumonSprite, FocumonInitial, FocumonTime, sprite, initial, time, timeBrush, name, status, setHostVisible: true);
        PaintFocumon(null, CollapsedFocumonSprite, CollapsedFocumonInitial, CollapsedFocumonTime, sprite, initial, time, timeBrush, name, status, setHostVisible: false);
        PaintFocumon(CompactFocumon, CompactFocumonSprite, CompactFocumonInitial, CompactFocumonTime, sprite, initial, time, timeBrush, name, status, setHostVisible: false);

        // Compact: Focumon yields to a Holm timer; otherwise it takes the equalizer's place.
        bool compact = _info?.IsPlaying == true && _timer == null && CompactTimer.Visibility != Visibility.Visible;
        CompactFocumon.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (compact)
        {
            EqPanel.Visibility = Visibility.Collapsed;
            UpdateEqualizer();
        }

        _focumonTick ??= MakeTimer(500, RefreshFocumon);
        if (!_expanded && !_focumonTick.IsEnabled) _focumonTick.Start();
        if (_expanded) _focumonTick.Stop(); // UpdateLive already ticks while open
    }

    void PaintFocumon(FrameworkElement? host, Border spriteHost, TextBlock initial, TextBlock timeLabel,
        Brush sprite, string letter, string? time, Brush timeBrush, string name, FocumonStatus status, bool setHostVisible)
    {
        if (setHostVisible && host != null) host.Visibility = Visibility.Visible;
        spriteHost.Background = sprite;
        initial.Text = letter;
        if (time != null)
        {
            timeLabel.Visibility = Visibility.Visible;
            if (timeLabel.Text != time) timeLabel.Text = time;
            timeLabel.Foreground = timeBrush;
        }
        else
        {
            timeLabel.Visibility = Visibility.Collapsed;
        }
        if (host != null)
        {
            host.ToolTip = time == null
                ? $"{name} – click to open Focumon"
                : $"{name}: {(status.Phase == FocumonPhase.Break ? "break" : status.CountsDown ? "focusing" : "focused for")} – click to open Focumon";
        }
    }

    /// <summary>The monster's picture, from Focumon's own server only; a failure just leaves the first letter.</summary>
    async Task LoadSpriteAsync(string url)
    {
        _spriteUrl = url;
        _spriteBrush = null;
        try
        {
            using var response = await SpriteHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxSpriteBytes) return;
            // Redirects must stay on Focumon too.
            if (Focumon.SafeImage(response.RequestMessage?.RequestUri?.AbsoluteUri) == null) return;
            await using var body = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer)) > 0)
            {
                if (memory.Length + read > MaxSpriteBytes) return;
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 96;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            var brush = new ImageBrush(image) { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor); // pixel art stays crisp
            brush.Freeze();
            if (_spriteUrl != url) return; // another monster meanwhile
            _spriteBrush = brush;
            RefreshFocumon();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            Diag.Log("Focumon picture: " + ex.GetType().Name);
        }
    }

    void FocumonChip_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SetExpanded(false);
        if (!TryOpen(Focumon.AppUrl)) ShowPeek("", "Can't open Focumon", "", null, false, null);
    }

    void MenuFocumon_Click(object sender, RoutedEventArgs e)
    {
        _settings.Focumon = MenuFocumon.IsChecked;
        _settings.Save();
        if (_settings.Focumon) StartFocumon();
        else StopFocumon();
    }
}
