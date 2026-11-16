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

    enum NotchState { Idle, Timer, Media, Download, Peek, Volume, Task, Expanded }

    readonly INotchWindowPlatform _platform;
    readonly MediaService _media;
    readonly IAudioPlatform _audio;
    readonly IAutostartPlatform _autostart;
    // Read only on Windows for now: there the port shares the installed app's settings file and must not write to
    // it yet. With HOLM_DATA=folder (Debug builds) it runs on test data of its own and saves there. On Linux and
    // macOS there is no other Holm: the file is this app's own, and it saves.
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

    NotchState _state = NotchState.Idle;
    bool _motionRunning, _hovered, _opened, _volumePeek;
    /// <summary>Work that opening needs but that doesn't change the open notch's size: done once it has landed.</summary>
    bool _openSettling;
    long _motionLastTick;
    double _shapeW = 150, _shapeH = 30, _shapeR = 15;
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
        _island.Child = _islandContent;
        _root.Children.Add(_backdrop);
        _root.Children.Add(_island);
        Content = _root;

        _island.PointerEntered += (_, _) => { if (_taskVisible || _picking) return; _hovered = true; ApplyState(animate: true); };
        _island.PointerExited += (_, _) => { if (KeepOpenForTyping || _menuOpen) return; _hovered = false; ApplyState(animate: true); };

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
        UpdateHitRegion(_shapeW, _shapeH);
        StartClock();
        ShowIdleStatus();
        CheckReminders();
        StartDevices();
        StartClipboard();
        StartDownloads();
        StartDiskSpace();
        StartConnectivity();
        StartWeather();
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
        ForgetClipFiles();
        base.OnClosed(e);
    }

    /// <summary>Top centre of the primary screen.</summary>
    void PositionWindow()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen == null) return;
        int width = (int)Math.Round(BaseWidth * screen.Scaling);
        int top = screen.Bounds.Y;
#if DEBUG
        // HOLM_DEV_TOP=300 puts the notch that many pixels down, to run it next to an installed Holm while developing.
        if (int.TryParse(Environment.GetEnvironmentVariable("HOLM_DEV_TOP"), out int down)) top += down;
