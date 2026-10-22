using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

public partial class MainWindow : Window
{
    enum NotchState { None, Idle, Badge, Media, Peek, Message, Volume, Task, Share, Expanded }

    const double ExpandedWidth = 440;
    static readonly Brush DimBrush = Frozen(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));
    static readonly Brush GreenBrush = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
    static readonly Brush RedBrush = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    static readonly Brush TeamsBrush = Frozen(Color.FromRgb(0x5B, 0x5F, 0xC7));
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly double[] BarRest = { 0.45, 0.85, 0.6, 0.75 }, BarLow = { 0.3, 0.4, 0.25, 0.35 }, BarSeconds = { 0.42, 0.55, 0.37, 0.49 };

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(MainWindow), new PropertyMetadata(15.0, (d, _) => ((MainWindow)d).UpdateShape()));

    /// <summary>Bottom corner radius of the notch; a DP so it can be animated together with the size.</summary>
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    const double MaxEar = 10;

    public static readonly DependencyProperty EarProperty = DependencyProperty.Register(
        nameof(Ear), typeof(double), typeof(MainWindow), new PropertyMetadata(MaxEar, (d, _) => ((MainWindow)d).UpdateShape()));

    /// <summary>Radius of the concave shoulders at the top corners (0 in the mini idle bar).</summary>
    public double Ear { get => (double)GetValue(EarProperty); set => SetValue(EarProperty, value); }

    readonly AppSettings _settings = AppSettings.Load();
    readonly MediaService _media;
    readonly TeamsService _teams;
    readonly VolumeService _volume = new();
    readonly DispatcherTimer _expandDelay, _collapseDelay, _peekTimer, _liveTimer, _watchTimer, _btSoon, _btLate, _btPoll, _trimTimer, _fitTimer, _volumeTimer;
    readonly ScaleTransform[] _bars;
    readonly BatteryGlyph PeekBattery = new();
    readonly MiniSlider Progress = new();
    readonly MiniSlider VolSlider = new();
    readonly MiniSlider VolPeekBar = new() { IsInteractive = false };
    readonly MiniSlider TaskProgress = new() { IsInteractive = false };
    bool _volumePeek;

    IntPtr _hwnd, _powerNotifyA, _powerNotifyB, _monitor;
    NotchState _state = NotchState.None;
    double _shapeW = -1, _shapeH = -1, _shapeR = -1;
    bool _expanded, _peekActive, _peekMessage, _hiddenForFullscreen, _eqRunning, _shapeAnimating;
    MediaInfo? _info;
    ImageSource? _shownArt, _shownBackdrop;
    Border _artFront, _artBack;
    PowerInfo _power;
    bool _powerKnown, _btKnown;
    List<BtDevice> _bt = new();
    IReadOnlyList<TeamsMessage> _messages = Array.Empty<TeamsMessage>();
    AiTarget? _ai; // what the AI button opens, once the app list has been searched
    Task<AiTarget?>? _aiLookup;
    DateTime _aiResolvedAt;

    public MainWindow()
    {
        InitializeComponent();
        PeekBatteryHost.Child = PeekBattery;
        ProgressHost.Child = Progress;
        VolumeHost.Child = VolSlider;
        VolPeekHost.Child = VolPeekBar;
        TaskProgressHost.Child = TaskProgress;
        (_artFront, _artBack) = (ArtLayerA, ArtLayerB);
        _bars = new[] { Bar1, Bar2, Bar3, Bar4 }.Select(b => (ScaleTransform)b.RenderTransform).ToArray();

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        _teams = new TeamsService(Dispatcher);
        _teams.Changed += OnMessagesChanged;

        // Every timer below is either short-lived or only runs while the notch is open.
        // Not while a question or a transfer is shown: opening the notch would hide its buttons.
        _expandDelay = MakeTimer(140, () => { if (Island.IsMouseOver && !TaskShowing && !_sharing) SetExpanded(true); }, oneShot: true);
        _collapseDelay = MakeTimer(300, () =>
        {
            // Dragging a slider or menu open: try again shortly.
            if (Mouse.Captured != null || Island.ContextMenu.IsOpen) { _collapseDelay!.Start(); return; }
            if (!Island.IsMouseOver) SetExpanded(false);
        }, oneShot: true);
        _peekTimer = MakeTimer(3000, () => { _peekActive = false; ApplyState(); }, oneShot: true);
        _liveTimer = MakeTimer(500, UpdateLive);
        _watchTimer = MakeTimer(1500, WatchFullscreen);
        _btSoon = MakeTimer(1500, ScanBluetooth, oneShot: true);
        _btLate = MakeTimer(8000, ScanBluetooth, oneShot: true);
        _btPoll = MakeTimer(60_000, ScanBluetooth);
        _trimTimer = MakeTimer(6000, () => { if (!_expanded) Native.TrimWorkingSet(); }, oneShot: true);
        _fitTimer = MakeTimer(500, OnShapeSettled, oneShot: true);
        _volumeTimer = MakeTimer(1600, () => { _volumePeek = false; ApplyState(); }, oneShot: true);
        InitSharing();

        Progress.ValueCommitted += v =>
        {
            if (_info is { } info && info.Duration > TimeSpan.Zero)
            {
                var target = TimeSpan.FromTicks((long)(info.Duration.Ticks * v));
                info.Position = target;
                info.PositionStamp = DateTimeOffset.Now;
                _media.Seek(target);
            }
        };
        VolSlider.ValueChanging += v => { _volume.SetLevel((float)v); UpdateVolumeIcon(v, false); };
        Island.SizeChanged += (_, _) => UpdateShape();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    DispatcherTimer MakeTimer(int milliseconds, Action tick, bool oneShot = false)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            if (oneShot) timer.Stop();
            tick();
        };
        return timer;
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ───────────────────────── window plumbing ─────────────────────────

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Tool window: no Alt+Tab entry. No-activate: clicking the notch never steals focus from your app.
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Native.AddClipboardFormatListener(_hwnd); // a copied AirDrop code pops up the notch
        _powerNotifyA = Native.RegisterPowerSettingNotification(_hwnd, ref Native.GUID_BATTERY_PERCENTAGE_REMAINING, 0);
        _powerNotifyB = Native.RegisterPowerSettingNotification(_hwnd, ref Native.GUID_ACDC_POWER_SOURCE, 0);
        Native.EnableEcoQoS();
        PositionWindow();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        ReadPower();
        ScanBluetooth();
        ApplyState(animate: false);
        _watchTimer.Start();
        _btPoll.Start();
        _trimTimer.Start();
        UpdateAiButton();
        Dispatcher.InvokeAsync(() => ResolveAi(force: true), DispatcherPriority.ApplicationIdle);
        _ = _media.StartAsync();
        if (_settings.TeamsMessages) _ = _teams.StartAsync(ask: true);

        _volume.Changed += (level, muted) => Dispatcher.InvokeAsync(() => OnVolumeChanged(level, muted));
        _volume.DefaultDeviceChanged += () => Dispatcher.InvokeAsync(_volume.Bind);
        _volume.Bind();
        StartSharing();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        if (_powerNotifyA != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_powerNotifyA);
        if (_powerNotifyB != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_powerNotifyB);
        if (_hwnd != IntPtr.Zero) Native.RemoveClipboardFormatListener(_hwnd);
        _volume.Shutdown();
        _teams.Stop();
        StopSharing();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_POWERBROADCAST:
                int ev = wParam.ToInt32();
                if (ev == Native.PBT_POWERSETTINGCHANGE || ev == Native.PBT_APMPOWERSTATUSCHANGE)
                    Dispatcher.InvokeAsync(ReadPower, DispatcherPriority.Background);
                break;

            case Native.WM_DEVICECHANGE when wParam.ToInt32() == Native.DBT_DEVNODES_CHANGED:
                // Something was (dis)connected. Windows fills in the battery level a few seconds later, so look twice.
                _btSoon.Stop(); _btSoon.Start();
                _btLate.Stop(); _btLate.Start();
                break;

            case Native.WM_DISPLAYCHANGE:
            case Native.WM_DPICHANGED:
                Dispatcher.InvokeAsync(PositionWindow, DispatcherPriority.Background);
                break;

            case Native.WM_CLIPBOARDUPDATE:
                OnClipboardChanged();
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Top centre of the chosen display (main display if none was chosen or it is unplugged), in physical pixels.
    /// Moving to a display with another scaling triggers WM_DPICHANGED, which calls this again with the new DPI.
    /// </summary>
    void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        var screens = Native.Screens();
        var screen = screens.FirstOrDefault(s => string.Equals(s.Device, _settings.Monitor, StringComparison.OrdinalIgnoreCase))
                  ?? screens.FirstOrDefault(s => s.Primary) ?? screens.FirstOrDefault();
        if (screen == null) return;
        _monitor = screen.Handle;

        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(Width * dpi.DpiScaleX), height = (int)Math.Round(Height * dpi.DpiScaleY);
        int x = screen.Bounds.Left + (screen.Bounds.Right - screen.Bounds.Left - width) / 2;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, screen.Bounds.Top, width, height, Native.SWP_NOACTIVATE);
    }

    /// <summary>Gets out of the way of games and fullscreen videos, and stays above other topmost windows.</summary>
    void WatchFullscreen()
    {
        // The shell's answer covers all monitors, so also check that the fullscreen app is on ours.
        bool fullscreen = Native.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4
            && Native.MonitorFromWindow(Native.GetForegroundWindow(), 2 /* NEAREST */) == _monitor;
        if (fullscreen != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = fullscreen;
            if (fullscreen)
            {
                _expanded = false;
                _peekActive = false;
                _liveTimer.Stop();
                ApplyState(animate: false);
                UpdateBackdrop();
                Hide();
            }
            else
            {
                Show();
                PositionWindow();
            }
            UpdateEqualizer();
        }
        // Not while the menu is open: the notch would jump in front of it and the menu flicker.
        if (!fullscreen && _hwnd != IntPtr.Zero && !Island.ContextMenu.IsOpen)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        if (_messages.Count > 0 && TeamsInFront()) _teams.MarkRead();
    }

    static bool TeamsInFront()
        => Native.ProcessNameOf(Native.GetForegroundWindow()).Contains("teams", StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── states & animation ─────────────────────────

    void SetExpanded(bool expand)
    {
        if (_expanded == expand || _hiddenForFullscreen) return;
        _expanded = expand;
        Diag.Log(expand ? "Expanded" : "Collapsed");
        if (expand)
        {
            _peekActive = false;
            _peekTimer.Stop();
            _volumePeek = false;
            _volumeTimer.Stop();
            _trimTimer.Stop();
            RefreshMediaUi();
            RefreshDevices();
            ResolveAi(force: false);
            // Access may have been granted in Settings since we last asked.
            if (_settings.TeamsMessages && !_teams.IsRunning && _teams.Access != TeamsService.AccessState.Unavailable)
                _ = _teams.StartAsync(ask: false);
            _liveTimer.Start();
        }
        else
        {
            _liveTimer.Stop();
            _trimTimer.Start();
        }
        ApplyState();
        UpdateBackdrop();
    }

    void ApplyState(bool animate = true)
    {
        var target = _expanded ? NotchState.Expanded
                   : _sharing ? NotchState.Share
                   : TaskShowing ? NotchState.Task
                   : _volumePeek ? NotchState.Volume
                   : _peekActive ? (_peekMessage ? NotchState.Message : NotchState.Peek)
                   : _info?.IsPlaying == true ? NotchState.Media
                   : _messages.Count > 0 ? NotchState.Badge
                   : NotchState.Idle;
        bool mini = target == NotchState.Idle && _settings.MiniIdle;

        double w, h, r;
        switch (target)
        {
            case NotchState.Expanded:
                ExpandedView.Visibility = Visibility.Visible;
                ExpandedView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (ExpandedWidth, Math.Ceiling(ExpandedView.DesiredSize.Height) + 18, 32);
                break;
            case NotchState.Share:
                ShareView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
                (w, h, r) = (400, Math.Ceiling(ShareView.DesiredSize.Height) + 6, 28);
                break;
            case NotchState.Task: (w, h, r) = (420, 60, 26); break;
            case NotchState.Volume: (w, h, r) = (250, 34, 17); break;
            case NotchState.Peek: (w, h, r) = (320, 38, 19); break;
            case NotchState.Message: (w, h, r) = (350, 54, 24); break;
            case NotchState.Media: (w, h, r) = (220, 32, 16); break;
            case NotchState.Badge: (w, h, r) = (170, 30, 15); break;
            default: (w, h, r) = mini ? (96, 6, 3) : (150, 30, 15); break;
        }

        // Media apps report every few seconds; only animate when the shape really changes.
        if (!animate || w != _shapeW || h != _shapeH || r != _shapeR)
        {
            bool grow = w * h > Island.ActualWidth * Island.ActualHeight;
            (_shapeW, _shapeH, _shapeR) = (w, h, r);
            Animate(Island, WidthProperty, w, grow, animate);
            Animate(Island, HeightProperty, h, grow, animate);
            Animate(this, RadiusProperty, r, grow, animate);
            Animate(this, EarProperty, mini ? 0 : MaxEar, grow, animate);

            // The window is a layered (per-pixel alpha) window: every animation frame copies its whole
            // surface. So it is only ever as tall as the notch. Its width and top-left never change,
            // which keeps the notch from jumping while the window is resized.
            if (!animate) FitWindowHeight();
            else
            {
                double needed = Math.Ceiling(h * 1.12) + 2; // room for the spring overshoot
                if (needed > Height) Height = needed;
                _shapeAnimating = true;
                _fitTimer.Stop();
                _fitTimer.Interval = TimeSpan.FromMilliseconds((grow ? 480 : 340) + 60);
                _fitTimer.Start();
            }
        }

        if (target != _state)
        {
            SetView(CompactView, target == NotchState.Media, animate);
            SetView(BadgeView, target == NotchState.Badge, animate);
            SetView(PeekView, target == NotchState.Peek, animate);
            SetView(MessageView, target == NotchState.Message, animate);
            SetView(VolumeView, target == NotchState.Volume, animate);
            SetView(TaskView, target == NotchState.Task, animate);
            SetView(ShareView, target == NotchState.Share, animate);
            SetView(ExpandedView, target == NotchState.Expanded, animate);
            _state = target;
        }
        UpdateEqualizer();
    }

    void FitWindowHeight() => Height = Math.Ceiling(Math.Max(_shapeH, 1)) + 2;

    void OnShapeSettled()
    {
        FitWindowHeight();
        _shapeAnimating = false;
        UpdateEqualizer();
    }

    static void Animate(IAnimatable target, DependencyProperty property, double to, bool grow, bool animate)
    {
        if (!animate)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            return;
        }
        // Growing gets a little spring overshoot like the iPhone; shrinking must not overshoot (height would go negative).
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(grow ? 480 : 340))
        {
            EasingFunction = grow
                ? new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    void SetView(UIElement view, bool show, bool animate)
    {
        view.IsHitTestVisible = show;
        if (show) view.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(!animate ? 0 : show ? 220 : 110))
        {
            BeginTime = TimeSpan.FromMilliseconds(show && animate ? 110 : 0),
        };
        if (!show && view == ExpandedView)
            fade.Completed += (_, _) => { if (_state != NotchState.Expanded) ExpandedView.Visibility = Visibility.Collapsed; };
        view.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Cuts the outline (notch + shoulders) out of the backdrop. Runs on every animation frame,
    /// because the outline follows the animated size, radius and shoulder size.
    /// </summary>
    void UpdateShape()
    {
        double w = Island.ActualWidth, h = Island.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = Math.Clamp(Radius, 0, Math.Min(h, w / 2));
        double e = Math.Clamp(Ear, 0, Math.Min(MaxEar, h - r));

        IslandContent.Clip = new RectangleGeometry(new Rect(0, -r, w, h + r), r, r);

        // Backdrop coordinates: the notch itself spans [left, right], the shoulders sit outside it.
        double left = MaxEar, right = MaxEar + w;
        var outline = new StreamGeometry();
        using (var c = outline.Open())
        {
            c.BeginFigure(new Point(left - e, 0), true, true);
            c.LineTo(new Point(right + e, 0), false, false);
            if (e > 0) c.ArcTo(new Point(right, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
            c.LineTo(new Point(right, h - r), false, false);
            if (r > 0) c.ArcTo(new Point(right - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(left + r, h), false, false);
            if (r > 0) c.ArcTo(new Point(left, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(left, e), false, false);
            if (e > 0) c.ArcTo(new Point(left - e, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
        }
        outline.Freeze();
        Backdrop.Clip = outline;
    }

    /// <summary>
    /// Washes the notch in the colours of the cover while something plays (or while the player is open).
    /// A new cover fades in on top of the old one.
    /// </summary>
    void UpdateBackdrop()
    {
        var wanted = _info is { Backdrop: { } art } info && (info.IsPlaying || _expanded) ? art : null;
        if (ReferenceEquals(wanted, _shownBackdrop)) return;
        _shownBackdrop = wanted;

        if (wanted == null || _info == null)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
            ArtLayerA.BeginAnimation(OpacityProperty, fadeOut);
            ArtLayerB.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        (_artFront, _artBack) = (_artBack, _artFront);
        var brush = new ImageBrush(wanted) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        _artFront.Background = brush;
        ((Border)_artFront.Child).Opacity = _info.BackdropShade;
        Panel.SetZIndex(_artFront, 2);
        Panel.SetZIndex(_artBack, 1);

        var back = _artBack;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = new SineEase() };
        fadeIn.Completed += (_, _) =>
        {
            if (back != _artBack) return; // another cover arrived meanwhile
            back.BeginAnimation(OpacityProperty, null);
            back.Opacity = 0;
        };
        _artFront.BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Bouncing bars while music plays. This is the only continuous animation in the app, so it runs
    /// at a low frame rate: the window is per-pixel transparent and every frame has to be copied
    /// back from the GPU.
    /// </summary>
    void UpdateEqualizer()
    {
        bool run = _state == NotchState.Media && _settings.Animations && !_hiddenForFullscreen && !_shapeAnimating;
        if (run == _eqRunning) return;
        _eqRunning = run;

        for (int i = 0; i < _bars.Length; i++)
        {
            if (run)
            {
                var bounce = new DoubleAnimation(BarLow[i], 1.0, TimeSpan.FromSeconds(BarSeconds[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(bounce, 12);
                _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
            }
            else
            {
                _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _bars[i].ScaleY = BarRest[i];
            }
        }
    }

    void ShowPeek(string? glyph, string text, string value, int? battery, bool charging, Brush? valueBrush)
    {
        if (_hiddenForFullscreen) return;
        if (_expanded) { RefreshDevices(); return; }

        PeekIcon.Text = glyph ?? "";
        PeekIcon.Visibility = glyph == null ? Visibility.Collapsed : Visibility.Visible;
        PeekText.Text = text;
        PeekValue.Text = value;
        PeekValue.Foreground = valueBrush ?? Brushes.White;
        PeekBatteryHost.Visibility = battery == null ? Visibility.Collapsed : Visibility.Visible;
        PeekBattery.Level = battery ?? 0;
        PeekBattery.Charging = charging;

        _peekActive = true;
        _peekMessage = false;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(3);
        _peekTimer.Start();
        ApplyState();
    }

    /// <summary>Pops out a message that just came in. The caller applies the state.</summary>
    void ShowMessage(TeamsMessage message)
    {
        if (_hiddenForFullscreen || _expanded) return;
        MessageLogoHost.Child = TeamsLogo(28);
        MessageTitle.Text = message.Title;
        MessageText.Text = message.Text;
        MessageText.Visibility = message.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _peekActive = true;
        _peekMessage = true;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(5); // long enough to read a line
        _peekTimer.Start();
    }

    // ───────────────────────── data sources ─────────────────────────

    void ReadPower()
    {
        var now = PowerInfo.Read();
        var before = _power;
        bool known = _powerKnown;
        _power = now;
        _powerKnown = true;

        if (known && now.HasBattery)
        {
            if (now.OnAc && !before.OnAc)
                ShowPeek(null, "Charging", $"{now.Percent}%", now.Percent, true, GreenBrush);
            else if (!now.OnAc && now.Percent < before.Percent && now.Percent is 20 or 10 or 5)
                ShowPeek(null, "Low battery", $"{now.Percent}%", now.Percent, false, RedBrush);
        }
        if (_expanded) RefreshDevices();
    }

    void ScanBluetooth()
    {
        List<BtDevice> found;
        try { found = BluetoothBattery.Scan(); }
        catch (Exception ex) { Diag.Log("Bluetooth scan failed: " + ex.Message); return; }

        var previous = _bt.ToDictionary(d => d.Address, StringComparer.OrdinalIgnoreCase);
        _bt = found;
        Diag.Log("Bluetooth: " + string.Join(", ", found.Select(d => $"{d.Name}={d.Battery}% connected={d.Connected}")));

        if (_btKnown)
        {
            var arrived = found.FirstOrDefault(d => d.IsConnected && !(previous.TryGetValue(d.Address, out var p) && p.IsConnected));
            var low = found.FirstOrDefault(d => d.IsConnected && d.Battery <= 15
                                             && previous.TryGetValue(d.Address, out var p) && p.Battery > 15);
            if (arrived != null)
                ShowPeek(arrived.Glyph, arrived.Name, $"{arrived.Battery}%", arrived.Battery, false, null);
            else if (low != null)
                ShowPeek(low.Glyph, low.Name, $"{low.Battery}%", low.Battery, false, RedBrush);
        }
        _btKnown = true;
        if (_expanded) RefreshDevices();
    }

    void OnMediaChanged(MediaInfo? info, bool trackChanged)
    {
        var oldAccent = _info?.Accent;
        _info = info;
        Diag.Log(info == null ? "Media: none" : $"Media: {info.App} | {info.Title} | {info.Artist} | playing={info.IsPlaying} | art={info.Art != null}");

        var accent = info?.Accent ?? Colors.White;
        if (oldAccent != accent || Progress.Fill == Brushes.White)
        {
            var brush = Frozen(accent);
            foreach (var bar in new[] { Bar1, Bar2, Bar3, Bar4 }) bar.Fill = brush;
            Progress.Fill = brush;
            VolSlider.Fill = brush;
        }
        if (!ReferenceEquals(_shownArt, info?.Art) || info == null)
        {
            _shownArt = info?.Art;
            CompactArt.Background = ArtBrush(info?.Art);
        }

        if (_expanded) RefreshMediaUi();
        ApplyState();
        UpdateBackdrop();
    }

    void OnMessagesChanged(TeamsMessage? arrived)
    {
        // Already looking at Teams: nothing is unread. (MarkRead calls back here with the list cleared.)
        if (_teams.Messages.Count > 0 && TeamsInFront()) { _teams.MarkRead(); return; }

        _messages = _teams.Messages;
        Diag.Log($"Teams: {_messages.Count} unread" + (arrived != null ? ", new one" : ""));
        RefreshMessages();
        if (arrived != null) ShowMessage(arrived);
        ApplyState();
    }

    /// <summary>The count on the badges and the newest message in the open notch.</summary>
    void RefreshMessages()
    {
        int count = _messages.Count;
        BadgeText.Text = MediaBadgeText.Text = RowCount.Text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        MediaBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageRow.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count == 0) return;

        var latest = _messages[^1];
        BadgeLogoHost.Child = TeamsLogo(18);
        RowLogoHost.Child = TeamsLogo(28);
        RowTitle.Text = latest.Title;
        RowText.Text = latest.Text;
        RowText.Visibility = latest.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Teams' own icon once a notification has brought it along, a purple chat bubble until then.</summary>
    FrameworkElement TeamsLogo(double size)
    {
        if (_teams.Logo is { } logo)
        {
            var image = new Image { Source = logo, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = TeamsBrush,
            Child = new TextBlock
            {
                Text = "\uE8BD", FontFamily = IconFont, FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    // ───────────────────────── AI button ─────────────────────────

    /// <summary>Shows the AI button with the best we know so far: the app's icon once the lookup is done.</summary>
    void UpdateAiButton()
    {
        var target = _ai ?? AiLauncher.Quick(_settings.Ai, _settings.AiPath);
        AiBtn.Visibility = target != null ? Visibility.Visible : Visibility.Collapsed;
        if (target == null) return;
        AiIconHost.Child = AiLauncher.IconFor(target, 18);
        AiBtn.ToolTip = "Open " + target.Name;
    }

    /// <summary>Looks up the installed app in the background; again every few minutes, in case it was installed meanwhile.</summary>
    async void ResolveAi(bool force)
    {
        if (!force && DateTime.UtcNow - _aiResolvedAt < TimeSpan.FromMinutes(5)) return;
        _aiResolvedAt = DateTime.UtcNow;
        if (force) _ai = null; // another choice: show it right away, its icon follows
        var lookup = AiLauncher.ResolveAsync(_settings.Ai, _settings.AiPath);
        _aiLookup = lookup;
        UpdateAiButton();
        AiTarget? found = null;
        try { found = await lookup; } catch (Exception ex) { Diag.Log("AI lookup failed: " + ex.Message); }
        if (_aiLookup != lookup) return; // the choice changed meanwhile
        _ai = found;
        UpdateAiButton();
    }

    void SetAi(string? key, string? path)
    {
        _settings.Ai = key;
        _settings.AiPath = path;
        _settings.Save();
        ResolveAi(force: true);
    }

    static bool TryOpen(string? target)
    {
        if (target == null) return false;
        try { Shell.Open(target); return true; }
        catch (Exception ex) { Diag.Error(ex); return false; }
    }

    Brush ArtBrush(ImageSource? art)
    {
        if (art == null) return (Brush)FindResource("ArtFallback");
        var brush = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        return brush;
    }

    // ───────────────────────── expanded view ─────────────────────────

    void RefreshMediaUi()
    {
        var info = _info;
        MediaPanel.Visibility = info != null ? Visibility.Visible : Visibility.Collapsed;
        ClockPanel.Visibility = info != null ? Visibility.Collapsed : Visibility.Visible;
        if (info == null) { UpdateClock(); return; }

        TitleText.Text = info.Title;
        ArtistText.Text = info.Artist;
        ArtistText.Visibility = info.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppText.Text = info.Album.Length > 0 && info.App.Length > 0 ? $"{info.Album} · {info.App}" : info.Album.Length > 0 ? info.Album : info.App;
        BigArt.Background = ArtBrush(info.Art);
        RefreshPlayModes();
        ArtPlaceholder.Visibility = info.Art == null ? Visibility.Visible : Visibility.Collapsed;
        SetPlayIcon(info.IsPlaying);
        PlayBtn.IsEnabled = info.CanPlayPause;
        PrevBtn.IsEnabled = info.CanPrevious;
        NextBtn.IsEnabled = info.CanNext;
        ProgressRow.Visibility = info.Duration > TimeSpan.FromSeconds(1) ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsInteractive = info.CanSeek;
        UpdateLive();
    }

    /// <summary>Shuffle and repeat, lit in the cover's colour when on; hidden when the player doesn't offer them.</summary>
    void RefreshPlayModes()
    {
        var info = _info;
        var on = Progress.Fill;
        var off = (Brush)FindResource("Faint");
        ShuffleBtn.Visibility = info is { CanShuffle: true, Shuffle: not null } ? Visibility.Visible : Visibility.Hidden;
        RepeatBtn.Visibility = info is { CanRepeat: true, Repeat: not null } ? Visibility.Visible : Visibility.Hidden;
        ShuffleIcon.Foreground = info?.Shuffle == true ? on : off;
        RepeatIcon.Foreground = info?.Repeat is 1 or 2 ? on : off;
        RepeatIcon.Text = info?.Repeat == 1 ? "" : ""; // repeat one / repeat all
        RepeatBtn.ToolTip = info?.Repeat switch { 1 => "Repeat: this song", 2 => "Repeat: all", _ => "Repeat: off" };
        ShuffleBtn.ToolTip = info?.Shuffle == true ? "Shuffle: on" : "Shuffle: off";
    }

    void SetPlayIcon(bool playing)
    {
        PlayIcon.Data = (Geometry)FindResource(playing ? "PauseGeo" : "PlayGeo");
        PlayIcon.Margin = new Thickness(playing ? 0 : 3, 0, 0, 0); // a triangle looks off-centre when centred
    }

    /// <summary>Runs every 500 ms, but only while the notch is open.</summary>
    void UpdateLive()
    {
        if (_info is { } info && ProgressRow.Visibility == Visibility.Visible && !Progress.IsDragging)
        {
            var pos = info.CurrentPosition;
            Progress.Value = info.Duration.Ticks > 0 ? (double)pos.Ticks / info.Duration.Ticks : 0;
            PosText.Text = FormatTime(pos);
            RemainText.Text = "-" + FormatTime(info.Duration - pos);
        }
        if (ClockPanel.Visibility == Visibility.Visible) UpdateClock();
        ReadVolume();
    }

    static string FormatTime(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    void UpdateClock()
    {
        var now = DateTime.Now;
        ClockBig.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"));
    }

    void ReadVolume()
    {
        if (VolSlider.IsDragging) return;
        if (_volume.TryGet(out float level, out bool muted))
        {
            VolSlider.Value = muted ? 0 : level;
            VolSlider.Opacity = 1;
            UpdateVolumeIcon(level, muted);
        }
        else VolSlider.Opacity = 0.35;
    }

    void UpdateVolumeIcon(double level, bool muted)
        => VolIcon.Text = muted || level < 0.005 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";

    void RefreshDevices()
    {
        DevicesPanel.Children.Clear();
        if (_power.HasBattery)
            DevicesPanel.Children.Add(DeviceChip("", null, _power.Percent, _power.OnAc));
        foreach (var d in _bt.Where(d => d.IsConnected))
            DevicesPanel.Children.Add(DeviceChip(d.Glyph, d.Name, d.Battery, false));
        if (DevicesPanel.Children.Count == 0)
            DevicesPanel.Children.Add(new TextBlock { Text = "No battery devices", FontSize = 12, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center });
    }

    static FrameworkElement DeviceChip(string glyph, string? name, int level, bool charging)
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        chip.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        if (name != null)
            chip.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = DimBrush, MaxWidth = 100, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        chip.Children.Add(new BatteryGlyph { Level = level, Charging = charging, VerticalAlignment = VerticalAlignment.Center });
        chip.Children.Add(new TextBlock { Text = $"{level}%", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return chip;
    }

    // ───────────────────────── input ─────────────────────────

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseDelay.Stop();
        if (!_expanded) _expandDelay.Start();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _expandDelay.Stop();
        if (_expanded) _collapseDelay.Start();
    }

    void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_expanded || TaskShowing || _sharing) return;
        _expandDelay.Stop();
        SetExpanded(true);
    }

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_info != null) SetPlayIcon(!_info.IsPlaying); // feels instant; the real state follows a moment later
        _media.PlayPause();
    }

    void Prev_Click(object sender, RoutedEventArgs e) => _media.Previous();
    void Next_Click(object sender, RoutedEventArgs e) => _media.Next();

    void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_info is not { Shuffle: { } shuffle } info) return;
        info.Shuffle = !shuffle; // feels instant; the player confirms a moment later
        RefreshPlayModes();
        _media.SetShuffle(!shuffle);
    }

    /// <summary>Off → all → this song → off, like the phone players.</summary>
    void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (_info is not { Repeat: { } repeat } info) return;
        int next = repeat switch { 0 => 2, 2 => 1, _ => 0 };
        info.Repeat = next;
        RefreshPlayModes();
        _media.SetRepeat(next);
    }

    void Player_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_info == null) return;
        SetExpanded(false);
        try
        {
            if (!Shell.Activate(_info.AppId)) ShowPeek("", "Can't open " + (_info.App.Length > 0 ? _info.App : "the player"), "", null, false, null);
        }
        catch (Exception ex) { Diag.Error(ex); }
    }

    /// <summary>Someone changed the volume (keys, the taskbar, another app): show it like the phone does.</summary>
    void OnVolumeChanged(float level, bool muted)
    {
        if (_expanded) { ReadVolume(); return; }
        if (_hiddenForFullscreen || TaskShowing || _sharing) return;

        VolPeekBar.Value = muted ? 0 : level;
        VolPeekBar.Fill = _info?.IsPlaying == true ? Progress.Fill : Brushes.White;
        VolPeekIcon.Text = muted || level < 0.005 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";
        VolPeekText.Text = muted ? "Muted" : $"{Math.Round(level * 100)}%";
        _volumePeek = true;
        _volumeTimer.Stop();
        _volumeTimer.Start();
        ApplyState();
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        _volume.ToggleMute();
        ReadVolume();
    }

    async void Ai_Click(object sender, RoutedEventArgs e)
    {
        var target = _ai;
        // Only right after picking another AI is the lookup still running; it takes a moment at most.
        if (target == null && _aiLookup != null)
        {
            try { target = await _aiLookup; } catch { }
        }
        target ??= AiLauncher.Quick(_settings.Ai, _settings.AiPath);
        if (target == null) return;

        // Close up, so the notch isn't in front of the app that opens.
        SetExpanded(false);
        if (!TryOpen(target.Launch) && !TryOpen(target.Web))
            ShowPeek("\uE783", "Can't open " + target.Name, "", null, false, null);
    }

    void MessageRow_Click(object sender, RoutedEventArgs e)
    {
        _teams.MarkRead();
        SetExpanded(false);
        if (!TryOpen(_teams.AppId != null ? Shell.AppTarget(_teams.AppId) : "msteams:"))
            ShowPeek("\uE783", "Can't open Teams", "", null, false, null);
    }

    /// <summary>
    /// Fills the menu before it opens. Filling it in Opened made it visibly jump: the AI entry turned
    /// into a submenu and the screen entries were added while the menu was already on screen.
    /// </summary>
    void Island_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        MenuAnim.IsChecked = _settings.Animations;
        MenuMini.IsChecked = _settings.MiniIdle;
        try { MenuAutostart.IsChecked = Autostart.IsEnabled; } catch { MenuAutostart.IsChecked = false; }
        FillAiMenu();
        bool denied = _settings.TeamsMessages && _teams.Access == TeamsService.AccessState.Denied;
        bool unavailable = _teams.Access == TeamsService.AccessState.Unavailable;
        MenuTeams.IsChecked = _settings.TeamsMessages && !denied && !unavailable;
        MenuTeams.Header = denied ? "Teams messages (allow access…)" : unavailable ? "Teams messages (not supported here)" : "Teams messages";
        MenuTeams.IsEnabled = !unavailable;
        MenuAirDrop.IsChecked = _settings.AirDrop;
        MenuReceive.IsEnabled = _settings.AirDrop && !TaskShowing;
        ListScreens(Island.ContextMenu);
    }

    /// <summary>AI button › Claude, Gemini, ChatGPT, the app you picked, Choose app…, Off.</summary>
    void FillAiMenu()
    {
        MenuAi.Items.Clear();
        foreach (var (key, name) in AiLauncher.Choices)
            MenuAi.Items.Add(AiChoice(name, key, _settings.AiPath));
        if (!string.IsNullOrEmpty(_settings.AiPath))
            MenuAi.Items.Add(AiChoice(AiLauncher.NameOf(_settings.AiPath).Replace("_", "__"), AiLauncher.Custom, _settings.AiPath));
        MenuAi.Items.Add(new Separator());
        var pick = new MenuItem { Header = "Choose app…" };
        // After the menu has closed: a dialog from inside the click would open under the closing menu.
        pick.Click += (_, _) => Dispatcher.InvokeAsync(PickAiApp, DispatcherPriority.Background);
        MenuAi.Items.Add(pick);
        MenuAi.Items.Add(AiChoice("Off", null, _settings.AiPath));
    }

    MenuItem AiChoice(string header, string? key, string? path)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = _settings.Ai == key };
        item.Click += (_, _) => SetAi(key, path);
        return item;
    }

    void PickAiApp()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the app the AI button opens",
            Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.appref-ms|All files|*.*",
            DereferenceLinks = false, // keep a shortcut's arguments and start folder
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };
        try
        {
            if (dialog.ShowDialog(this) == true) SetAi(AiLauncher.Custom, dialog.FileName);
        }
        catch (Exception ex) { Diag.Error(ex); }
    }

    /// <summary>With more than one display, the menu lists them so the notch can be moved.</summary>
    void ListScreens(ContextMenu menu)
    {
        foreach (var old in menu.Items.OfType<MenuItem>().Where(i => i.Tag is Native.Screen).ToList())
            menu.Items.Remove(old);

        var screens = Native.Screens();
        MonitorSeparator.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (screens.Count < 2) return;

        string[] places = screens.Count == 2 ? new[] { "Left", "Right" }
                        : screens.Count == 3 ? new[] { "Left", "Middle", "Right" }
                        : Array.Empty<string>();
        int at = menu.Items.IndexOf(MonitorSeparator) + 1;
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            string name = places.Length > 0 ? $"{places[i]} screen" : $"Screen {i + 1}";
            var item = new MenuItem
            {
                Header = screen.Primary ? name + " (main display)" : name,
                IsCheckable = true,
                IsChecked = screen.Handle == _monitor,
                Tag = screen,
            };
            item.Click += (_, _) =>
            {
                _settings.Monitor = screen.Device;
                _settings.Save();
                PositionWindow();
            };
            menu.Items.Insert(at + i, item);
        }
    }

    void Menu_Closed(object sender, RoutedEventArgs e)
    {
        if (_expanded && !Island.IsMouseOver) _collapseDelay.Start();
    }

    void MenuAnim_Click(object sender, RoutedEventArgs e)
    {
        _settings.Animations = MenuAnim.IsChecked;
        _settings.Save();
        UpdateEqualizer();
    }

    void MenuMini_Click(object sender, RoutedEventArgs e)
    {
        _settings.MiniIdle = MenuMini.IsChecked;
        _settings.Save();
        _state = NotchState.None;
        ApplyState();
    }

    async void MenuTeams_Click(object sender, RoutedEventArgs e)
    {
        _settings.TeamsMessages = MenuTeams.IsChecked;
        _settings.Save();
        if (!_settings.TeamsMessages) { _teams.Stop(); return; }
        await _teams.StartAsync(ask: true);
        // Once refused, Windows doesn't ask again; the switch is in Settings.
        if (_teams.Access == TeamsService.AccessState.Denied) TryOpen("ms-settings:privacy-notifications");
    }

    void MenuAutostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(MenuAutostart.IsChecked); }
        catch (Exception ex) { Diag.Error(ex); }
    }

    void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

