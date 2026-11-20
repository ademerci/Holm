using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The notch: a transparent window at the top of the screen in which the black shape moves. The window is not
/// resized frame by frame: it grows once before the shape does and fits it again once it rests. The shape is a
/// clip on <see cref="_backdrop"/>, driven by the same springs as the WPF app (Spring.cs).
///
/// This file holds the window, the shape and its motion; NotchWindow.Views.cs what shows inside.
/// Ported so far: idle, the compact player and the open notch's home (player or clock).
/// </summary>
public sealed partial class NotchWindow : Window
{
    // The numbers in this file are the WPF app's (MainWindow.xaml.cs, MainWindow.Motion.cs); they must stay identical.
    const double BaseWidth = 560;
    const double ExpandedWidth = 520;
    const double MaxEar = 10;

    enum NotchState { Idle, Badge, Timer, Focumon, Media, Download, Call, Dictation, Peek, Message, Volume, Phone, Task, Share, Expanded }

    readonly INotchWindowPlatform _platform;
    readonly MediaService _media;
    readonly IAudioPlatform _audio;
    readonly IAutostartPlatform _autostart;
    // Read only on Windows for now: there the port shares the installed app's settings file and must not write to
    // it yet. With HOLM_DATA=folder (Debug builds) it runs on test data of its own and saves there. On Linux
    // there is no other Holm: the file is this app's own, and it saves.
    readonly AppSettings _settings = AppSettings.Load();
    readonly Canvas _root = new();
    /// <summary>The black surface (and the cover's colours on it), as wide as the notch plus a shoulder on each side.</summary>
    readonly Panel _backdrop = new();
    readonly PrismGlass? _prismRim = Look.Prism ? new() { IsHitTestVisible = false } : null;
    // The design's line around the notch (Neon, Terminal, Brutalist …), drawn inside it: the clip keeps the outer half off.
    readonly Avalonia.Controls.Shapes.Path _edge = new()
    {
        IsHitTestVisible = false, IsVisible = Look.Current.Edge != 0, Stroke = Look.Brush(Look.Current.Edge),
        StrokeThickness = Look.Current.EdgeWidth * 2,
        Effect = Look.Current.Glow ? new DropShadowEffect { BlurRadius = 10, OffsetX = 0, OffsetY = 0, Color = Color.FromUInt32(Look.Current.Edge) } : null,
    };
    /// <summary>The notch itself: what is in it, and what the mouse opens.</summary>
    readonly Border _island = new() { Background = Brushes.Transparent };
    readonly Grid _islandContent = new();

    SpringValue _wSpring = new(150);
    SpringValue _hSpring = new(30);
    SpringValue _rSpring = new(15, 0.15, 2.0);
    SpringValue _earSpring = new(MaxEar, 0.1, 1.5);
    SpringValue _scaleSpring = new(1, 0.0008, 0.015);
    SpringValue _dragLean = new(0, 0.08, 0.5);
    readonly SkewTransform _dragSkew = new();
    /// <summary>The notch's size preference (Customize › Collapsed / Expanded size), applied to everything in it.</summary>
    readonly ScaleTransform _notchScale = new(1, 1);

    NotchState _state = NotchState.Idle;
    bool _motionRunning, _hovered, _opened, _volumePeek, _mini;
    long _motionLastTick;
    double _shapeW = 150, _shapeH = 30, _shapeR = 15, _shapeScale = 1;
    (double Width, double Height, double Radius, double Ear) _drawnOutline;
    double _hitRegionWidth, _hitRegionHeight;
    double _screenWidth = double.PositiveInfinity;
    bool _shapeAnimating, _hoverBoost;
    MediaInfo? _info;

