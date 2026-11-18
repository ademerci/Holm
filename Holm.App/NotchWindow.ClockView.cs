using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Clicking the clock in the open notch switches to a large clock that fills the whole notch (the WPF app's
/// MainWindow.ClockView.cs, with BigClock.cs linked). It can be pinned open and customized (seconds, date, style,
/// colour, music bar), and shows compact media controls at the bottom.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly IBrush PillOn = Look.Accent, PillOff = Look.InkA(0x2E);

    /// <summary>The colour choices, as the WPF app's BigClock.Colors (its WPF half).</summary>
    static readonly (string Key, string Label, Color Swatch)[] ClockColors =
    {
        (BigClock.ColorWhite, "White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (BigClock.ColorSky, "Auto", Color.FromRgb(0x5E, 0x9E, 0xFF)),
        (BigClock.ColorAmber, "Amber", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        (BigClock.ColorCyan, "Cyan", Color.FromRgb(0x64, 0xD2, 0xFF)),
        (BigClock.ColorGreen, "Green", Color.FromRgb(0x30, 0xD1, 0x58)),
        (BigClock.ColorRose, "Rose", Color.FromRgb(0xFF, 0x37, 0x5F)),
    };

    readonly StackPanel _clockFullView = new() { IsVisible = false };
    readonly TextBlock _clockFullTime = new DotTextBlock { FontFamily = DisplayFont, FontSize = 86, FontWeight = FontWeight.Light, VerticalAlignment = VerticalAlignment.Bottom };
    readonly TextBlock _clockFullSeconds = new DotTextBlock { FontFamily = DisplayFont, FontSize = 28, FontWeight = FontWeight.Light, Opacity = 0.72, Margin = new Thickness(6, 0, 0, 15), VerticalAlignment = VerticalAlignment.Bottom };
    readonly TextBlock _clockFullDate = new() { FontSize = 14, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -8, 0, 2) };
    readonly StackPanel _clockCustomizePanel = new() { IsVisible = false, Margin = new Thickness(0, 4, 0, 6) };
    readonly StackPanel _clockColorRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel _clockMediaBar = new() { Margin = new Thickness(0, 4, 0, 2) };
    readonly Border _clockMediaArt = new() { Width = 38, Height = 38, CornerRadius = new CornerRadius(9), Background = ArtFallback };
    readonly TextBlock _clockMediaArtPlaceholder = new() { Text = "", FontFamily = IconFont, FontSize = 15, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _clockMediaTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _clockMediaArtist = new() { FontSize = 11, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly Path _clockPlayIcon = IconPath(PlayGeo, 14, 16);
    readonly TextBlock _clockPinIcon = new() { Text = "", FontFamily = IconFont, FontSize = 13, Foreground = Dim };
    readonly TextBlock _clockCustomizeIcon = new() { Text = "", FontFamily = IconFont, FontSize = 13, Foreground = Dim };
    NotchButton _clockPinBtn = null!, _clockCustomizeBtn = null!, _clockPlayBtn = null!, _clockPrevBtn = null!, _clockNextBtn = null!;
    NotchButton _clockSecToggle = null!, _clockDateToggle = null!, _clockMediaToggle = null!, _clockStyleToggle = null!;
    bool _clockViewOpen, _clockPinned, _clockCustomizing;
    int _shownClockSecond = -1;

    /// <summary>True while the large clock is open and pinned, so leaving and clicking away keep the notch open.</summary>
    bool PinnedClockOpen => _hovered && _clockViewOpen && _clockPinned;

    void BuildClockView(Panel clockHeader)
    {
        // The header: back on the left, customize and pin on the right.
        var header = new Grid { Margin = new Thickness(0, 0, 0, 2), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var back = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 13, Foreground = Dim }) { Width = 30, Height = 30 };
        ToolTip.SetTip(back, "Back");
        back.Click += () => SetClockView(false);
        header.Children.Add(back);
        _clockCustomizeBtn = new NotchButton(_clockCustomizeIcon) { Width = 30, Height = 30 };
        ToolTip.SetTip(_clockCustomizeBtn, "Customize clock");
        _clockCustomizeBtn.Click += () =>
        {
            _clockCustomizing = !_clockCustomizing;
            PulseElement(_clockCustomizeBtn);
            RefreshClockControls();
            if (_state == NotchState.Expanded) ApplyState(animate: true);
        };
        _clockPinBtn = new NotchButton(_clockPinIcon) { Width = 30, Height = 30, Margin = new Thickness(4, 0, 0, 0) };
        _clockPinBtn.Click += () =>
        {
            _clockPinned = !_clockPinned;
            PulseElement(_clockPinBtn);
            RefreshClockControls();
            WatchClicksAway();
            if (!_clockPinned && !_island.IsPointerOver) _collapseDelay.Start();
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_clockCustomizeBtn);
        right.Children.Add(_clockPinBtn);
        Grid.SetColumn(right, 2);
        header.Children.Add(right);
        _clockFullView.Children.Add(header);

        // The digits; a click goes back unless it's pinned or being customized.
        var digits = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(digits, "Back to notch");
        var time = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        time.Children.Add(_clockFullTime);
        time.Children.Add(_clockFullSeconds);
        digits.Children.Add(time);
        digits.Children.Add(_clockFullDate);
        digits.PointerReleased += (_, e) => { e.Handled = true; if (!_clockPinned && !_clockCustomizing) SetClockView(false); };
        _clockFullView.Children.Add(digits);

        // The customization bar.
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _clockSecToggle = Toggle("Seconds", () => { _settings.ClockShowSeconds = !_settings.ClockShowSeconds; });
        _clockDateToggle = Toggle("Date", () => { _settings.ClockShowDate = !_settings.ClockShowDate; });
        _clockMediaToggle = Toggle("Music", () => { _settings.ClockShowMedia = !_settings.ClockShowMedia; RefreshBigClockMedia(); });
        _clockStyleToggle = Toggle("Style: Light", () => { _settings.ClockStyle = BigClock.NextStyle(_settings.ClockStyle); });
        toggles.Children.AddRange(new Control[] { _clockSecToggle, _clockDateToggle, _clockMediaToggle, _clockStyleToggle });
        _clockCustomizePanel.Children.Add(toggles);
        foreach (var (key, label, swatch) in ClockColors)
        {
            var ring = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.6), BorderBrush = Brushes.Transparent,
                Child = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(swatch), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var button = new NotchButton(ring) { Width = 26, Height = 26, Margin = new Thickness(3, 0), Tag = key };
            ToolTip.SetTip(button, "Colour: " + label);
            button.Click += () =>
            {
                _settings.ClockColor = BigClock.NormalizeColor(key);
                _settings.Save();
                RefreshBigClockStyle();
                RefreshClockControls();
            };
            _clockColorRow.Children.Add(button);
        }
        _clockCustomizePanel.Children.Add(_clockColorRow);
        _clockFullView.Children.Add(_clockCustomizePanel);

        // The compact music bar.
        _clockMediaBar.Children.Add(new Border { Height = 1, Background = Look.InkA(0x1A), Margin = new Thickness(0, 0, 0, 10) });
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _clockMediaArt.Child = _clockMediaArtPlaceholder;
        bar.Children.Add(_clockMediaArt);
        var words = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(_clockMediaTitle);
        words.Children.Add(_clockMediaArtist);
        Grid.SetColumn(words, 1);
        bar.Children.Add(words);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _clockPrevBtn = new NotchButton(IconPath(PrevGeo, 15, 11)) { Width = 32, Height = 32 };
        _clockPrevBtn.Click += () => { PulseElement(_clockPrevBtn); _media.Previous(); };
        _clockPlayBtn = new NotchButton(_clockPlayIcon) { Width = 36, Height = 36, Margin = new Thickness(6, 0) };
        _clockPlayBtn.Click += () =>
        {
            PulseElement(_clockPlayBtn);
            if (_info != null) SetClockPlayIcon(!_info.IsPlaying);
            _media.PlayPause();
        };
        _clockNextBtn = new NotchButton(IconPath(NextGeo, 15, 11)) { Width = 32, Height = 32 };
        _clockNextBtn.Click += () => { PulseElement(_clockNextBtn); _media.Next(); };
        controls.Children.AddRange(new Control[] { _clockPrevBtn, _clockPlayBtn, _clockNextBtn });
        Grid.SetColumn(controls, 2);
        bar.Children.Add(controls);
        _clockMediaBar.Children.Add(bar);
        _clockFullView.Children.Add(_clockMediaBar);
        _expandedView.Children.Add(_clockFullView);

        // The clock under the tool row (or beside it while music plays) opens the large one.
        foreach (Panel opener in new Panel[] { clockHeader, _mediaClockHeader })
        {
            opener.Background ??= Brushes.Transparent;
            opener.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(opener, "Large clock");
            opener.PointerReleased += (_, e) => { e.Handled = true; SetClockView(true); };
        }
    }

    NotchButton Toggle(string label, Action flip)
    {
        var pill = NotchButton.Pill(PillOff);
        pill.Label = label;
        pill.Margin = new Thickness(3, 0);
        pill.Click += () =>
        {
            flip();
            _settings.Save();
            RefreshBigClockStyle();
            RefreshClockControls();
            if (_state == NotchState.Expanded) ApplyState(animate: true);
        };
        return pill;
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
        _expandedPanel.IsVisible = !open;
        _clockFullView.IsVisible = open;
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
        if (animate && _hovered && _state == NotchState.Expanded) AnimateToolSwitch(open ? _clockFullView : _expandedPanel);
        if (_hovered && _state == NotchState.Expanded) ApplyState(animate);
        if (!open && _hovered && !_island.IsPointerOver) _collapseDelay.Start();
    }

    /// <summary>Every second while it shows (the open notch's live timer calls it).</summary>
    void UpdateBigClock(bool force = false)
    {
        if (!_clockViewOpen && !force) return;
        var now = DateTime.Now;
        if (!force && now.Second == _shownClockSecond) return;
        _shownClockSecond = now.Second;
        _clockFullTime.Text = BigClock.FormatTime(now);
        _clockFullSeconds.Text = BigClock.FormatSeconds(now);
        _clockFullDate.Text = BigClock.FormatDate(now);
    }

    void RefreshBigClockStyle()
    {
        // Mono: the bundled text font with figures of equal width (no system monospace font is the same everywhere).
        var (weight, tabular) = BigClock.NormalizeStyle(_settings.ClockStyle) switch
        {
            BigClock.StyleBold => (FontWeight.Bold, false),
            BigClock.StyleMono => (FontWeight.SemiBold, true),
            _ => (FontWeight.Light, false),
        };
        foreach (var text in new[] { _clockFullTime, _clockFullSeconds })
        {
            text.FontWeight = weight;
            text.FontFeatures = tabular ? new FontFeatureCollection { FontFeature.Parse("tnum") } : null;
        }
        var color = ResolveClockColor();
        var brush = new SolidColorBrush(color);
        _clockFullTime.Foreground = brush;
        _clockFullSeconds.Foreground = brush;
        _clockFullSeconds.IsVisible = _settings.ClockShowSeconds;
        _clockFullDate.IsVisible = _settings.ClockShowDate;
    }

    /// <summary>BigClock.ResolveColor of the WPF app: "Auto" takes the cover's colour, else the sky's, else white.</summary>
    Color ResolveClockColor()
    {
        string key = BigClock.NormalizeColor(_settings.ClockColor);
        if (key == BigClock.ColorSky)
        {
            if (_info?.Accent is { } accent && _info.Art != null) return accent;
            if (_skyShown is { } sky) return Color.FromRgb(sky.Left.R, sky.Left.G, sky.Left.B);
            return Colors.White;
        }
        foreach (var (k, _, swatch) in ClockColors) if (k == key) return swatch;
        return Colors.White;
    }

    void RefreshBigClockMedia()
    {
        bool show = _settings.ClockShowMedia && _settings.MediaControls;
        _clockMediaBar.IsVisible = show;
        if (!show) return;
        var info = _settings.MediaControls ? _info : null;
        if (info != null)
        {
            _clockMediaTitle.Text = info.Title.Length > 0 ? info.Title : info.App.Length > 0 ? info.App : "Playing";
            _clockMediaArtist.Text = info.Artist.Length > 0 ? info.Artist : info.App;
            _clockMediaArtist.IsVisible = _clockMediaArtist.Text.Length > 0;
            _clockMediaArt.Background = ArtBrush(info.Art);
            _clockMediaArtPlaceholder.IsVisible = info.Art == null;
            SetClockPlayIcon(info.IsPlaying);
            _clockPlayBtn.IsEnabled = info.CanPlayPause;
            _clockPrevBtn.IsEnabled = info.CanPrevious;
            _clockNextBtn.IsEnabled = info.CanNext;
        }
        else
        {
            _clockMediaTitle.Text = "Not playing";
            _clockMediaArtist.Text = "No active media";
            _clockMediaArtist.IsVisible = true;
            _clockMediaArt.Background = ArtFallback;
            _clockMediaArtPlaceholder.IsVisible = true;
            SetClockPlayIcon(false);
            _clockPlayBtn.IsEnabled = _clockPrevBtn.IsEnabled = _clockNextBtn.IsEnabled = false;
        }
    }

    void SetClockPlayIcon(bool playing)
    {
        _clockPlayIcon.Data = playing ? PauseGeo : PlayGeo;
        _clockPlayIcon.Margin = new Thickness(playing ? 0 : 2, 0, 0, 0);
    }

    void RefreshClockControls()
    {
        _clockPinBtn.Rest = _clockPinned ? ToolLit : Brushes.Transparent;
        _clockPinIcon.Foreground = _clockPinned ? Look.Ink : Dim;
        ToolTip.SetTip(_clockPinBtn, _clockPinned ? "Unpin clock" : "Pin clock open");
        _clockCustomizeBtn.Rest = _clockCustomizing ? ToolLit : Brushes.Transparent;
        _clockCustomizeIcon.Foreground = _clockCustomizing ? Look.Ink : Dim;
        _clockCustomizePanel.IsVisible = _clockCustomizing;
        _clockSecToggle.Rest = _settings.ClockShowSeconds ? PillOn : PillOff;
        _clockDateToggle.Rest = _settings.ClockShowDate ? PillOn : PillOff;
        _clockMediaToggle.Rest = _settings.ClockShowMedia ? PillOn : PillOff;
        _clockStyleToggle.Label = "Style: " + BigClock.StyleLabel(_settings.ClockStyle);
        string active = BigClock.NormalizeColor(_settings.ClockColor);
        foreach (var child in _clockColorRow.Children)
            if (child is NotchButton { Tag: string key, Child: Border ring })
                ring.BorderBrush = key == active ? Look.Ink : Brushes.Transparent;
    }
}
