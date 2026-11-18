using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Focumon in the notch (the WPF app's MainWindow.Focumon.cs; Focumon.cs linked): your monster and the timer, in the
/// closed bar, beside the music and in the open top bar next to the sleep timer. The numbers come from the Focumon
/// browser extension (extension/focumon), which tells the page's state to <see cref="FocumonLink"/> on this computer.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly HttpClient SpriteHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    const long MaxSpriteBytes = 512 * 1024;

    FocumonLink? _focumonLink;
    FocumonStatus? _focumon;
    string? _spriteUrl;
    IBrush? _spriteBrush;
    readonly DispatcherTimer _focumonTick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _focumonWasShowing;

    // The chip in the open bar, the closed view and the one beside the music.
    NotchButton _focumonChip = null!;
    readonly (Border Sprite, TextBlock Initial, TextBlock Time) _chipParts = FocumonParts(22, 7, 11, 12, 7);
    readonly Grid _focumonView = new() { Margin = new Thickness(10, 0, 14, 0) };
    readonly (Border Sprite, TextBlock Initial, TextBlock Time) _closedParts = FocumonParts(20, 6, 11, 13, 0);
    readonly StackPanel _compactFocumon = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(0, 0, 2, 0) };
    readonly (Border Sprite, TextBlock Initial, TextBlock Time) _compactParts = FocumonParts(18, 5, 10, 13, 6);

    static (Border, TextBlock, TextBlock) FocumonParts(double size, double radius, double initialSize, double timeSize, double gap)
    {
        var initial = new TextBlock { FontSize = initialSize, FontWeight = FontWeight.SemiBold, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var sprite = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = ArtFallback, VerticalAlignment = VerticalAlignment.Center, Child = initial };
        RenderOptions.SetBitmapInterpolationMode(sprite, BitmapInterpolationMode.None); // pixel art stays crisp
        var time = new TextBlock { FontFamily = DisplayFont, FontSize = timeSize, FontWeight = FontWeight.SemiBold, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        return (sprite, initial, time);
    }

    void BuildFocumon()
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 10, 0) };
        chip.Children.Add(_chipParts.Sprite);
        chip.Children.Add(_chipParts.Time);
        _focumonChip = new NotchButton(chip) { Height = 30, Margin = new Thickness(0, 0, 2, 0), IsVisible = false };
        _focumonChip.Click += () =>
        {
            SetExpanded(false);
            try { _shell.Open(Focumon.AppUrl); } catch { ShowPeek("", "Can't open Focumon", "", null); }
        };
        // Beside the sleep timer, as in the WPF app's top bar.
        if (_sleepBtn.Parent is Control moon && moon.Parent is Panel right) right.Children.Insert(right.Children.IndexOf(moon), _focumonChip);

        _closedParts.Sprite.HorizontalAlignment = HorizontalAlignment.Left;
        _closedParts.Time.HorizontalAlignment = HorizontalAlignment.Right;
        _focumonView.Children.Add(_closedParts.Sprite);
        _focumonView.Children.Add(_closedParts.Time);
        _focumonView.Opacity = 0;
        _focumonView.IsHitTestVisible = false;
        _islandContent.Children.Add(_focumonView);

        _compactFocumon.Children.Add(_compactParts.Sprite);
        _compactFocumon.Children.Add(_compactParts.Time);
        if (_eqPanel.Parent is Panel compactRight) compactRight.Children.Insert(compactRight.Children.IndexOf(_eqPanel), _compactFocumon);
        _focumonTick.Tick += (_, _) => RefreshFocumon();
    }

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
        _focumonLink.Updated += status => Dispatcher.UIThread.Post(() => OnFocumon(status));
        _focumonLink.Gone += () => Dispatcher.UIThread.Post(() => OnFocumon(null));
    }

    void StopFocumon()
    {
        _focumonLink?.Dispose();
        _focumonLink = null;
        _focumon = null;
        _focumonTick.Stop();
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

    /// <summary>The chip, the closed view and the one beside the music; every half second while live.</summary>
    void RefreshFocumon()
    {
        var now = DateTime.UtcNow;
        if (_focumon is { } stale && stale.IsStale(now)) _focumon = null;
        bool show = _settings.Focumon && _focumon is not null;
        if (show != _focumonWasShowing)
        {
            _focumonWasShowing = show;
            if (!_hovered) ApplyState(animate: true);
        }
        if (!show)
        {
            _focumonChip.IsVisible = false;
            _compactFocumon.IsVisible = false;
            _focumonTick.Stop();
            _eqPanel.IsVisible = true;
            return;
        }

        var status = _focumon!;
        bool known = _spriteBrush != null && status.ImageUrl == _spriteUrl;
        IBrush sprite = known ? _spriteBrush! : ArtFallback;
        string initial = known || status.Monster.Length == 0 ? "" : status.Monster[..1].ToUpperInvariant();
        string name = status.Monster.Length > 0 ? status.Monster : "Focumon";
        string? time = status.Clock(now) is { } clock ? Focumon.Format(clock) : null;
        IBrush timeBrush = status.Phase == FocumonPhase.Break ? TimerOrange : Look.Ink;
        foreach (var parts in new[] { _chipParts, _closedParts, _compactParts })
        {
            parts.Sprite.Background = sprite;
            parts.Initial.Text = initial;
            parts.Time.IsVisible = time != null;
            if (time != null && parts.Time.Text != time) parts.Time.Text = time;
            parts.Time.Foreground = timeBrush;
        }
        _focumonChip.IsVisible = true;
        ToolTip.SetTip(_focumonChip, time == null ? $"{name} – click to open Focumon"
            : $"{name}: {(status.Phase == FocumonPhase.Break ? "break" : status.CountsDown ? "focusing" : "focused for")} – click to open Focumon");

        // Beside the music: Focumon yields to a Holm timer; otherwise it takes the equalizer's place.
        bool compact = _info?.IsPlaying == true && _timer == null && !_compactTimer.IsVisible;
        _compactFocumon.IsVisible = compact;
        _eqPanel.IsVisible = !compact;
        if (!_focumonTick.IsEnabled) _focumonTick.Start();
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
            var brush = new ImageBrush(Bitmap.DecodeToHeight(memory, 96, BitmapInterpolationMode.None)) { Stretch = Stretch.Uniform };
            if (_spriteUrl != url) return; // another monster meanwhile
            _spriteBrush = brush;
            RefreshFocumon();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            Diag.Log("Focumon picture: " + ex.GetType().Name);
        }
    }

    void SetFocumon(bool on)
    {
        _settings.Focumon = on;
        _settings.Save();
        if (on) StartFocumon();
        else StopFocumon();
    }
}