    public NotchWindow(IPlatformServices platform)
    {
        _platform = platform.NotchWindow;
        _media = new MediaService(platform.Media);
        _audio = platform.Audio;
        _autostart = platform.Autostart;
        InitDevices(platform);
        InitClipboard(platform);
        InitColorPicker(platform);
        InitMixer(platform);
        InitDownloads(platform);
        InitDiskSpace();
        InitConnectivity(platform);
        InitCapsLock(platform);
        InitMediaKeys(platform);
        InitWeather(platform);
        _sound = platform.Sound;
        InitHotKey(platform);
        InitClickAway(platform);
        InitFullscreen(platform);
        // As the WPF app with --snap: a self-test poses the notch and must not end up in your settings. Otherwise Holm
        // saves, on every system (it was read-only on Windows while the port ran next to the WPF app).
        AppSettings.ReadOnly = SelfTestFolder != null;
        Spring.Bounce = _settings.Bounce ? _settings.BounceLevel : 0;
        Spring.Jelly = _settings.Jelly;

        Title = "Holm";
        Width = BaseWidth;
        Height = NeededWindowHeight(30);
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        // X11 (Linux): a dock, which window managers keep above normal windows, on every workspace and out of the
        // task switcher. It has to be said before the window shows; other systems ignore it.
        X11Properties.SetNetWmWindowType(this, Avalonia.Controls.Platform.X11NetWmWindowType.Dock);
        FontFamily = Fonts.Text;
        Fonts.ApplyRendering(this);
        Foreground = Look.Ink;
        FontFamily = Look.Font;

        BuildViews();
        BuildShelf();
        InitScreenshots();
        InitUsb(platform);
        InitVpn();
        InitPrivacy(platform);
        InitMessages(platform);
        InitBeam(platform);
        InitHub();
        InitAchievements();
        InitVerity();
        InitAccount();
        InitActivity();
        InitStreaming();
        InitSendLater();
        InitUpdates();
        BuildFocumon();
        InitDictation(platform);
        InitPhone();
        BuildToday();
        // The rows of home in the WPF app's order (MainWindow.xaml), each in its section; the sections go where you put
        // them (Settings › Start page), in that order unless you moved them.
        _liveSection.Children.AddRange(new Control[] { _quietNote, _clearNotificationsBtn, _messageRows, _downloadRow, _laterRows, _usbRows, _callRow, _privacyRows, _timerRow });
        _shelfSection.Children.Add(_shelfRow);
        ApplyHomeLayout();
        _island.Child = _islandContent;
        _root.Children.Add(_backdrop);
        _root.Children.Add(_island);
        // The notch's size preference scales everything about the top centre, as the WPF app's layout transform does.
        _root.Width = BaseWidth;
        _root.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _root.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _root.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        _root.RenderTransform = new TransformGroup { Children = { _dragSkew, _notchScale } };
        Content = _root;

        InitMotion();
        InitPositionDrag();

        BuildContextMenu();
        _media.Changed += OnMediaChanged;
        _audio.VolumeChanged += (level, muted) => Dispatcher.UIThread.Post(() => OnVolumeChanged(level, muted));
        _audio.DefaultDeviceChanged += () => Dispatcher.UIThread.Post(OnDefaultDeviceChanged);
        ApplyState(animate: false);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        if (TryGetPlatformHandle() is { } handle) _platform.MakeOverlay(handle.Handle);
        FitToScreen();
        // A display came or went, or the notch moved to one with another scale (125 % to 100 %): the window's cut-out is
        // in device pixels, so it is worked out again, or the notch shows clipped into a stale, shifted rectangle.
        Screens.Changed += (_, _) => FitToScreen();
        ScalingChanged += (_, _) => FitToScreen();
        UpdateHitRegion(_shapeW, _shapeH, _shapeScale);
        StartClock();
        ShowIdleStatus();
        CheckReminders();
        StartDevices();
        StartClipboard();
        StartDownloads();
        StartDiskSpace();
        StartConnectivity();
        StartWeather();
        ShowSleep();
        StartCapsLock();
        StartMediaKeys();
        StartHotKey();
        StartScreenshots();
        StartUsb();
        StartVpn();
        // Keep the notch on screen for the visual self-test, even behind a fullscreen foreground app.
        if (SelfTestFolder == null) StartFullscreen();
        StartPrivacy();
        StartMessages();
        StartSharing();
        StartAccount();
        StartSendLater();
        StartUpdates();
        StartFocumon();
        StartDictation();
        StartAiUsage();
        _trimTimer.Start();
        _audio.Start();
        _ = _media.StartAsync();
        StartSelfTest();
    }