#if DEBUG
    // ───────────────────────── dev snapshots ─────────────────────────

    /// <summary>
    /// Renders the notch states to PNGs (used for the README). Poses with demo data only,
    /// so screenshots never show what is really playing or paired on this machine.
    /// </summary>
    internal async Task SnapshotAsync(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        _settings.Animations = false; // still frames, so the bars are captured at rest
        _settings.MiniIdle = false;
        await Task.Delay(2500); // let the real sources report in once, then take over
        _media.Changed -= OnMediaChanged;
        _teams.Changed -= OnMessagesChanged;
        _bt = new List<BtDevice> { new() { Address = "DEMO", Name = "AirPods Pro", Battery = 80, Connected = true, ClassOfDevice = 0x240404 } };

        OnMediaChanged(null, false);
        await Snap(dir, "idle", 60);

        OnMediaChanged(DemoMedia(), true);
        await Snap(dir, "compact", 60);

        ShowPeek("", "AirPods Pro", "80%", 80, false, null);
        await Snap(dir, "peek", 60);
        _peekTimer.Stop();
        _peekActive = false;

        SetExpanded(true);
        await Snap(dir, "expanded", 240);

        SetExpanded(false);
        OnMediaChanged(null, false);
        SetExpanded(true);
        await Snap(dir, "expanded-idle", 200);

        SetExpanded(false);
        _messages = new List<TeamsMessage>
        {
            new() { Title = "Lena Hoffmann", Text = "Morning! Did you see the new mockups?", Time = DateTimeOffset.Now.AddMinutes(-3) },
            new() { Title = "Lena Hoffmann", Text = "Can we go through them before the 3 pm call?", Time = DateTimeOffset.Now },
        };
        RefreshMessages();
        ShowMessage(_messages[^1]);
        ApplyState();
        await Snap(dir, "message", 70);
        _peekTimer.Stop();
        _peekActive = false;
        ApplyState();
        await Snap(dir, "badge", 60);

        SetExpanded(true);
        await Snap(dir, "expanded-messages", 260);

        // 1.2: volume, AirDrop, shuffle/repeat
        SetExpanded(false);
        _messages = Array.Empty<TeamsMessage>();
        RefreshMessages();
        var demo = DemoMedia();
        demo.Album = "After Hours";
        demo.CanShuffle = demo.CanRepeat = true;
        demo.Shuffle = true;
        demo.Repeat = 2;
        OnMediaChanged(demo, true);
        await Task.Delay(300);
        OnVolumeChanged(0.62f, false);
        await Snap(dir, "volume", 60);
        _volumePeek = false;
        _volumeTimer.Stop();

        SetExpanded(true);
        await Snap(dir, "expanded-playmodes", 240);
        SetExpanded(false);

        _demoPeers = new List<NearbyPeer>
        {
            new(Guid.NewGuid(), "Lena Hoffmann", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 1), DateTime.UtcNow),
            new(Guid.NewGuid(), "Tim", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 2), DateTime.UtcNow),
        };
        _dragFiles = new[] { @"C:\Demo\Holiday photos.zip" };
        _sharing = true;
        RefreshShareTargets();
        HighlightChip(ShareTargets.Children[0]);
        ApplyState();
        await Snap(dir, "airdrop-share", 140);
        _sharing = false;
        _demoPeers = null;

        ShowTask(Initials("Lena Hoffmann"), AvatarColor("Lena Hoffmann"), "Lena Hoffmann wants to send you", "Holiday photos.zip · 48.2 MB", null,
                 ("Accept", () => { }), ("Decline", () => { }), textIcon: true);
        await Snap(dir, "airdrop-ask", 70);

        _busy = true;
        ShowProgress("Sending to Lena Hoffmann", "Holiday photos.zip · 48.2 MB", 0.42, sending: true);
        await Snap(dir, "airdrop-progress", 70);

        ShowTask("\uE71B", ShareBlue, "Code copied – paste it to your friend", ShareCode.Format(ShareCode.NewSecret()), null, null, ("Cancel", () => { }));
        await Snap(dir, "airdrop-code", 70);

        _busy = false;
        EndTask("\uE73E", GreenBrush, "Saved Holiday photos.zip", "From Lena Hoffmann · in Downloads", ("Show", () => { }));
        await Snap(dir, "airdrop-saved", 70);
        HideTask();
    }

    static MediaInfo DemoMedia()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFF, 0x5F, 0x6D), Color.FromRgb(0x5B, 0x24, 0x7A), 45), null, new Rect(0, 0, 160, 160));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)), null, new Point(115, 45), 30, 30);
        }
        var art = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
        art.Render(visual);
        art.Freeze();
        // Same treatment the real covers get.
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(art));
        using var bytes = new System.IO.MemoryStream();
        png.Save(bytes);
        var (backdrop, shade) = MediaService.BackdropFor(bytes.ToArray());
        return new MediaInfo
        {
            Title = "Blinding Lights", Artist = "The Weeknd", App = "Spotify", IsPlaying = true,
            Duration = TimeSpan.FromSeconds(200), Position = TimeSpan.FromSeconds(42), PositionStamp = DateTimeOffset.Now,
            CanNext = true, CanPrevious = true, CanSeek = true, CanPlayPause = true,
            Art = art, Accent = Color.FromRgb(0xFF, 0x7A, 0x8A), Backdrop = backdrop, BackdropShade = shade,
        };
    }

    async Task Snap(string dir, string name, double height)
    {
        await Task.Delay(900);
        UpdateLayout();
        const double scale = 2, width = 480;
        var image = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var desktop = new DrawingVisual();
        using (var dc = desktop.RenderOpen())
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5B, 0x7F, 0xC9), Color.FromRgb(0x1D, 0x2B, 0x4F), 90), null, new Rect(0, 0, width, height));
        image.Render(desktop);
        image.Render(Root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"));
        png.Save(file);
    }
#endif
}
