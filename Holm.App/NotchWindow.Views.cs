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
    static readonly IBrush Dim = Look.Prism ? Look.Brush(Look.Current.DimColor) : Look.InkA(0xA6),
        Faint = Look.Prism ? Look.Brush(Look.Current.FaintColor) : Look.InkA(0x66), ArtFallback = Brush("#FF262628");
    static readonly FontFamily IconFont = Fonts.Icons;
    static readonly FontFamily DisplayFont = Fonts.Display;
    static readonly Geometry PlayGeo = Geometry.Parse("M3,1 L16,9 L3,17 Z"),
        PauseGeo = Geometry.Parse("M3,1 L7,1 L7,17 L3,17 Z M11,1 L15,1 L15,17 L11,17 Z"),
        NextGeo = Geometry.Parse("M1,2 L9,9 L1,16 Z M9,2 L17,9 L9,16 Z"),
        PrevGeo = Geometry.Parse("M17,2 L9,9 L17,16 Z M9,2 L1,9 L9,16 Z");

    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();

    static readonly IBrush Ink = Look.Ink;

    // Backdrop
    // Two layers for the cover's colours: a new cover fades in on top of the old one.
    readonly Border _artLayerA = new() { Opacity = 0, IsHitTestVisible = false, Child = new Border { Background = Brushes.Black, Opacity = 0.5 } };
    readonly Border _artLayerB = new() { Opacity = 0, IsHitTestVisible = false, Child = new Border { Background = Brushes.Black, Opacity = 0.5 } };
    Border _artFront = null!, _artBack = null!;
    Bitmap? _shownBackdrop;

    // Idle
    readonly Grid _idleView = new() { Margin = new Thickness(14, 0, 14, 0) };
    readonly TextBlock _idleClock = new DotTextBlock()
    {
        FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Dim,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
    };

    // Compact: something is playing
    readonly Grid _compactView = new() { Margin = new Thickness(9, 0, 12, 0) };
    readonly Border _compactArt = new()
    {
        Width = 20, Height = 20, CornerRadius = new CornerRadius(Look.Pick(3, 5)), Background = ArtFallback,
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
    readonly Border _bigArt = new() { Width = 58, Height = 58, CornerRadius = new CornerRadius(Look.Pick(4, 12)), Background = ArtFallback };
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
    readonly TextBlock _clockBig = new DotTextBlock { FontFamily = DisplayFont, FontSize = 40, FontWeight = FontWeight.Light, Margin = new Thickness(0, -5, 0, 0) };
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
        Data = data, Fill = Look.Ink, Stroke = Look.Ink, StrokeThickness = 1.6, StrokeJoin = PenLineJoin.Round,
        // Let the path take its fitted size so the button can centre the drawn icon.
        Stretch = Stretch.Uniform, MaxWidth = width, MaxHeight = height,
    };

    void BuildViews()
    {
        // One backdrop for the notch and its concave "shoulders", so the cover colours flow into both.
        (_artFront, _artBack) = (_artLayerA, _artLayerB);
        _backdrop.Children.Add(new Rectangle { Fill = Look.NotchFill() });
        if (Look.Aero) _backdrop.Children.Add(new AeroScenery());
        DesignDetail.AddTo(_backdrop, Look.Current);
        if (DotTextBlock.Enabled) _backdrop.Children.Add(new Rectangle { Fill = DotMatrix.Grid(Look.Current.Ink), IsHitTestVisible = false });
        _backdrop.Children.Add(_artLayerA);
        _backdrop.Children.Add(_artLayerB);
        _backdrop.Children.Add(_edge);
        if (_prismRim != null) _backdrop.Children.Add(_prismRim);

        _idleView.Children.Add(_idleClock);

        var eq = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Fill = Look.Ink, Margin = new Thickness(1, 0),
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

        foreach (var view in new Control[] { _compactView, _idleView, _timerView, _volumeView, _downloadView, _peekView, _taskView, _expandedView })
        {
            view.Opacity = 0;
            view.IsHitTestVisible = false;
            _islandContent.Children.Add(view);
        }

        _live.Tick += (_, _) => { UpdateLive(); UpdateBigClock(); };
        _minute.Tick += (_, _) => { UpdateClock(); ShowIdleStatus(); ShowVerity(); ScheduleMinute(); };
    }

    void BuildExpanded()
    {
        var panel = _expandedPanel;
        // Tools: each opens its page in place of home; click again to go back
        _toolRow = BuildToolRow();

        // Home: the player (or the clock)
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _bigArt.Child = _artPlaceholder;
        var texts = _playerTexts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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
        _prevBtn.Click += () => { PulseElement(_prevBtn); _media.Previous(); };
        _playBtn.Click += () =>
        {
            PulseElement(_playBtn);
            if (_info != null)
            {
                // Feels instant; the real state follows a moment later.
                _playIcon.Data = _info.IsPlaying ? PlayGeo : PauseGeo;
            }
            _media.PlayPause();
        };
        _nextBtn.Click += () => { PulseElement(_nextBtn); _media.Next(); };
        // Off, all, this song, and round again.
        _repeatBtn.Click += () => { if (_info?.Repeat is { } mode) _media.SetRepeat(mode switch { 0 => 2, 2 => 1, _ => 0 }); };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.AddRange(new Control[] { _shuffleBtn, _prevBtn, _playBtn, _nextBtn, _repeatBtn });

        _mediaPanel.Children.Add(head);
        _mediaPanel.Children.Add(_progressRow);
        _mediaPanel.Children.Add(controls);

        // No music: time and date stay at the top of home.
        var clockHeader = _clockHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var clockStack = _clockStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        clockStack.Children.Add(_clockBig);
        clockStack.Children.Add(_dateText);
        clockHeader.Children.Add(clockStack);
        _clockPanel.Children.Add(clockHeader);
        BuildWeather(clockHeader);
        BuildSky(clockStack);
        BuildClockView(clockStack);

        // The sections of home; the rows of the others join them later (the constructor), and ApplyHomeLayout puts
        // them into the open notch in your order.
        _mainSection.Children.Add(_mediaPanel);
        _mainSection.Children.Add(_clockPanel);
        BuildToolPages();

        // Below the line: the battery and connected devices on the left, the volume on the right.
        var muteBtn = new NotchButton(_volIcon) { Width = 30, Height = 30 };
        muteBtn.Click += () => { _audio.ToggleMute(); ReadVolume(); };
        _volSlider.ValueChanging += v => { _audio.SetVolume((float)v); UpdateVolumeIcon(v, false); };
        var sound = _soundPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sound.Children.Add(muteBtn);
        sound.Children.Add(_volSlider);
        BuildAiButton(sound);
        BuildOutputButtons(sound);
        var bottom = _statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(sound, 1);
        bottom.Children.Add(_devicesPanel);
        bottom.Children.Add(sound);
        _statusSection.Children.Add(_statusLineAbove);
        _statusSection.Children.Add(bottom);
        _statusSection.Children.Add(_statusLineBelow);
        _expandedView.Children.Add(panel);
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
        string time = now.ToString("HH:mm");
        bool turnedOver = _clockBig.Text != null && _clockBig.Text != time && _state == NotchState.Expanded && !_shapeAnimating;
        _idleClock.Text = time;
        _clockBig.Text = time;
        _dateText.Text = now.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"));
        UpdateMediaClock(now);
        if (turnedOver) RollClock();
    }

    /// <summary>The clock turns over to a new minute: the new time rolls up into place (the WPF app's RollClock).</summary>
    void RollClock()
    {
        if (!_settings.Animations || !_clockPanel.IsVisible) return;
        if (_clockBig.RenderTransform is not TranslateTransform roll) _clockBig.RenderTransform = roll = new TranslateTransform();
        Animate(_clockBig, "roll", v => roll.Y = v, 9, 0, 420, 0, Bouncy);
        Animate(_clockBig, "roll-fade", v => _clockBig.Opacity = v, 0.2, 1, 260, 0, t => t);
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
        bool media = _settings.MediaControls && info != null && !_choosingPlace;
        _mediaClockHeader.IsVisible = _settings.MediaControls && info != null;
        if (Look.Instrument) PlaceToolBar(_mediaClockHeader.IsVisible);
        if (_mediaPanel.IsVisible != media)
        {
            _mediaPanel.IsVisible = media;
            _clockPanel.IsVisible = !media;
            ShowHomeSections(); // the cards come and go with the clock
            FadeIn(media ? _mediaPanel : _clockPanel);
            if (!media && _todaySection.IsVisible) FadeIn(_todaySection);
        }
        _compactArt.Background = ArtBrush(info?.Art);
        RefreshBigClockMedia();
        if (_clockViewOpen) RefreshBigClockStyle();
        RefreshMediaWeather();
        if (info == null) { UpdateClock(); return; }

        // Designs with the cover's colours take the cover's colour for the progress, the volume and the equalizer; the others their accent.
        var accent = Look.Current.Wash ? new SolidColorBrush(info.Accent).ToImmutable() : Look.Accent;
        foreach (var bar in _bars) bar.Fill = accent;
        _progress.Fill = accent;
        if (!Look.Current.Wash) _volSlider.Fill = accent;

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
        _volPeekBar.Fill = _info?.IsPlaying == true ? _progress.Fill : Look.Ink;
        _volPeekIcon.Text = VolumeGlyph(level, muted);
        _volPeekText.Text = muted ? "Muted" : $"{Math.Round(level * 100)}%";
        bool wasVolume = _state == NotchState.Volume;
        _volumePeek = true;
        _volumeTimer.Stop();
        _volumeTimer.Start();
        ApplyState(animate: true);
        if (wasVolume && _state == NotchState.Volume) PulseNotch(16, 10, 0.04);
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
    /// <summary>
    /// Washes the notch in the colours of the cover while something plays (or while the player is open). A new cover
    /// fades in on top of the old one (the WPF app's UpdateBackdrop).
    /// </summary>
    void UpdateBackdrop()
    {
        // The instrument look keeps the notch black: the cover shows in its own square, never washed over the notch.
        var wanted = Look.Current.Wash && _info is { Backdrop: { } art } info && (info.IsPlaying || _state == NotchState.Expanded) ? art : null;
        if (ReferenceEquals(wanted, _shownBackdrop)) return;
        _shownBackdrop = wanted;
        if (!_settings.Animations)
        {
            StopTween(_artLayerA, "opacity");
            StopTween(_artLayerB, "opacity");
            _artFront.Opacity = wanted != null ? 1 : 0;
            _artBack.Opacity = 0;
            if (wanted != null && _info != null)
            {
                _artFront.Background = new ImageBrush(wanted) { Stretch = Stretch.UniformToFill };
                ((Border)_artFront.Child!).Opacity = _info.BackdropShade * Look.Current.Shade;
            }
            return;
        }
        if (wanted == null || _info == null)
        {
            Animate(_artLayerA, "opacity", v => _artLayerA.Opacity = v, _artLayerA.Opacity, 0, 500, 0, t => t);
            Animate(_artLayerB, "opacity", v => _artLayerB.Opacity = v, _artLayerB.Opacity, 0, 500, 0, t => t);
            return;
        }
        (_artFront, _artBack) = (_artBack, _artFront);
        _artFront.Background = new ImageBrush(wanted) { Stretch = Stretch.UniformToFill };
        ((Border)_artFront.Child!).Opacity = _info.BackdropShade * Look.Current.Shade;
        _artFront.ZIndex = 2;
        _artBack.ZIndex = 1;
        var back = _artBack;
        var front = _artFront;
        Animate(front, "opacity", v => front.Opacity = v, 0, 1, 700, 0, t => Math.Sin(t * Math.PI / 2), // WPF's SineEase, easing out
            () => { if (back == _artBack) { StopTween(back, "opacity"); back.Opacity = 0; } }); // another cover arrived meanwhile otherwise
    }
}