#endif
        Position = new PixelPoint(screen.Bounds.X + (screen.Bounds.Width - width) / 2, top);
    }

    void OnMediaChanged(MediaInfo? info, bool trackChanged)
    {
        _info = info;
        RefreshMediaUi();
        ApplyState(animate: true);
    }

    /// <summary>Works out what the notch shows right now and moves the shape and the views there.</summary>
    void ApplyState(bool animate)
    {
        animate &= _settings.Animations;
        var target = _hovered ? NotchState.Expanded
                   : _taskVisible ? NotchState.Task
                   : _volumePeek ? NotchState.Volume
                   : _peekActive ? NotchState.Peek
                   // A running download shows while nothing plays; over music only for a moment when it starts.
                   : _download != null && (_downloadPeek || _info?.IsPlaying != true) ? NotchState.Download
                   : _info?.IsPlaying == true ? NotchState.Media // a running timer shows in place of the equalizer
                   : _timer != null ? NotchState.Timer
                   : NotchState.Idle;

        double w, h, r;
        switch (target)
        {
            case NotchState.Expanded:
                // A page that just changed (rows added, a longer note) is still waiting for layout, and measuring
                // only the top would hand back its old size: lay out first.
                UpdateLayout();
                _expandedView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (ExpandedWidth, Math.Ceiling(_expandedView.DesiredSize.Height) + 18, 32);
                break;
            case NotchState.Task: (w, h, r) = (420, 60, 26); break;
            case NotchState.Volume: (w, h, r) = (250, 34, 17); break;
            case NotchState.Peek: (w, h, r) = (320, 38, 19); break;
            case NotchState.Download: (w, h, r) = (330, 34, 17); break;
            case NotchState.Media: (w, h, r) = (220, 32, 16); break;
            case NotchState.Timer: (w, h, r) = (170, 30, 15); break;
            default: (w, h, r) = (150, 30, 15); break;
        }

        // Media apps report every few seconds; only animate when the shape really changes.
        if (!animate || w != _shapeW || h != _shapeH || r != _shapeR)
        {
            bool grow = w * h > _wSpring.Value * _hSpring.Value;
            (_shapeW, _shapeH, _shapeR) = (w, h, r);
            if (animate) MoveShapeTo(w, h, r, MaxEar, grow);
            else SnapShape(w, h, r, MaxEar);
        }

        if (target != _state || !animate)
        {
            SetView(_idleView, target == NotchState.Idle, animate);
            SetView(_compactView, target == NotchState.Media, animate);
            SetView(_timerView, target == NotchState.Timer, animate);
            SetView(_volumeView, target == NotchState.Volume, animate);
            SetView(_downloadView, target == NotchState.Download, animate);
            SetView(_peekView, target == NotchState.Peek, animate);
            SetView(_taskView, target == NotchState.Task, animate);
            SetView(_expandedView, target == NotchState.Expanded, animate);
            if (_state == NotchState.Expanded && target != NotchState.Expanded) { EndTyping(); SaveNotes(); EndChoosingPlace(); }
            _state = target;
            if (target == NotchState.Expanded) { _volumePeek = false; _volumeTimer.Stop(); UpdateLive(); RefreshDevices(); _openSettling = true; _live.Start(); }
            else _live.Stop();
        }
        UpdateBackdrop();
        UpdateEqualizer();
    }

    /// <summary>Room for a shape this high, including the spring's overshoot.</summary>
    static double NeededWindowHeight(double height) => Math.Ceiling(Math.Max(height, 1) * 1.15) + 2;

    void SnapShape(double w, double h, double r, double ear)
    {
        _motionRunning = false;
        Height = NeededWindowHeight(h);
        _wSpring.Snap(w);
        _hSpring.Snap(h);
        _rSpring.Snap(r);
        _earSpring.Snap(ear);
        ApplyShape(w, h, r, ear);
        UpdateHitRegion(w, h);
    }

    void MoveShapeTo(double w, double h, double r, double ear, bool grow)
    {
        // Opening is slightly softer on height than width for a liquid squash-and-stretch feel;
        // shrinking into a normal pill still snaps in with a touch of bounce.
        _wSpring.Retarget(w, grow ? 0.34 : 0.30, grow ? 0.63 : 0.73);
        _hSpring.Retarget(h, grow ? 0.38 : 0.33, grow ? 0.60 : 0.75);
        _rSpring.Retarget(r, grow ? 0.36 : 0.30, grow ? 0.64 : 0.75);
        _earSpring.Retarget(ear, grow ? 0.34 : 0.28, grow ? 0.68 : 0.85);

        // Reserve room for both the outgoing and the incoming shape before anything moves.
        double needed = NeededWindowHeight(Math.Max(h, _hSpring.Value));
        if (needed > Height) Height = needed;

        // While it moves, the mouse region covers both where the shape is and where it is going.
        UpdateHitRegion(Math.Max(w, _wSpring.Value), Math.Max(h, _hSpring.Value));

        if (_wSpring.IsSettled && _hSpring.IsSettled && _rSpring.IsSettled && _earSpring.IsSettled)
        {
            _motionRunning = false;
            ApplyShape(w, h, r, ear);
            Height = NeededWindowHeight(h);
            UpdateHitRegion(w, h);
            return;
        }
        StartMotion();
    }

    /// <summary>Adds a small physical bounce when a live pill updates in place (volume step).</summary>
    void PulseNotch(double widthVelocity, double heightVelocity)
    {
        if (!_settings.Animations || _state == NotchState.Expanded) return;
        _wSpring.Kick(widthVelocity, 0.30, 0.58);
        _hSpring.Kick(heightVelocity, 0.32, 0.58);
        double needed = NeededWindowHeight(_shapeH + 6);
        if (needed > Height) Height = needed;
        UpdateHitRegion(_shapeW + 12, _shapeH + 6);
        StartMotion();
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
                     & _earSpring.Step(dt);

        ApplyShape(Math.Max(1, _wSpring.Value), Math.Max(12, _hSpring.Value), Math.Max(0, _rSpring.Value), Math.Max(0, _earSpring.Value));

        if (settled)
        {
            // No further frame is asked for: nothing runs while the notch rests.
            _motionRunning = false;
            Height = NeededWindowHeight(_shapeH);
            UpdateHitRegion(_shapeW, _shapeH);
            UpdateEqualizer();
            FinishOpening();
            return;
        }
        RequestAnimationFrame(OnMotionFrame);
    }

    void ApplyShape(double w, double h, double radius, double earSize)
    {
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
    }

    /// <summary>Clicks land on the notch (with room for the spring's overshoot) and pass through beside it.</summary>
    void UpdateHitRegion(double w, double h)
    {
        if (!_opened || TryGetPlatformHandle() is not { } handle) return;
        double scale = RenderScaling;
        double width = (w + 2 * MaxEar) * 1.12, height = h * 1.15 + 2;
        double x = (BaseWidth - width) / 2;
        _platform.SetHitRegion(handle.Handle,
            (int)Math.Floor(x * scale), 0, (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
    }
}
