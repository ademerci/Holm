using System;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Holm.App.Controls;

namespace Holm.App;

// What shows inside the notch. Sizes, margins, fonts and colours are those of the WPF app's MainWindow.xaml;
// each view is named after its counterpart there.
public sealed partial class NotchWindow
{
    static readonly IBrush Dim = Brush("#A6FFFFFF"), Faint = Brush("#66FFFFFF"), ArtFallback = Brush("#FF262628");
    static readonly FontFamily IconFont = Fonts.Icons;
    static readonly FontFamily DisplayFont = Fonts.Display;
    static readonly Geometry PlayGeo = Geometry.Parse("M3,1 L16,9 L3,17 Z"),
        PauseGeo = Geometry.Parse("M3,1 L7,1 L7,17 L3,17 Z M11,1 L15,1 L15,17 L11,17 Z"),
        NextGeo = Geometry.Parse("M1,2 L9,9 L1,16 Z M9,2 L17,9 L9,16 Z"),
        PrevGeo = Geometry.Parse("M17,2 L9,9 L17,16 Z M9,2 L1,9 L9,16 Z");

    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();

    // Backdrop
    readonly Border _artLayer = new() { Opacity = 0, IsHitTestVisible = false };
    readonly Border _artShade = new() { Background = Brushes.Black, Opacity = 0.5 };
    Bitmap? _shownBackdrop;

    // Idle
    readonly Grid _idleView = new() { Margin = new Thickness(14, 0, 14, 0) };
    readonly TextBlock _idleClock = new()
    {
        FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Dim,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
    };

    // Compact: something is playing
    readonly Grid _compactView = new() { Margin = new Thickness(9, 0, 12, 0) };
    readonly Border _compactArt = new()
    {
        Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = ArtFallback,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
    };
    readonly Rectangle[] _bars = new Rectangle[4];
    StackPanel _eqPanel = null!;

    // Volume: the level just changed
    readonly Grid _volumeView = new() { Margin = new Thickness(16, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,40") };
    readonly TextBlock _volPeekIcon = new() { FontFamily = IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    readonly MiniSlider _volPeekBar = new() { IsInteractive = false, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _volPeekText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };

    // Expanded: hover
    readonly Grid _expandedView = new()
    {
        Width = 480, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 14, 0, 0),
    };
    readonly StackPanel _mediaPanel = new() { IsVisible = false };
    readonly StackPanel _clockPanel = new() { Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Top };
    readonly Border _bigArt = new() { Width = 58, Height = 58, CornerRadius = new CornerRadius(12), Background = ArtFallback };
    readonly TextBlock _artPlaceholder = new()
    {
        Text = "", FontFamily = IconFont, FontSize = 22, Foreground = Faint,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    readonly TextBlock _titleText = new() { FontFamily = DisplayFont, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _artistText = new() { FontSize = 13, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly TextBlock _appText = new() { FontSize = 11, Foreground = Faint, Margin = new Thickness(0, 3, 0, 0) };
    readonly Grid _progressRow = new() { Margin = new Thickness(0, 12, 0, 0) };
    readonly TextBlock _posText = new() { FontSize = 11, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _remainText = new() { FontSize = 11, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly MiniSlider _progress = new() { Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Path _playIcon = IconPath(PlayGeo, 18, 20);
    readonly TextBlock _shuffleIcon = new() { Text = "", FontFamily = IconFont, FontSize = 14 };
    readonly TextBlock _repeatIcon = new() { Text = "", FontFamily = IconFont, FontSize = 14 };
    NotchButton _playBtn = null!, _prevBtn = null!, _nextBtn = null!, _shuffleBtn = null!, _repeatBtn = null!;
    readonly TextBlock _clockBig = new() { FontFamily = DisplayFont, FontSize = 40, FontWeight = FontWeight.Light, Margin = new Thickness(0, -5, 0, 0) };
    readonly TextBlock _volIcon = new() { Text = "\uE995", FontFamily = IconFont, FontSize = 14, Foreground = Dim };
    readonly MiniSlider _volSlider = new() { Width = 84, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _dateText = new() { FontSize = 13, Foreground = Dim, Margin = new Thickness(1, 0, 0, 0) };

    /// <summary>Runs every 500 ms, but only while the notch is open.</summary>
    readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(500) };
    /// <summary>Wakes once a minute, on the minute, for the closed notch's clock.</summary>
    readonly DispatcherTimer _minute = new();
    ImageBrush? _artBrush;

    // The equalizer bars: where they rest, how low they swing and how long one swing takes.
    static readonly double[] BarRest = { 0.45, 0.85, 0.6, 0.75 }, BarLow = { 0.3, 0.4, 0.25, 0.35 }, BarSeconds = { 0.42, 0.55, 0.37, 0.49 };
    /// <summary>The lowest a reactive bar goes, so the four always read as an equalizer.</summary>
    const double BarFloor = 0.22;
    readonly DispatcherTimer _eqTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) }; // 25 frames a second
    readonly double[] _eqShown = new double[4];
    readonly ScaleTransform[] _barScales = new ScaleTransform[4];
    bool _eqRunning, _eqReactive;
    DateTime _eqStarted, _eqLast;

    static Path IconPath(Geometry data, double width, double height) => new()
    {
        Data = data, Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 1.6, StrokeJoin = PenLineJoin.Round,
        Stretch = Stretch.Uniform, Width = width, Height = height,
    };

    void BuildViews()
    {
        // One backdrop for the notch and its concave "shoulders", so the cover colours flow into both.
        _artLayer.Child = _artShade;
        _artLayer.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) } };
        _backdrop.Children.Add(new Rectangle { Fill = Brushes.Black });
        _backdrop.Children.Add(_artLayer);

        _idleView.Children.Add(_idleClock);

        var eq = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Margin = new Thickness(1, 0),
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = _barScales[i] = new ScaleTransform(1, BarRest[i]),
            };
            eq.Children.Add(_bars[i]);
        }
        _compactView.Children.Add(_compactArt);
        _compactView.Children.Add(eq);
        _eqPanel = eq;
        _compactView.Children.Add(_compactTimer);
        BuildTimer();

        Grid.SetColumn(_volPeekBar, 1);
        Grid.SetColumn(_volPeekText, 2);
        _volumeView.Children.AddRange(new Control[] { _volPeekIcon, _volPeekBar, _volPeekText });
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); _volumePeek = false; ApplyState(animate: true); };
        _eqTimer.Tick += (_, _) => TickEqualizer();

