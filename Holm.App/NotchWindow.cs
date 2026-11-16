using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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
    /// <summary>The notch itself: what is in it, and what the mouse opens.</summary>
    readonly Border _island = new() { Background = Brushes.Transparent };
    readonly Grid _islandContent = new();

    SpringValue _wSpring = new(150);
    SpringValue _hSpring = new(30);
    SpringValue _rSpring = new(15, 0.15, 2.0);
    SpringValue _earSpring = new(MaxEar, 0.1, 1.5);
    SpringValue _scaleSpring = new(1, 0.0008, 0.015);
    /// <summary>The notch's size preference (Customize › Collapsed / Expanded size), applied to everything in it.</summary>
    readonly ScaleTransform _notchScale = new(1, 1);

    NotchState _state = NotchState.Idle;
    bool _motionRunning, _hovered, _opened, _volumePeek, _mini;
    /// <summary>Work that opening needs but that doesn't change the open notch's size: done once it has landed.</summary>
    bool _openSettling;
    long _motionLastTick;
    double _shapeW = 150, _shapeH = 30, _shapeR = 15, _shapeScale = 1;
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
        InitHotKey(platform);
        InitClickAway(platform);
        InitFullscreen(platform);
        AppSettings.ReadOnly = OperatingSystem.IsWindows() && Diag.DevFolder == null;
        Spring.Bounce = _settings.Bounce ? _settings.BounceLevel : 0;

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
        Foreground = Brushes.White;

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
        InitActivity();
        InitStreaming();
        InitSendLater();
        InitUpdates();
        BuildFocumon();
        InitDictation(platform);
        InitPhone();
        BuildToday();
        // The rows of home in the WPF app's order (MainWindow.xaml), whichever feature added them first.
        foreach (var row in new Control[] { _mediaPanel, _clockPanel, _quietNote, _clearNotificationsBtn, _messageRows, _downloadRow, _laterRows, _usbRows, _callRow, _privacyRows, _timerRow, _shelfRow })
            _homePanel.Children.Remove(row);
        _homePanel.Children.AddRange(new Control[] { _mediaPanel, _clockPanel, _quietNote, _clearNotificationsBtn, _messageRows, _downloadRow, _laterRows, _usbRows, _callRow, _privacyRows, _timerRow, _shelfRow });
        _island.Child = _islandContent;
        _root.Children.Add(_backdrop);
        _root.Children.Add(_island);
        // The notch's size preference scales everything about the top centre, as the WPF app's layout transform does.
        _root.Width = BaseWidth;
        _root.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _root.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _root.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        _root.RenderTransform = _notchScale;
        Content = _root;
        Width = BaseWidth * Math.Max(CollapsedScale, ExpandedScale);

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
        PositionWindow();
        Screens.Changed += (_, _) => PositionWindow(); // a display came or went
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
        StartHotKey();
        StartScreenshots();
        StartUsb();
        StartVpn();
        StartFullscreen();
        StartPrivacy();
        StartMessages();
        StartSharing();
        StartSendLater();
        StartUpdates();
        StartFocumon();
        StartDictation();
        StartAiUsage();
        _audio.Start();
        _ = _media.StartAsync();
        StartSelfTest();
    }

    protected override void OnClosed(EventArgs e)
    {
        _eqTimer.Stop();
        _powerPlatform.Stop();
        _audio.Stop();
        _clipboardPlatform.Stop();
        StopDownloads();
        _connectivity.Stop();
        _capsLock.Stop();
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
        ForgetClipFiles();
        base.OnClosed(e);
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
        PlacePrivacyDots(target == NotchState.Badge);
        // Minimize when idle: a small pill without shoulders while nothing is going on.
        bool mini = browserMini || (target == NotchState.Idle && _settings.MiniIdle && SensorsInUse.Count == 0);
        double w, h, r;
        switch (target)
        {
            case NotchState.Expanded:
                ShowQuietNote();
                if (_clockPanel.IsVisible) ShowToday();
                // A page that just changed (rows added, a longer note) is still waiting for layout, and measuring
                // only the top would hand back its old size: lay out first.
                UpdateLayout();
                _expandedView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (ExpandedWidth, Math.Ceiling(_expandedView.DesiredSize.Height) + 18, 32);
                break;
            case NotchState.Share:
                _shareView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (400, Math.Ceiling(_shareView.DesiredSize.Height) + 6, 28);
                break;
            case NotchState.Phone: (w, h, r) = (420, 160, 28); break;
            case NotchState.Task: (w, h, r) = (_taskExtra.IsVisible ? 490 : 420, 60, 26); break;
            case NotchState.Volume: (w, h, r) = (250, 34, 17); break;
            case NotchState.Peek: (w, h, r) = (320, 38, 19); break;
            case NotchState.Message: (w, h, r) = (350, 54, 24); break;
            case NotchState.Badge: (w, h, r) = (170, 30, 15); break;
            case NotchState.Download: (w, h, r) = (330, 34, 17); break;
            case NotchState.Media: (w, h, r) = (220, 32, 16); break;
            case NotchState.Call: (w, h, r) = (190, 32, 16); break;
            case NotchState.Dictation: (w, h, r) = (190, 32, 16); break;
            case NotchState.Timer: (w, h, r) = (170, 30, 15); break;
            case NotchState.Focumon: (w, h, r) = (190, 30, 15); break;
            default: (w, h, r) = mini ? (96, 6, 3) : (150, 30, 15); break;
        }

        // Media apps report every few seconds; only animate when the shape really changes.
        double scale = target == NotchState.Expanded ? ExpandedScale : CollapsedScale;
        if (!animate || w != _shapeW || h != _shapeH || r != _shapeR || mini != _mini || scale != _shapeScale)
        {
            double currentScale = _scaleSpring.Value;
            bool grow = w * h * scale * scale > _wSpring.Value * _hSpring.Value * currentScale * currentScale;
            (_shapeW, _shapeH, _shapeR, _shapeScale) = (w, h, r, scale);
            double ear = mini ? 0 : MaxEar;
            if (animate) MoveShapeTo(w, h, r, ear, scale, grow, mini);
            else SnapShape(w, h, r, ear, scale);
        }

        if (target != _state || mini != _mini || !animate)
        {
            if (animate && target == NotchState.Expanded && _state != NotchState.Expanded) Unfold();
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
            if (target == NotchState.Expanded) { _volumePeek = false; _volumeTimer.Stop(); UpdateLive(); RefreshDevices(); RefreshOutputButtons(); _openSettling = true; _live.Start(); }
            else _live.Stop();
        }
        UpdateBackdrop();
        UpdateEqualizer();
        ShowSky();
        ShowAiDock();
    }

    /// <summary>Room for a shape this high at this scale, including the spring's overshoot.</summary>
    static double NeededWindowHeight(double height, double scale = 1) => Math.Ceiling(Math.Max(height, 1) * Math.Max(scale, 0.1) * 1.15) + 2;

    double CollapsedScale => Math.Clamp(_settings.CollapsedScale, 0.7, 1.5);
    double ExpandedScale => Math.Clamp(_settings.ExpandedScale, 0.7, 1.5);

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
        ApplyShape(w, h, r, ear, scale);
        FitWindowHeight();
        UpdateHitRegion(w, h, scale);
    }

    void MoveShapeTo(double w, double h, double r, double ear, double scale, bool grow, bool mini)
    {
        _hoverBoost = false;
        // Opening is slightly softer on height than width for a liquid squash-and-stretch feel;
        // shrinking into a normal pill still snaps in with a touch of bounce, while mini glides cleanly onto the edge.
        double wResp = grow ? 0.34 : 0.30;
        double hResp = grow ? 0.38 : (mini ? 0.27 : 0.33);
        double wDamp = grow ? 0.63 : (mini ? 0.78 : 0.73);
        double hDamp = grow ? 0.60 : (mini ? 1.0 : 0.75);
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

        if (_wSpring.IsSettled && _hSpring.IsSettled && _rSpring.IsSettled && _earSpring.IsSettled && _scaleSpring.IsSettled)
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
        FitWindowHeight();
        _shapeAnimating = false;
        UpdateHitRegion(_shapeW, _shapeH, _shapeScale);
        UpdateEqualizer();
        FinishOpening();
    }

    /// <summary>The open notch has landed: now the work that would have cost frames on the way.</summary>
    void FinishOpening()
    {
        if (!_openSettling) return;
        _openSettling = false;
        if (_state != NotchState.Expanded) return;
        ScanBluetooth();
    }

    void StartMotion()
    {
        if (_motionRunning) return;
        _motionRunning = true;
        UpdateEqualizer(); // the bars rest while the shape moves
        _motionLastTick = Stopwatch.GetTimestamp();
        RequestAnimationFrame(OnMotionFrame);
    }

    void OnMotionFrame(TimeSpan _)
    {
        if (!_motionRunning) return;
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((double)(now - _motionLastTick) / Stopwatch.Frequency, 0.001, 0.05);
        _motionLastTick = now;

        bool settled = _wSpring.Step(dt)
                     & _hSpring.Step(dt)
                     & _rSpring.Step(dt)
                     & _earSpring.Step(dt)
                     & _scaleSpring.Step(dt);

        double hMin = _mini && !_hoverBoost ? 1 : 12;
        double h = Math.Max(_shapeH < hMin ? 1 : hMin, _hSpring.Value);
        double scale = Math.Max(0.1, _scaleSpring.Value);
        double frameNeed = NeededWindowHeight(h, scale);
        if (frameNeed > Height) Height = frameNeed;
        ApplyShape(Math.Max(1, _wSpring.Value), h, Math.Max(0, _rSpring.Value), Math.Max(0, _earSpring.Value), scale);

        if (settled)
        {
            // No further frame is asked for: nothing runs while the notch rests.
            _motionRunning = false;
            OnShapeSettled();
            return;
        }
        RequestAnimationFrame(OnMotionFrame);
    }

    void ApplyShape(double w, double h, double radius, double earSize, double scale)
    {
        _notchScale.ScaleX = _notchScale.ScaleY = scale;
        double r = Math.Clamp(radius, 0, Math.Min(h, w / 2));
        double e = Math.Clamp(earSize, 0, Math.Min(MaxEar, h - r));
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
        PlaceAiDock(w, h, e);
    }

    /// <summary>Clicks land on the notch (with room for the spring's overshoot) and pass through beside it.</summary>
    void UpdateHitRegion(double w, double h, double notchScale)
    {
        if (!_opened || TryGetPlatformHandle() is not { } handle) return;
        double scale = RenderScaling;
        double width = (w + 2 * MaxEar) * 1.12 * notchScale, height = h * notchScale * 1.15 + 2;
        double x = (Width - width) / 2;
        var notchRect = new HitRect((int)Math.Floor(x * scale), 0, (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
        if (AiDockBounds(w, h, notchScale) is { } bubble)
        {
            var bubbleRect = new HitRect((int)Math.Floor(bubble.X * scale), (int)Math.Floor(bubble.Y * scale),
                (int)Math.Ceiling(bubble.W * scale), (int)Math.Ceiling(bubble.H * scale));
            _platform.SetHitRegions(handle.Handle, new[] { notchRect, bubbleRect });
            return;
        }
        _platform.SetHitRegion(handle.Handle, notchRect.X, notchRect.Y, notchRect.Width, notchRect.Height);
    }
}