    protected override void OnClosed(EventArgs e)
    {
        _opened = false;
        _animationTiming?.Dispose();
        _animationTiming = null;
        HideMixer();
        _eqTimer.Stop();
        _powerPlatform.Stop();
        _audio.Stop();
        _clipboardPlatform.Stop();
        StopDownloads();
        _connectivity.Stop();
        _capsLock.Stop();
        _mediaKeys.Stop();
        _weatherClosed = true;
        _locationCancel?.Cancel();
        _hotKey.Stop();
        _clickAway.Watch(false);
        StopScreenshots();
        _drives.Stop();
        _sensors.Stop();
        _notifications.Stop();
        StopSharing();
        StopUpdates();
        StopFocumon();
        StopDictation();
        StopPhone();
        StopAiUsage();
        StopAccount();
        ForgetClipFiles();
        base.OnClosed(e);
    }

    /// <summary>Places the window on its screen again and redoes its size and cut-out for that screen's scale.</summary>
    void FitToScreen()
    {
        double width = ChosenScreen() is { } screen
            ? Math.Max(3, Math.Floor(screen.Bounds.Width / screen.Scaling))
            : double.PositiveInfinity;
        if (_screenWidth != width)
        {
            _screenWidth = width;
            ApplyState(animate: false);
        }
        PositionWindow();
        if (_motionRunning)
        {
            // Keep the room reserved for the current animation when converting to the new DPI.
            ApplyHitRegion();
        }
        else
        {
            FitWindowHeight();
            UpdateHitRegion(_shapeW, _shapeH, _shapeScale);
        }
    }

    /// <summary>Top centre of the primary screen.</summary>
    void PositionWindow()
    {
        var screen = ChosenScreen();
        if (screen == null) return;
        int width = (int)Math.Round(Width * screen.Scaling);
        int top = screen.Bounds.Y;
#if DEBUG
        // HOLM_DEV_TOP=300 puts the notch that many pixels down, to run it next to an installed Holm while developing.
        if (int.TryParse(Environment.GetEnvironmentVariable("HOLM_DEV_TOP"), out int down)) top += down;
#endif
        Position = new PixelPoint(NotchPlacement.Left(screen.Bounds.X, screen.Bounds.Right, width, _settings.NotchPosition), top);
    }

    void OnMediaChanged(MediaInfo? info, bool trackChanged)
    {
        if (trackChanged && info is { IsPlaying: true, Title.Length: > 0 }) Achieve(AchievementBook.Songs);
        // Music stopped: the covers and the equalizer pulled a lot into memory that idle doesn't need.
        if (_info?.IsPlaying == true && info?.IsPlaying != true && !_hovered) _trimTimer.Start();
        _info = info;
        RefreshMediaUi();
        ApplyState(animate: true);
        UpdateCallPause();
    }

