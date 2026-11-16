using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinNotch;

/// <summary>
/// Clicking the clock in the open notch switches to a large clock view that fills the whole notch.
/// It can be pinned open, customized (seconds, date, style, colour, music bar), and shows compact media controls at the bottom.
/// </summary>
public partial class MainWindow
{
    static readonly Brush PillOn = Frozen(Color.FromArgb(0xD9, 0x0A, 0x84, 0xFF));
    static readonly Brush PillOff = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    bool _clockViewOpen;
    bool _clockPinned;
    bool _clockCustomizing;
    int _shownClockSecond = -1;

    /// <summary>True while the large clock view is open and pinned, so mouse-leave and click-away keep the notch open.</summary>
    bool PinnedClockOpen => _expanded && _clockViewOpen && _clockPinned;

    void InitClockView()
    {
        ClockColorRow.Children.Clear();
        foreach (var (key, label, swatch) in BigClock.Colors)
        {
            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = Frozen(swatch),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var ring = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1.6),
                BorderBrush = Brushes.Transparent,
                Child = dot,
            };
            var btn = new Button
            {
                Style = (Style)FindResource("NotchButton"),
                Width = 26,
                Height = 26,
                Margin = new Thickness(3, 0, 3, 0),
                ToolTip = "Colour: " + label,
                Tag = key,
                Content = ring,
            };
            btn.Click += ClockColor_Click;
            ClockColorRow.Children.Add(btn);
        }
    }

    void SetClockView(bool open, bool animate = true)
    {
        if (_clockViewOpen == open) return;
        _clockViewOpen = open;
        if (!open)
        {
            _clockCustomizing = false;
            _clockPinned = false;
        }
        else EndTyping();

        ExpandedPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        ClockFullView.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            _shownClockSecond = -1;
            RefreshBigClockStyle();
            UpdateBigClock(force: true);
            RefreshBigClockMedia();
            RefreshClockControls();
        }

        ShowSky();
        WatchClicksAway();
        if (animate && _expanded && _state == NotchState.Expanded)
            AnimateToolSwitch(open ? ClockFullView : ExpandedPanel);
        if (_expanded && _state == NotchState.Expanded)
            ApplyState(animate);
        if (!open && _expanded && !Island.IsMouseOver)
            _collapseDelay.Start();
    }

    void UpdateBigClock(bool force = false)
    {
        if (!_clockViewOpen && !force) return;
        var now = DateTime.Now;
        if (!force && now.Second == _shownClockSecond) return;
        _shownClockSecond = now.Second;
        ClockFullTime.Text = BigClock.FormatTime(now);
        ClockFullSeconds.Text = BigClock.FormatSeconds(now);
        ClockFullDate.Text = BigClock.FormatDate(now);
    }

    void RefreshBigClockStyle()
    {
        var (family, weight) = BigClock.ResolveFont(_settings.ClockStyle);
        ClockFullTime.FontFamily = family;
        ClockFullTime.FontWeight = weight;
        ClockFullSeconds.FontFamily = family;
        ClockFullSeconds.FontWeight = weight;

        var color = BigClock.ResolveColor(_settings.ClockColor, _info?.Accent, _skyShown is { } sky ? ToColor(sky.Left) : null);
        var brush = Frozen(color);
        ClockFullTime.Foreground = brush;
        ClockFullSeconds.Foreground = brush;

        ClockFullSeconds.Visibility = _settings.ClockShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        ClockFullDate.Visibility = _settings.ClockShowDate ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshBigClockMedia()
    {
        bool show = _settings.ClockShowMedia && _settings.MediaControls;
        ClockMediaBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var info = _settings.MediaControls ? _info : null;
        if (info != null)
        {
            ClockMediaTitle.Text = info.Title.Length > 0 ? info.Title : (info.App.Length > 0 ? info.App : "Playing");
            ClockMediaArtist.Text = info.Artist.Length > 0 ? info.Artist : info.App;
            ClockMediaArtist.Visibility = ClockMediaArtist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ClockMediaArt.Background = ArtBrush(info.Art);
            ClockMediaArtPlaceholder.Visibility = info.Art == null ? Visibility.Visible : Visibility.Collapsed;
            SetClockPlayIcon(info.IsPlaying);
            ClockPlayBtn.IsEnabled = info.CanPlayPause;
            ClockPrevBtn.IsEnabled = info.CanPrevious;
            ClockNextBtn.IsEnabled = info.CanNext;
        }
        else
        {
            ClockMediaTitle.Text = "Not playing";
            ClockMediaArtist.Text = "No active media";
            ClockMediaArtist.Visibility = Visibility.Visible;
            ClockMediaArt.Background = (Brush)FindResource("ArtFallback");
            ClockMediaArtPlaceholder.Visibility = Visibility.Visible;
            SetClockPlayIcon(false);
            ClockPlayBtn.IsEnabled = false;
            ClockPrevBtn.IsEnabled = false;
            ClockNextBtn.IsEnabled = false;
        }
    }

    void SetClockPlayIcon(bool playing)
    {
        ClockPlayIcon.Data = (Geometry)FindResource(playing ? "PauseGeo" : "PlayGeo");
        ClockPlayIcon.Margin = new Thickness(playing ? 0 : 2, 0, 0, 0);
    }

    void RefreshClockControls()
    {
        ClockPinBtn.Background = _clockPinned ? ToolLit : Brushes.Transparent;
        ClockPinIcon.Foreground = _clockPinned ? Brushes.White : DimBrush;
        ClockPinBtn.ToolTip = _clockPinned ? "Unpin clock" : "Pin clock open";

        ClockCustomizeBtn.Background = _clockCustomizing ? ToolLit : Brushes.Transparent;
        ClockCustomizeIcon.Foreground = _clockCustomizing ? Brushes.White : DimBrush;
        ClockCustomizePanel.Visibility = _clockCustomizing ? Visibility.Visible : Visibility.Collapsed;

        ClockSecToggle.Background = _settings.ClockShowSeconds ? PillOn : PillOff;
        ClockDateToggle.Background = _settings.ClockShowDate ? PillOn : PillOff;
        ClockMediaToggle.Background = _settings.ClockShowMedia ? PillOn : PillOff;
        ClockStyleToggle.Content = "Style: " + BigClock.StyleLabel(_settings.ClockStyle);

        string activeColor = BigClock.NormalizeColor(_settings.ClockColor);
        foreach (UIElement child in ClockColorRow.Children)
        {
            if (child is Button { Tag: string key, Content: Border ring })
                ring.BorderBrush = key == activeColor ? Brushes.White : Brushes.Transparent;
        }
    }

    void ClockHeader_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetClockView(true);
    }

    void ClockBack_Click(object sender, RoutedEventArgs e) => SetClockView(false);

    void ClockFullDigits_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_clockPinned && !_clockCustomizing) SetClockView(false);
    }

    void ClockPin_Click(object sender, RoutedEventArgs e)
    {
        _clockPinned = !_clockPinned;
        PulseElement(ClockPinBtn);
        RefreshClockControls();
        WatchClicksAway();
        if (!_clockPinned && !Island.IsMouseOver) _collapseDelay.Start();
    }

    void ClockCustomize_Click(object sender, RoutedEventArgs e)
    {
        _clockCustomizing = !_clockCustomizing;
        PulseElement(ClockCustomizeBtn);
        RefreshClockControls();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClockSecToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClockShowSeconds = !_settings.ClockShowSeconds;
        _settings.Save();
        RefreshBigClockStyle();
        RefreshClockControls();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClockDateToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClockShowDate = !_settings.ClockShowDate;
        _settings.Save();
        RefreshBigClockStyle();
        RefreshClockControls();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClockMediaToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClockShowMedia = !_settings.ClockShowMedia;
        _settings.Save();
        RefreshBigClockMedia();
        RefreshClockControls();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClockStyleToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClockStyle = BigClock.NextStyle(_settings.ClockStyle);
        _settings.Save();
        RefreshBigClockStyle();
        RefreshClockControls();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void ClockColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        _settings.ClockColor = BigClock.NormalizeColor(key);
        _settings.Save();
        RefreshBigClockStyle();
        RefreshClockControls();
    }

    void ClockPlay_Click(object sender, RoutedEventArgs e)
    {
        PulseElement(ClockPlayBtn);
        if (_info != null)
        {
            SetPlayIcon(!_info.IsPlaying);
            SetClockPlayIcon(!_info.IsPlaying);
        }
        _media.PlayPause();
    }

    void ClockPrev_Click(object sender, RoutedEventArgs e)
    {
        PulseElement(ClockPrevBtn);
        _media.Previous();
    }

    void ClockNext_Click(object sender, RoutedEventArgs e)
    {
        PulseElement(ClockNextBtn);
        _media.Next();
    }
}
