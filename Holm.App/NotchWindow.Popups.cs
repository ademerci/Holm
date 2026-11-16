using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;

namespace Holm.App;

/// <summary>
/// What the closed notch pops out by itself: a short line (the peek) and a card with buttons (the task: a reminder
/// that is due, later transfers and screenshots). Both go away by themselves.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly IBrush TimerOrange = Brush("#FFFF9F0A"), ShareBlue = Brush("#FF0A84FF");

    // Peek: one line, for a few seconds
    readonly Grid _peekView = new() { Margin = new Thickness(16, 0) };
    readonly TextBlock _peekIcon = new() { FontFamily = IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    readonly TextBlock _peekText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
    readonly TextBlock _peekValue = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly BatteryGlyph _peekBattery = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    bool _peekActive;

    // Task: an icon, two lines and up to two buttons
    readonly Grid _taskView = new() { Margin = new Thickness(12, 0, 12, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
    readonly Border _taskIconCircle = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = ShareBlue, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _taskIcon = new() { FontFamily = IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _taskTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _taskSubtitle = new() { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly MiniSlider _taskProgress = new() { IsInteractive = false, Margin = new Thickness(0, 4, 0, 0), IsVisible = false };
    readonly NotchButton _taskSecondary = NotchButton.Pill(), _taskPrimary = NotchButton.Pill(ShareBlue), _taskExtra = NotchButton.Pill();
    readonly DispatcherTimer _taskHide = new() { Interval = TimeSpan.FromSeconds(5) };
    Action? _primaryAction, _secondaryAction, _extraAction;
    bool _taskVisible;

    void BuildPopups()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(_peekIcon);
        left.Children.Add(_peekText);
        _peekView.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_peekValue);
        right.Children.Add(_peekBattery);
        _peekView.Children.Add(right);
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); _peekActive = false; ApplyState(animate: true); };

        _taskIconCircle.Child = _taskIcon;
        var texts = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_taskTitle);
        texts.Children.Add(_taskSubtitle);
        texts.Children.Add(_taskProgress);
        Grid.SetColumn(texts, 1);
        _taskPrimary.Margin = new Thickness(6, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _taskExtra.Margin = new Thickness(0, 0, 6, 0);
        _taskExtra.IsVisible = false;
        buttons.Children.Add(_taskExtra);
        buttons.Children.Add(_taskSecondary);
        buttons.Children.Add(_taskPrimary);
        Grid.SetColumn(buttons, 2);
        _taskView.Children.AddRange(new Control[] { _taskIconCircle, texts, buttons });
        _taskPrimary.Click += () => _primaryAction?.Invoke();
        _taskSecondary.Click += () => _secondaryAction?.Invoke();
        _taskExtra.Click += () => _extraAction?.Invoke();
        // Stays while the mouse rests on it.
        _taskHide.Tick += (_, _) =>
        {
            _taskHide.Stop();
            if (_taskVisible && _island.IsPointerOver) _taskHide.Start();
            else HideTask();
        };
    }

    /// <summary>Pops a line out of the closed notch for a few seconds.</summary>
    void ShowPeek(string? glyph, string text, string value, IBrush? valueBrush, double seconds = 3, IBrush? glyphBrush = null,
                  int? battery = null, bool charging = false)
    {
        if (_hiddenForFullscreen || _state == NotchState.Expanded) return;

        _updateNotification = false;
        _peekIcon.Text = glyph ?? "";
        _peekIcon.Foreground = glyphBrush ?? Brushes.White;
        _peekIcon.IsVisible = glyph != null;
        _peekText.Text = text;
        _peekText.MaxWidth = value.Length == 0 && battery == null ? 260 : 170; // a sentence on its own may use the whole width
        _peekValue.Text = value;
        _peekValue.Foreground = valueBrush ?? Brushes.White;
        _peekBattery.IsVisible = battery != null;
        _peekBattery.Level = battery ?? 0;
        _peekBattery.Charging = charging;

        bool wasPeek = _state == NotchState.Peek;
        _peekActive = true;
        _peekMessage = false;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
        _peekTimer.Start();
        ApplyState(animate: true);
        if (wasPeek && _state == NotchState.Peek) PulseNotch();
    }

    /// <summary>Shows a card with up to two buttons in place of the closed notch; it stays until <see cref="HideTask"/>.</summary>
    void ShowTask(string icon, IBrush circle, string title, string subtitle, double? progress,
                  (string Label, Action Act)? primary, (string Label, Action Act)? secondary, bool textIcon = false)
    {
        _taskExtra.IsVisible = false; // a third button belongs to the task that asked for it
        _extraAction = null;
        _taskIcon.Text = icon;
        // A person's initials in their circle, an icon otherwise.
        _taskIcon.FontFamily = textIcon ? FontFamily.Default : IconFont;
        _taskIcon.FontWeight = textIcon ? FontWeight.SemiBold : FontWeight.Normal;
        _taskIconCircle.Background = circle;
        // A screenshot's picture or a chat GIF takes the icon's place; every other card has its icon.
        StopTaskGif();
        _taskGif = null;
        _taskThumb.IsVisible = false;
        _taskIconCircle.IsVisible = true;
        _taskTitle.Text = title;
        _taskSubtitle.Text = subtitle;
        _taskSubtitle.IsVisible = subtitle.Length > 0;
        _taskProgress.IsVisible = progress != null;
        _taskProgress.Value = progress ?? 0;
        _taskProgress.Fill = ShareBlue;

        _taskPrimary.Label = primary?.Label;
        _taskPrimary.IsVisible = primary != null;
        _primaryAction = primary?.Act;
        _taskSecondary.Label = secondary?.Label;
        _taskSecondary.IsVisible = secondary != null;
        _secondaryAction = secondary?.Act;

        _taskHide.Stop();
        if (!_taskVisible)
        {
            _taskVisible = true;
            _peekActive = false;
            _volumePeek = false;
            // The card takes the open notch's place: it closes, also under the mouse.
            EndTyping();
            _hovered = false;
        }
        ApplyState(animate: true);
    }

    /// <summary>A third button, left of the other two, for the task shown last; the notch grows to fit it.</summary>
    void ShowTaskExtra(string label, Action act)
    {
        _taskExtra.Label = label;
        _taskExtra.IsVisible = true;
        _taskExtra.IsEnabled = true;
        _extraAction = act;
        ApplyState(animate: true);
    }

    void HideTask()
    {
        _taskHide.Stop();
        _primaryAction = _secondaryAction = _extraAction = null;
        StopTaskGif();
        _taskGif = null;
        if (!_taskVisible) return;
        _taskVisible = false;
        ApplyState(animate: true);
    }

    // ───────────── volume and media keys ─────────────

    IMediaKeysPlatform _mediaKeys = null!;

    void InitMediaKeys(IPlatformServices platform) => _mediaKeys = platform.MediaKeys;

    void SetNotchOnlyPopups(bool on)
    {
        _settings.NotchOnlyPopups = on;
        _settings.Save();
        StartMediaKeys();
    }

    void StartMediaKeys()
    {
        _mediaKeys.Stop();
        if (!_settings.NotchOnlyPopups || !_mediaKeys.IsSupported) return;
        _mediaKeys.Start(TakesMediaKey, key => Dispatcher.UIThread.Post(() => PressMediaKey(key)));
    }

    bool TakesMediaKey(MediaKey key)
        => !_hiddenForFullscreen && (key is MediaKey.VolumeMute or MediaKey.VolumeDown or MediaKey.VolumeUp || _info != null);

    void PressMediaKey(MediaKey key)
    {
        switch (key)
        {
            case MediaKey.VolumeMute:
                _audio.ToggleMute();
                break;
            case MediaKey.VolumeUp or MediaKey.VolumeDown when _audio.TryGetVolume(out float level, out bool muted):
                if (muted) _audio.SetMute(false);
                _audio.SetVolume(MediaKeysMath.Step(level, key == MediaKey.VolumeUp));
                break;
            case MediaKey.PlayPause: _media.PlayPause(); break;
            case MediaKey.NextTrack: _media.Next(); break;
            case MediaKey.PreviousTrack: _media.Previous(); break;
            case MediaKey.StopMedia: _media.Pause(); break;
        }
    }
}