    /// <summary>Works out what the notch shows right now and moves the shape and the views there.</summary>
    void ApplyState(bool animate)
    {
        animate &= _settings.Animations;
        WatchClicksAway();
        var target = _hovered ? NotchState.Expanded
                   : _sharing ? NotchState.Share
                   : _taskVisible ? NotchState.Task
                   : PhoneShowing ? NotchState.Phone
                   : _volumePeek ? NotchState.Volume
                   : _peekActive ? (_peekMessage ? NotchState.Message : NotchState.Peek)
                   : _dictating ? NotchState.Dictation // you're talking to it right now: above a call or music
                   : _call != null ? NotchState.Call
                   // A running download shows while nothing plays; over music only for a moment when it starts.
                   : _download != null && (_downloadPeek || _info?.IsPlaying != true) ? NotchState.Download
                   : _info?.IsPlaying == true ? NotchState.Media // a running timer shows in place of the equalizer
                   : _timer != null ? NotchState.Timer
                   : HasFocumon ? NotchState.Focumon
                   : _messages.Count > 0 || HasUpdateNotification ? NotchState.Badge
                   : NotchState.Idle;

        // Over a maximized browser the notch is the idle pill, so the tabs under it can be clicked.
        // Alerts, calls and privacy indicators still get their normal space.
        bool browserMini = _browserInFront && !_hiddenForFullscreen && SensorsInUse.Count == 0
                           && target is NotchState.Idle or NotchState.Badge or NotchState.Media or NotchState.Timer or NotchState.Focumon;
        if (browserMini) target = NotchState.Idle;
        _expandedVolumeView.IsVisible = target == NotchState.Expanded && _state == NotchState.Expanded
                                        && _volumePeek && !_volSlider.IsEffectivelyVisible;
        PlacePrivacyDots(target == NotchState.Badge);
        // Minimize when idle: a small pill without shoulders while nothing is going on.
        bool mini = browserMini || (target == NotchState.Idle && _settings.MiniIdle && SensorsInUse.Count == 0);
        double w, h, r;
        switch (target)
        {
            case NotchState.Expanded:
                _expandedView.IsVisible = true;
                ShowQuietNote();
                if (_clockPanel.IsVisible) ShowToday();
                SpaceHome();
                // A page that just changed (rows added, a longer note) is still waiting for layout, and measuring
                // only the top would hand back its old size: lay out first.
                UpdateLayout();
                _expandedView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (ExpandedWidth, Math.Ceiling(_expandedView.DesiredSize.Height) + 18, 32);
                break;
            case NotchState.Share:
                _shareView.IsVisible = true;
                _shareView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (400, Math.Ceiling(_shareView.DesiredSize.Height) + 6, 28);
                break;
            case NotchState.Phone: (w, h, r) = (420, 160, 28); break;
            case NotchState.Task: (w, h, r) = (_taskExtra.IsVisible ? 490 : 420, 60, 26); break;
            case NotchState.Volume: (w, h, r) = (250, 34, 17); break;
            case NotchState.Peek: (w, h, r) = (320, 38, 19); break;
            case NotchState.Message: (w, h, r) = (350, 54, 24); break;
            case NotchState.Badge: (w, h, r) = (Look.Pick(110, 170), 30, 15); break;
            case NotchState.Download: (w, h, r) = (330, 34, 17); break;
            case NotchState.Media:
                (w, h, r) = (220 + Math.Max(0, _mediaBadgeLogos.Children.Count - 2) * (MediaBadgeLogoSize + BadgeLogoSpacing), 32, 16);
                break;
            case NotchState.Call: (w, h, r) = (190, 32, 16); break;
            case NotchState.Dictation: (w, h, r) = (190, 32, 16); break;
            case NotchState.Timer: (w, h, r) = (170, 30, 15); break;
            case NotchState.Focumon: (w, h, r) = (190, 30, 15); break;
            default: (w, h, r) = mini ? (96, 6, 3) : (150, 30, 15); break;
        }
        w += PlaceAiWings(target, mini); // Codenotch's rings at the ends of the closed notch
        w += VerityRoom(target, mini);

        // Media apps report every few seconds; only animate when the shape really changes.
        double scale = target == NotchState.Expanded ? ExpandedScale : CollapsedScale;
        scale = NotchPlacement.FitScale(scale, w + 2 * MaxEar, _screenWidth);
        if (!animate || w != _shapeW || h != _shapeH || r != _shapeR || mini != _mini || scale != _shapeScale)
        {
            double currentScale = _scaleSpring.Value;
            bool grow = w * h * scale * scale > _wSpring.Value * _hSpring.Value * currentScale * currentScale;
            bool resizeOpenHeight = _state == NotchState.Expanded && target == NotchState.Expanded
                                    && w == _shapeW && r == _shapeR && scale == _shapeScale;
            (_shapeW, _shapeH, _shapeR, _shapeScale) = (w, h, r, scale);
            double ear = mini ? 0 : MaxEar;
            if (animate) MoveShapeTo(w, h, r, ear, scale, grow, mini, resizeOpenHeight);
            else SnapShape(w, h, r, ear, scale);
        }

        if (target != _state || mini != _mini || !animate)
        {
            if (animate && target == NotchState.Expanded && _state != NotchState.Expanded && _expandedView.Opacity == 0)
                Unfold();
            SetView(_idleView, target == NotchState.Idle && !mini, animate);
            SetView(_compactView, target == NotchState.Media, animate);
            SetView(_timerView, target == NotchState.Timer, animate);
            SetView(_focumonView, target == NotchState.Focumon, animate);
            SetView(_callView, target == NotchState.Call, animate);
            SetView(_dictationView, target == NotchState.Dictation, animate);
            SetView(_volumeView, target == NotchState.Volume, animate);
            SetView(_downloadView, target == NotchState.Download, animate);
            SetView(_peekView, target == NotchState.Peek, animate);
            SetView(_messageView, target == NotchState.Message, animate);
            SetView(_badgeView, target == NotchState.Badge, animate);
            SetView(_taskView, target == NotchState.Task, animate);
            SetView(_phoneView, target == NotchState.Phone, animate);
            SetView(_shareView, target == NotchState.Share, animate);
            SetView(_expandedView, target == NotchState.Expanded, animate);
            if (_state == NotchState.Expanded && target != NotchState.Expanded) { EndTyping(); SaveNotes(); EndChoosingPlace(); }
            _state = target;
            _mini = mini;
            if (target == NotchState.Expanded)
            {
                _volumePeek = false;
                _volumeTimer.Stop();
                UpdateLive();
                RefreshDevices();
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_opened || _state != NotchState.Expanded) return;
                    RefreshOutputButtons();
                    ScanBluetooth();
                }, DispatcherPriority.Background);
                _live.Start();
            }
            else _live.Stop();
        }
        UpdateBackdrop();
        UpdateEqualizer();
        ShowSky();
        ShowVerity();
    }

    /// <summary>Room for a shape this high at this scale, including the spring's overshoot.</summary>
    static double NeededWindowHeight(double height, double scale = 1) => Math.Ceiling(Math.Max(height, 1) * Math.Max(scale, 0.1) * 1.15) + 2;

    double CollapsedScale => Math.Clamp(_settings.CollapsedScale, 0.7, 1.5);
    double ExpandedScale => Math.Clamp(_settings.ExpandedScale, 0.7, 1.5);

    void FitWindowWidth(double width)
    {
        double preferred = Math.Ceiling(BaseWidth * Math.Max(CollapsedScale, ExpandedScale));
        double needed = Math.Min(_screenWidth, Math.Max(preferred, Math.Ceiling(width) + NotchPlacement.WidthPadding));
        if (Width == needed) return;
        Width = needed;
        if (_opened) PositionWindow();
    }

    void SnapShape(double w, double h, double r, double ear, double scale)
    {
        _hoverBoost = false;
        _motionRunning = false;
        _shapeAnimating = false;
        _wSpring.Snap(w);
        _hSpring.Snap(h);
        _rSpring.Snap(r);
        _earSpring.Snap(ear);
        _scaleSpring.Snap(scale);
        _dragLean.Snap(0);
        _dragSkew.AngleX = 0;
        FitWindowWidth((w + 2 * MaxEar) * scale);
        ApplyShape(w, h, r, ear, scale);
        FitWindowHeight();
        UpdateHitRegion(w, h, scale);
    }

    void MoveShapeTo(double w, double h, double r, double ear, double scale, bool grow, bool mini, bool resizeOpenHeight)
    {
        _hoverBoost = false;
        // Opening is slightly softer on height than width for a liquid squash-and-stretch feel;
        // shrinking into a normal pill still snaps in with a touch of bounce, while mini glides cleanly onto the edge.
        double wResp = grow ? 0.34 : 0.30;
        double hResp = grow ? 0.38 : (mini ? 0.27 : 0.33);
        double wDamp = grow ? 0.63 : (mini ? 0.78 : 0.73);
        double hDamp = resizeOpenHeight || mini ? 1.0 : grow ? 0.60 : 0.75;
        double rDamp = grow ? 0.64 : (mini ? 1.0 : 0.75);
        double earDamp = grow ? 0.68 : (mini ? 1.0 : 0.85);
        double sDamp = grow ? 0.63 : (mini ? 0.82 : 0.74);
        _wSpring.Retarget(w, wResp, wDamp);
        _hSpring.Retarget(h, hResp, hDamp);
        _rSpring.Retarget(r, grow ? 0.36 : 0.30, rDamp);
        _earSpring.Retarget(ear, grow ? 0.34 : 0.28, earDamp);
        _scaleSpring.Retarget(scale, grow ? 0.36 : 0.30, sDamp);

        // Reserve room for both the outgoing and the incoming shape before anything moves.
        double needed = NeededWindowHeight(Math.Max(h, _hSpring.Value), Math.Max(scale, _scaleSpring.Value));
        if (needed > Height) Height = needed;
        // While it moves, the mouse region covers both where the shape is and where it is going.
        UpdateHitRegion(Math.Max(w, _wSpring.Value), Math.Max(h, _hSpring.Value), Math.Max(scale, _scaleSpring.Value));

        if (_wSpring.IsSettled && _hSpring.IsSettled && _rSpring.IsSettled && _earSpring.IsSettled && _scaleSpring.IsSettled && _dragLean.IsSettled)
        {
            _motionRunning = false;
            ApplyShape(w, h, r, ear, scale);
            if (_shapeAnimating) OnShapeSettled();
            return;
        }
        _shapeAnimating = true;
        StartMotion();
    }

    /// <summary>Gives the closed notch a subtle springy swell under the cursor before it opens.</summary>
    void SetHoverBoost(bool boost)
    {
        if (_hoverBoost == boost) return;
        _hoverBoost = boost;
        if (!_settings.Animations || _hovered || _shapeW <= 0) return;
        double targetW = boost ? _shapeW + 8 : _shapeW;
        double targetH = boost ? _shapeH + 2 : _shapeH;
        double targetR = boost ? _shapeR + 1 : _shapeR;
        double targetScale = boost ? _shapeScale * 1.03 : _shapeScale;
        _wSpring.Retarget(targetW, 0.28, 0.62);
        _hSpring.Retarget(targetH, 0.30, 0.62);
        _rSpring.Retarget(targetR, 0.28, 0.65);
        _scaleSpring.Retarget(targetScale, 0.28, 0.62);
        double needed = NeededWindowHeight(targetH, targetScale);
        if (needed > Height) Height = needed;
        UpdateHitRegion(targetW, targetH, targetScale);
        _shapeAnimating = true;
        StartMotion();
    }

    /// <summary>Jelly's poke: the notch squashes wide and flat and wobbles back (springs pick up jelly's damping).</summary>
    void Jiggle()
    {
        if (!_settings.Animations || _shapeW <= 0) return;
        double needed = NeededWindowHeight(_shapeH * 1.35, _shapeScale * 1.15);
        if (needed > Height) Height = needed;
        _wSpring.Kick(500, 0.30, 0.58);
        _hSpring.Kick(-300, 0.32, 0.58);
        _scaleSpring.Kick(0.5, 0.30, 0.58);
        UpdateHitRegion(_shapeW * 1.3, _shapeH * 1.35, _shapeScale * 1.15);
        _shapeAnimating = true;
        StartMotion();
    }

    void LeanWhileDragging(double offset)
    {
        _dragLean.Retarget(offset, 0.18, 0.65);
        StartMotion();
    }

    /// <summary>Adds a small physical bounce when a live pill updates in place (volume step, back-to-back peek).</summary>
    void PulseNotch(double widthVelocity = 28, double heightVelocity = 16, double scaleVelocity = 0.08)
    {
        if (!_settings.Animations || _hovered || _shapeW <= 0) return;
        double needed = NeededWindowHeight(_shapeH + 6, _shapeScale * 1.05);
        if (needed > Height) Height = needed;
        _wSpring.Kick(widthVelocity, 0.30, 0.58);
        _hSpring.Kick(heightVelocity, 0.32, 0.58);
        _scaleSpring.Kick(scaleVelocity, 0.30, 0.58);
        UpdateHitRegion(_shapeW + 12, _shapeH + 6, _shapeScale * 1.05);
        _shapeAnimating = true;
        StartMotion();
    }

    void FitWindowHeight() => Height = Math.Ceiling(Math.Max(_shapeH, 1) * _shapeScale) + 2;

    void OnShapeSettled()
    {
        // Keep the native surface stable on the final frame in both directions.
        // SnapShape can reclaim reserved space when laying out without animation.
        _shapeAnimating = false;
        UpdateHitRegion(_shapeW, _shapeH, _shapeScale, onlyIfChanged: true);
        UpdateEqualizer();
    }

    void StartMotion()
    {
        if (_motionRunning) return;
        _motionRunning = true;
        UpdateEqualizer(); // the bars rest while the shape moves
        _motionLastTick = Stopwatch.GetTimestamp();
        ScheduleAnimationFrame();
    }

    void StepShape(long now)
    {
        _selfTestMotionFrames?.Add(now);
        double dt = Math.Clamp((double)(now - _motionLastTick) / Stopwatch.Frequency, 0.001, 0.05);
        _motionLastTick = now;

        bool settled = _wSpring.Step(dt)
                     & _hSpring.Step(dt)
                     & _rSpring.Step(dt)
                     & _earSpring.Step(dt)
                     & _scaleSpring.Step(dt)
                     & _dragLean.Step(dt);

        double hMin = _mini && !_hoverBoost ? 1 : 12;
        double h = Math.Max(_shapeH < hMin ? 1 : hMin, _hSpring.Value);
        double scale = Math.Max(0.1, _scaleSpring.Value);
        double w = Math.Max(1, _wSpring.Value);
        double skew = 0;
        if (_dragLean.Value != 0 || _dragSkew.AngleX != 0)
        {
            double leanLimit = Math.Clamp((Width / scale - w) / 2 - MaxEar, 0, 18);
            double lean = Math.Clamp(_dragLean.Value, -leanLimit, leanLimit);
            skew = Math.Abs(lean);
            h = Math.Max(Math.Min(h, hMin), h * (1 - Math.Min(Math.Abs(_dragLean.Value) / 225, 0.08)));
            _dragSkew.AngleX = Math.Atan(lean / h) * 180 / Math.PI;
        }
        double frameWidth = w + 2 * (MaxEar + skew);
        scale = NotchPlacement.FitScale(scale, frameWidth, _screenWidth);
        EnsureFrameFits(frameWidth * scale, h * scale);
        ApplyShape(w, h, Math.Max(0, _rSpring.Value), Math.Max(0, _earSpring.Value), scale);

        if (settled)
        {
            // No further frame is asked for: nothing runs while the notch rests.
            _motionRunning = false;
            OnShapeSettled();
        }
    }

    void EnsureFrameFits(double shapeWidth, double shapeHeight)
    {
        // Jelly and interrupted springs can overshoot the target by more than the initial reservation.
        // Win32 clips the drawing to the hit region, so both the window and the region must fit this frame.
        double width = Math.Ceiling(shapeWidth) + NotchPlacement.WidthPadding;
        double height = Math.Ceiling(shapeHeight) + 2;
        // Only resize when the drawn frame needs it. Leave extra room for the next few
        // frames so a large overshoot doesn't repeatedly resize the native window.
        if (height > Height) Height = NeededWindowHeight(shapeHeight);
        bool resize = width > Width;
        if (resize) FitWindowWidth(shapeWidth + 16);
        if (!resize && width <= _hitRegionWidth && height <= _hitRegionHeight) return;
        _hitRegionWidth = Math.Max(_hitRegionWidth, width);
        _hitRegionHeight = Math.Max(_hitRegionHeight, height);
        ApplyHitRegion();
    }

    void ApplyShape(double w, double h, double radius, double earSize, double scale)
    {
        _notchScale.ScaleX = _notchScale.ScaleY = scale;
        double r = Math.Clamp(radius, 0, Math.Min(h, w / 2));
        double e = Math.Clamp(earSize, 0, Math.Min(MaxEar, h - r));
        // Scale and drag skew are render transforms and don't need new paths or layout.
        if (_drawnOutline == (w, h, r, e)) return;
        _drawnOutline = (w, h, r, e);
        double x = (BaseWidth - w) / 2;

        _island.Width = w;
        _island.Height = h;
        Canvas.SetLeft(_island, x);
        Canvas.SetTop(_island, 0);
        _islandContent.Clip = new RectangleGeometry(new Rect(0, -r, w, h + r)) { RadiusX = r, RadiusY = r };

        _backdrop.Width = w + 2 * MaxEar;
        _backdrop.Height = h;
        Canvas.SetLeft(_backdrop, x - MaxEar);
        Canvas.SetTop(_backdrop, 0);

        // Backdrop coordinates: the notch itself spans [left, right], the shoulders sit outside it.
        double left = MaxEar, right = MaxEar + w;
        var outline = new StreamGeometry();
        using (var c = outline.Open())
        {
            c.BeginFigure(new Point(left - e, 0), true);
            c.LineTo(new Point(right + e, 0));
            if (e > 0) c.ArcTo(new Point(right, e), new Size(e, e), 0, false, SweepDirection.CounterClockwise);
            c.LineTo(new Point(right, h - r));
            if (r > 0) c.ArcTo(new Point(right - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(left + r, h));
            if (r > 0) c.ArcTo(new Point(left, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(left, e));
            if (e > 0) c.ArcTo(new Point(left - e, 0), new Size(e, e), 0, false, SweepDirection.CounterClockwise);
            c.EndFigure(true);
        }
        _backdrop.Clip = outline;
        if (_edge.IsVisible) _edge.Data = outline;
        _prismRim?.ApplyOutline(outline, h);
    }

    /// <summary>Clicks land on the notch (with room for the spring's overshoot) and pass through beside it.</summary>
    void UpdateHitRegion(double w, double h, double notchScale, bool onlyIfChanged = false)
    {
        double width = (w + 2 * MaxEar) * 1.12 * notchScale;
        double height = h * notchScale * 1.15 + 2;
        if (onlyIfChanged && width == _hitRegionWidth && height == _hitRegionHeight) return;
        _hitRegionWidth = width;
        _hitRegionHeight = height;
        ApplyHitRegion();
    }

    void ApplyHitRegion()
    {
        if (!_opened || TryGetPlatformHandle() is not { } handle) return;
        double scale = RenderScaling;
        int left = (int)Math.Floor((Width - _hitRegionWidth) / 2 * scale);
        int right = (int)Math.Ceiling((Width + _hitRegionWidth) / 2 * scale);
        _platform.SetHitRegion(handle.Handle, left, 0, right - left, (int)Math.Ceiling(_hitRegionHeight * scale));
    }
}
