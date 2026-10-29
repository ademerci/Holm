using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch;

/// <summary>
/// Focumon in the open notch: your monster and the timer, in the top bar next to the sleep timer; a click opens Focumon.
/// The numbers come from the Focumon browser extension (extension/focumon in the repository), which tells the page's
/// state to <see cref="FocumonLink"/> on this PC. It only shows while the notch is open, never in the small one,
/// and it counts down by itself between two messages.
/// </summary>
public partial class MainWindow
{
    static readonly HttpClient SpriteHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    const long MaxSpriteBytes = 512 * 1024;

    FocumonLink? _focumonLink;
    FocumonStatus? _focumon;
    string? _spriteUrl;
    ImageBrush? _spriteBrush;

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
        RefreshFocumon();
    }

    void OnFocumon(FocumonStatus? status)
    {
        if (!_settings.Focumon) return;
        _focumon = status;
        RefreshFocumon();
        if (status?.ImageUrl is { } url && url != _spriteUrl) _ = LoadSpriteAsync(url);
    }

    /// <summary>The chip in the open notch's bar, as the page says it now; called every half second while open.</summary>
    void RefreshFocumon()
    {
        var now = DateTime.UtcNow;
        if (_focumon is { } stale && stale.IsStale(now)) _focumon = null;
        if (!_settings.Focumon || _focumon is not { } status)
        {
            FocumonChip.Visibility = Visibility.Collapsed;
            return;
        }

        FocumonChip.Visibility = Visibility.Visible;
        bool known = _spriteBrush != null && status.ImageUrl == _spriteUrl;
        FocumonSprite.Background = known ? _spriteBrush : (Brush)FindResource("ArtFallback");
        FocumonInitial.Text = known || status.Monster.Length == 0 ? "" : status.Monster[..1].ToUpperInvariant();

        string name = status.Monster.Length > 0 ? status.Monster : "Focumon";
        if (status.Clock(now) is { } clock)
        {
            FocumonTime.Visibility = Visibility.Visible;
            string time = Focumon.Format(clock);
            if (FocumonTime.Text != time) FocumonTime.Text = time;
            FocumonTime.Foreground = status.Phase == FocumonPhase.Break ? TimerOrange : Brushes.White;
            FocumonChip.ToolTip = $"{name}: {(status.Phase == FocumonPhase.Break ? "break" : status.CountsDown ? "focusing" : "focused for")} – click to open Focumon";
        }
        else
        {
            FocumonTime.Visibility = Visibility.Collapsed;
            FocumonChip.ToolTip = $"{name} – click to open Focumon";
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