        BuildPopups();
        BuildExpanded();

        foreach (var view in new Control[] { _compactView, _idleView, _timerView, _volumeView, _peekView, _taskView, _expandedView })
        {
            view.Opacity = 0;
            view.IsHitTestVisible = false;
            _islandContent.Children.Add(view);
        }

        _live.Tick += (_, _) => UpdateLive();
        _minute.Tick += (_, _) => { UpdateClock(); ShowIdleStatus(); ScheduleMinute(); };
    }

    void BuildExpanded()
    {
        var panel = new StackPanel();
        // Tools: each opens its page in place of home; click again to go back
        panel.Children.Add(BuildToolRow());

        // Home: the player (or the clock)
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _bigArt.Child = _artPlaceholder;
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_titleText);
        texts.Children.Add(_artistText);
        texts.Children.Add(_appText);
        Grid.SetColumn(texts, 1);
        head.Children.Add(_bigArt);
        head.Children.Add(texts);

        _progressRow.ColumnDefinitions = new ColumnDefinitions("42,*,42");
        Grid.SetColumn(_progress, 1);
        Grid.SetColumn(_remainText, 2);
        _progressRow.Children.Add(_posText);
        _progressRow.Children.Add(_progress);
        _progressRow.Children.Add(_remainText);
        _progress.ValueCommitted += value =>
        {
            if (_info is { CanSeek: true } info) _media.Seek(TimeSpan.FromTicks((long)(info.Duration.Ticks * value)));
        };

        _shuffleBtn = new NotchButton(_shuffleIcon) { Width = 34, Height = 34, Margin = new Thickness(0, 0, 14, 0) };
        _prevBtn = new NotchButton(IconPath(PrevGeo, 21, 15)) { Width = 42, Height = 42 };
        _playBtn = new NotchButton(_playIcon) { Width = 46, Height = 46, Margin = new Thickness(16, 0) };
        _nextBtn = new NotchButton(IconPath(NextGeo, 21, 15)) { Width = 42, Height = 42 };
        _repeatBtn = new NotchButton(_repeatIcon) { Width = 34, Height = 34, Margin = new Thickness(14, 0, 0, 0) };
        _shuffleBtn.Click += () => { if (_info?.Shuffle is { } on) _media.SetShuffle(!on); };
        _prevBtn.Click += _media.Previous;
        _playBtn.Click += _media.PlayPause;
        _nextBtn.Click += _media.Next;
        // Off, all, this song, and round again.
        _repeatBtn.Click += () => { if (_info?.Repeat is { } mode) _media.SetRepeat(mode switch { 0 => 2, 2 => 1, _ => 0 }); };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.AddRange(new Control[] { _shuffleBtn, _prevBtn, _playBtn, _nextBtn, _repeatBtn });

        _mediaPanel.Children.Add(head);
        _mediaPanel.Children.Add(_progressRow);
        _mediaPanel.Children.Add(controls);

        // No music: time and date stay at the top of home.
        _clockPanel.Children.Add(_clockBig);
        _clockPanel.Children.Add(_dateText);

        _homePanel.Children.Add(_mediaPanel);
        _homePanel.Children.Add(_clockPanel);
        _homePanel.Children.Add(_timerRow);
        panel.Children.Add(_homePanel);
        panel.Children.Add(BuildToolPages());

        panel.Children.Add(new Border { Height = 1, Background = Brush("#1AFFFFFF"), Margin = new Thickness(0, 12, 0, 8) });

        // Below the line: the battery and connected devices on the left, the volume on the right.
        var muteBtn = new NotchButton(_volIcon) { Width = 30, Height = 30 };
        muteBtn.Click += () => { _audio.ToggleMute(); ReadVolume(); };
        _volSlider.ValueChanging += v => { _audio.SetVolume((float)v); UpdateVolumeIcon(v, false); };
        var sound = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sound.Children.Add(muteBtn);
        sound.Children.Add(_volSlider);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(sound, 1);
        bottom.Children.Add(_devicesPanel);
        bottom.Children.Add(sound);
        panel.Children.Add(bottom);
        _expandedView.Children.Add(panel);
    }

    /// <summary>Shows or hides one of the notch's views, with a short fade when animated.</summary>
    static void SetView(Control view, bool show, bool animate)
    {
        view.IsHitTestVisible = show;
        view.Transitions = animate
            ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(show ? 180 : 100) } }
            : null;
        view.Opacity = show ? 1 : 0;
    }

    void StartClock()
    {
        UpdateClock();
        ScheduleMinute();
    }

    void ScheduleMinute()
    {
        var now = DateTime.Now;
        _minute.Stop();
        _minute.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _minute.Start();
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        _idleClock.Text = now.ToString("HH:mm");
        _clockBig.Text = now.ToString("HH:mm");
        _dateText.Text = now.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"));
    }

    /// <summary>One brush per cover, shared by the compact and the open notch.</summary>
    IBrush ArtBrush(Bitmap? art)
    {
        if (art == null) return ArtFallback;
        if (!ReferenceEquals(_artBrush?.Source, art)) _artBrush = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        return _artBrush!;
    }

    void RefreshMediaUi()
    {
        var info = _info;
        _mediaPanel.IsVisible = info != null;
        _clockPanel.IsVisible = info == null;
        _compactArt.Background = ArtBrush(info?.Art);
        if (info == null) { UpdateClock(); return; }

        var accent = new SolidColorBrush(info.Accent).ToImmutable();
        foreach (var bar in _bars) bar.Fill = accent;
        _progress.Fill = accent;

        _titleText.Text = info.Title;
        _artistText.Text = info.Artist;
        _artistText.IsVisible = info.Artist.Length > 0;
        _appText.Text = info.Album.Length > 0 && info.App.Length > 0 ? $"{info.Album} · {info.App}" : info.Album.Length > 0 ? info.Album : info.App;
        _bigArt.Background = ArtBrush(info.Art);
        _artPlaceholder.IsVisible = info.Art == null;

        // Shuffle and repeat, lit in the cover's colour when on; hidden when the player doesn't offer them.
        _shuffleBtn.Opacity = info is { CanShuffle: true, Shuffle: not null } ? 1 : 0;
        _shuffleBtn.IsHitTestVisible = _shuffleBtn.Opacity > 0;
        _repeatBtn.Opacity = info is { CanRepeat: true, Repeat: not null } ? 1 : 0;
        _repeatBtn.IsHitTestVisible = _repeatBtn.Opacity > 0;
        _shuffleIcon.Foreground = info.Shuffle == true ? accent : Faint;
        _repeatIcon.Foreground = info.Repeat is 1 or 2 ? accent : Faint;
        _repeatIcon.Text = info.Repeat == 1 ? "" : ""; // repeat one / repeat all

        _playIcon.Data = info.IsPlaying ? PauseGeo : PlayGeo;
        _playIcon.Margin = new Thickness(info.IsPlaying ? 0 : 3, 0, 0, 0); // a triangle looks off-centre when centred
        _playBtn.IsEnabled = info.CanPlayPause;
        _prevBtn.IsEnabled = info.CanPrevious;
        _nextBtn.IsEnabled = info.CanNext;
        _progressRow.IsVisible = info.Duration > TimeSpan.FromSeconds(1);
        _progress.IsInteractive = info.CanSeek;
        UpdateLive();
    }

    void UpdateLive()
    {
        if (_info is { } info && _progressRow.IsVisible && !_progress.IsDragging)
        {
            var pos = info.CurrentPosition;
            _progress.Value = info.Duration.Ticks > 0 ? (double)pos.Ticks / info.Duration.Ticks : 0;
            _posText.Text = FormatTime(pos);
            _remainText.Text = "-" + FormatTime(info.Duration - pos);
        }
        if (_clockPanel.IsVisible) UpdateClock();
        ReadVolume();
    }

    static string VolumeGlyph(double level, bool muted)
        => muted || level < 0.005 ? "\uE74F" : level < 0.34 ? "\uE993" : level < 0.67 ? "\uE994" : "\uE995";

    void ReadVolume()
    {
        if (_volSlider.IsDragging) return;
        if (_audio.TryGetVolume(out float level, out bool muted))
        {
            _volSlider.Value = muted ? 0 : level;
            _volSlider.Opacity = 1;
            UpdateVolumeIcon(level, muted);
        }
        else _volSlider.Opacity = 0.35;
    }

    void UpdateVolumeIcon(double level, bool muted)
    {
        string icon = VolumeGlyph(level, muted);
        if (_volIcon.Text != icon) _volIcon.Text = icon;
    }

    /// <summary>The volume changed, by a key, another app or us: the open notch follows, the closed one shows it for a moment.</summary>
    void OnVolumeChanged(float level, bool muted)
    {
        if (_state == NotchState.Expanded) { ReadVolume(); return; }

        _volPeekBar.Value = muted ? 0 : level;
        _volPeekBar.Fill = _info?.IsPlaying == true ? _progress.Fill : Brushes.White;
        _volPeekIcon.Text = VolumeGlyph(level, muted);
        _volPeekText.Text = muted ? "Muted" : $"{Math.Round(level * 100)}%";
        bool wasVolume = _state == NotchState.Volume;
        _volumePeek = true;
        _volumeTimer.Stop();
        _volumeTimer.Start();
        ApplyState(animate: true);
        if (wasVolume && _state == NotchState.Volume) PulseNotch(16, 10);
    }

    void OnDefaultDeviceChanged()
    {
        _audio.Start();
        if (_state == NotchState.Expanded) ReadVolume();
    }

    /// <summary>
    /// The bars while music plays. With <see cref="WinNotch.AppSettings.ReactiveEqualizer"/> (the default) they follow the
    /// sound that plays, read by the audio backend; otherwise, or while the sound can't be read, they bounce.
    /// This is the only continuous animation in the app, so it runs at a low frame rate, and only while the
    /// compact player shows and the shape rests.
    /// </summary>
    void UpdateEqualizer()
    {
        bool run = _state == NotchState.Media && _timer == null && _settings.Animations && !_motionRunning;
        bool reactive = run && _settings.ReactiveEqualizer;
        if (run == _eqRunning && reactive == _eqReactive) return;

        // Whatever ran stops first, then the new way starts.
        _eqTimer.Stop();
        _audio.StopMeter();
        _eqRunning = run;
        _eqReactive = reactive;

        if (!run)
        {
            for (int i = 0; i < _barScales.Length; i++) _barScales[i].ScaleY = BarRest[i];
            return;
        }
        if (reactive) _audio.StartMeter();
        for (int i = 0; i < _barScales.Length; i++) _eqShown[i] = _barScales[i].ScaleY;
        _eqStarted = _eqLast = DateTime.UtcNow;
        _eqTimer.Start();
    }

    /// <summary>One frame of the bars: the level each bar reads, smoothed between frames like the mod does.</summary>
    void TickEqualizer()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _eqLast).TotalSeconds, 0.001, 0.1);
        _eqLast = now;
        double alpha = 1 - Math.Exp(-18 * dt);
        double t = (now - _eqStarted).TotalSeconds;
        bool live = _eqReactive && _audio.MeterWorking;
        for (int i = 0; i < _barScales.Length; i++)
        {
            double target = live
                ? BarFloor + (1 - BarFloor) * _audio.MeterBar(i, _barScales.Length)
                // The sound can't be read (yet), or the bars aren't meant to follow it: the bounce, worked out by hand.
                : BarLow[i] + (1 - BarLow[i]) * (0.5 - 0.5 * Math.Cos(Math.PI * t / BarSeconds[i]));
            _eqShown[i] += (target - _eqShown[i]) * alpha;
            _barScales[i].ScaleY = Math.Clamp(_eqShown[i], 0.05, 1);
        }
    }

    static string FormatTime(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>Washes the notch in the colours of the cover while something plays (or while the player is open).</summary>
    void UpdateBackdrop()
    {
        var wanted = _info is { Backdrop: { } art } info && (info.IsPlaying || _state == NotchState.Expanded) ? art : null;
        if (ReferenceEquals(wanted, _shownBackdrop)) return;
        _shownBackdrop = wanted;
        if (wanted == null) { _artLayer.Opacity = 0; return; }
        _artLayer.Background = new ImageBrush(wanted) { Stretch = Stretch.UniformToFill };
        _artShade.Opacity = _info!.BackdropShade;
        _artLayer.Opacity = 1;
    }
}
