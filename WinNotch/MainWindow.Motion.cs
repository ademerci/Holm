using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinNotch;

/// <summary>
/// A damped spring as an easing curve, worked out so it has come to rest when the animation ends. With
/// <see cref="Damping"/> 1 it glides in without overshooting; below 1 it swings past the end and settles back
/// with a gentle bounce, like the iPhone's island.
/// </summary>
internal sealed class Spring : IEasingFunction
{
    /// <summary>The highest <see cref="Bounce"/>: one and a half times Holm's normal swing.</summary>
    public const double MaxBounce = 1.5;

    // Declared before the curves below, so it is set when they are made.
    static double _bounce = 1;

    /// <summary>The notch opening, views popping in and rows unfolding: a natural bounce (~7 % past the end).</summary>
    public static Spring Bouncy { get; private set; } = new(0.64);
    /// <summary>Quick playful pop (~11 % past the end) for reactions and micro-interactions.</summary>
    public static Spring Snappy { get; private set; } = new(0.58);
    /// <summary>The notch closing onto the edge: no overshoot, so a shrinking height never goes below zero.</summary>
    public static readonly Spring Smooth = new(1);

    /// <summary>
    /// How springy all motion is (Settings › Bouncy motion, right-click › Customize › Bounce): 0 glides in without
    /// overshooting, 1 is Holm's normal bounce, up to <see cref="MaxBounce"/> swings further.
    /// </summary>
    public static double Bounce
    {
        get => _bounce;
        set
        {
            _bounce = Math.Clamp(value, 0, MaxBounce);
            Bouncy = new(0.64);
            Snappy = new(0.58);
        }
    }

    /// <summary>A damping as tuned for the normal bounce, scaled to <see cref="Bounce"/>; 1 and above stay as they are.</summary>
    public static double Soften(double damping) => damping >= 1 ? damping : 1 - (1 - damping) * _bounce;

    readonly double _damping, _omega, _damped;

    public Spring(double damping)
    {
        _damping = Math.Clamp(Soften(damping), 0.3, 1);
        // Stiff enough that what's left of the swing at the end is below 0.1 %.
        _omega = _damping >= 1 ? 9.23 : Math.Log(1000 / Math.Sqrt(1 - _damping * _damping)) / _damping;
        _damped = _damping >= 1 ? 0 : _omega * Math.Sqrt(1 - _damping * _damping);
    }

    public double Damping => _damping;

    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        if (_damped == 0) return 1 - Math.Exp(-_omega * t) * (1 + _omega * t);
        return 1 - Math.Exp(-_damping * _omega * t) * (Math.Cos(_damped * t) + _damping * _omega / _damped * Math.Sin(_damped * t));
    }
}

/// <summary>
/// Exact analytical damped harmonic oscillator for a single animated scalar. Stepping preserves momentum across
/// mid-flight target changes and is independent of frame rate.
/// </summary>
internal struct SpringValue
{
    readonly double _positionTolerance, _velocityTolerance;

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public double Response { get; private set; }
    public double Damping { get; private set; }
    public bool IsSettled { get; private set; }

    public SpringValue(double initial, double positionTolerance = 0.25, double velocityTolerance = 3.0)
    {
        Value = Target = initial;
        Velocity = 0;
        Response = 0.36;
        Damping = 0.64;
        _positionTolerance = positionTolerance;
        _velocityTolerance = velocityTolerance;
        IsSettled = true;
    }

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
        IsSettled = true;
    }

    public void Retarget(double target, double response, double damping)
    {
        Target = target;
        Response = Math.Max(response, 0.05);
        Damping = Math.Clamp(Spring.Soften(damping), 0.25, 1.5);
        IsSettled = Math.Abs(Value - Target) <= _positionTolerance && Math.Abs(Velocity) <= _velocityTolerance;
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }

    public void Kick(double deltaVelocity, double? response = null, double? damping = null)
    {
        if (response.HasValue) Response = Math.Max(response.Value, 0.05);
        if (damping.HasValue) Damping = Math.Clamp(Spring.Soften(damping.Value), 0.25, 1.5);
        Velocity += deltaVelocity;
        IsSettled = false;
    }

    /// <summary>Advances the spring by <paramref name="dt"/> seconds; returns <see langword="true"/> once settled.</summary>
    public bool Step(double dt)
    {
        if (IsSettled || dt <= 0) return IsSettled;

        double omega = 2 * Math.PI / Response;
        double y0 = Value - Target;
        double v0 = Velocity;
        double y, v;

        if (Damping < 0.9999)
        {
            double root = Math.Sqrt(1 - Damping * Damping);
            double damped = omega * root;
            double decay = Math.Exp(-Damping * omega * dt);
            double c2 = (v0 + Damping * omega * y0) / damped;
            double cos = Math.Cos(damped * dt);
            double sin = Math.Sin(damped * dt);
            y = decay * (y0 * cos + c2 * sin);
            v = decay * ((c2 * damped - y0 * Damping * omega) * cos - (y0 * damped + c2 * Damping * omega) * sin);
        }
        else if (Damping <= 1.0001)
        {
            double decay = Math.Exp(-omega * dt);
            double c2 = v0 + omega * y0;
            y = decay * (y0 + c2 * dt);
            v = decay * (c2 - omega * (y0 + c2 * dt));
        }
        else
        {
            double root = Math.Sqrt(Damping * Damping - 1);
            double r1 = -omega * (Damping - root);
            double r2 = -omega * (Damping + root);
            double a = (v0 - r2 * y0) / (r1 - r2);
            double b = y0 - a;
            double e1 = Math.Exp(r1 * dt);
            double e2 = Math.Exp(r2 * dt);
            y = a * e1 + b * e2;
            v = a * r1 * e1 + b * r2 * e2;
        }

        if (Math.Abs(y) <= _positionTolerance && Math.Abs(v) <= _velocityTolerance)
        {
            Value = Target;
            Velocity = 0;
            IsSettled = true;
            return true;
        }

        Value = Target + y;
        Velocity = v;
        return false;
    }
}

// How the notch moves: real damped springs drive the shape on CompositionTarget.Rendering so momentum carries
// naturally across interruptions (with one UpdateShape per frame, and zero idle CPU once settled). The open
// notch's parts cascade out from under the closed one, and the small views pop in with bounce.
public partial class MainWindow
{
    const int OpenMs = 480, CloseMs = 360;

    // Frozen: shared by every window, whichever thread it runs on.
    static readonly IEasingFunction FadeOut = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    static readonly IEasingFunction FadeInEase = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut });

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

#if DEBUG || BENCH
    // Benchmark switches: without the unfolding and popping (HOLM_BENCH_PLAIN=1), and with the open notch drawn as one
    // cached picture while the shape moves (HOLM_BENCH_CACHE=1).
    static readonly bool BenchPlain = Environment.GetEnvironmentVariable("HOLM_BENCH_PLAIN") == "1";
    static readonly bool BenchCache = Environment.GetEnvironmentVariable("HOLM_BENCH_CACHE") == "1";
#else
    static readonly bool BenchPlain = false, BenchCache = false;
#endif

    /// <summary>Work that opening needs but that doesn't change the open notch's size: done once it has landed.</summary>
    bool _openSettling;
    bool _motionRunning, _steppingShape, _hoverBoost;
    long _motionLastTick;

    SpringValue _wSpring = new(150);
    SpringValue _hSpring = new(30);
    SpringValue _rSpring = new(15, 0.15, 2.0);
    SpringValue _earSpring = new(MaxEar, 0.1, 1.5);
    SpringValue _scaleSpring = new(1, 0.0008, 0.015);

    FrameworkElement[] AllViews => new FrameworkElement[]
    {
        IdleView, CompactView, BadgeView, TimerView, FocumonView, CallView, DictationView, PeekView, MessageView,
        VolumeView, DownloadView, TaskView, PhoneView, ShareView, ExpandedView,
    };

    static readonly (string Name, double Level)[] BounceLevels = { ("Subtle", 0.5), ("Normal", 1), ("Lively", Spring.MaxBounce) };

    /// <summary>Sets every spring to the bounce the settings ask for: none while Bouncy motion is off.</summary>
    void ApplyBounce() => Spring.Bounce = _settings.Bounce ? _settings.BounceLevel : 0;

    /// <summary>Customize › Bounce: how much the springs swing past the end while Bouncy motion is on.</summary>
    void FillBounceMenu()
    {
        MenuBounceLevel.Items.Clear();
        MenuBounceLevel.IsEnabled = _settings.Bounce;
        foreach (var (name, level) in BounceLevels)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Math.Abs(_settings.BounceLevel - level) < 0.01 };
            item.Click += (_, _) =>
            {
                _settings.BounceLevel = level;
                foreach (MenuItem choice in MenuBounceLevel.Items) choice.IsChecked = choice == item;
                _settings.Save();
                ApplyBounce();
            };
            MenuBounceLevel.Items.Add(item);
        }
    }

    void MenuBounce_Click(object sender, RoutedEventArgs e)
    {
        _settings.Bounce = MenuBounce.IsChecked;
        _settings.Save();
        ApplyBounce();
    }

    void InitMotion()
    {
        ApplyBounce();
        foreach (var view in AllViews)
        {
            view.RenderTransform = new ScaleTransform();
            // The open notch grows out of the closed one at the top; the small views pop from their middle.
            view.RenderTransformOrigin = view == ExpandedView ? new Point(0.5, 0) : new Point(0.5, 0.5);
            if (!view.IsHitTestVisible) view.Visibility = Resting(view);
        }
    }

    void StopMotion()
    {
        if (!_motionRunning) return;
        CompositionTarget.Rendering -= OnMotionFrame;
        _motionRunning = false;
    }

    void StartMotion()
    {
        if (_motionRunning) return;
        _motionLastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnMotionFrame;
        _motionRunning = true;
    }

    void SnapShape(double w, double h, double r, double ear, double scale)
    {
        _hoverBoost = false;
        StopMotion();
        _wSpring.Snap(w);
        _hSpring.Snap(h);
        _rSpring.Snap(r);
        _earSpring.Snap(ear);
        _scaleSpring.Snap(scale);
        ApplyShapeValues(w, h, r, ear, scale);
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

        if (_wSpring.IsSettled && _hSpring.IsSettled && _rSpring.IsSettled && _earSpring.IsSettled && _scaleSpring.IsSettled)
        {
            StopMotion();
            ApplyShapeValues(w, h, r, ear, scale);
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
        if (!_settings.Animations || _expanded || _shapeW <= 0) return;

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
        _shapeAnimating = true;
        StartMotion();
    }

    /// <summary>Adds a small physical bounce when a live pill updates in place (volume step, back-to-back peek).</summary>
    void PulseNotch(double widthVelocity = 28, double heightVelocity = 16, double scaleVelocity = 0.08)
    {
        if (!_settings.Animations || _expanded || _shapeW <= 0) return;
        double needed = NeededWindowHeight(_shapeH + 6, _shapeScale * 1.05);
        if (needed > Height) Height = needed;
        _wSpring.Kick(widthVelocity, 0.30, 0.58);
        _hSpring.Kick(heightVelocity, 0.32, 0.58);
        _scaleSpring.Kick(scaleVelocity, 0.30, 0.58);
        _shapeAnimating = true;
        StartMotion();
    }

    static double NeededWindowHeight(double height, double scale)
        => Math.Ceiling(Math.Max(height, 1) * Math.Max(scale, 0.1) * 1.15) + 2;

    void OnMotionFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((double)(now - _motionLastTick) / Stopwatch.Frequency, 0.001, 0.05);
        _motionLastTick = now;

        bool settled = _wSpring.Step(dt)
                     & _hSpring.Step(dt)
                     & _rSpring.Step(dt)
                     & _earSpring.Step(dt)
                     & _scaleSpring.Step(dt);

        double hMin = _mini && !_hoverBoost ? 1 : 12;
        double w = Math.Max(1, _wSpring.Value);
        double h = Math.Max(_shapeH < hMin ? 1 : hMin, _hSpring.Value);
        double r = Math.Max(0, _rSpring.Value);
        double ear = Math.Max(0, _earSpring.Value);
        double scale = Math.Max(0.1, _scaleSpring.Value);

        double frameNeed = NeededWindowHeight(h, scale);
        if (frameNeed > Height) Height = frameNeed;

        ApplyShapeValues(w, h, r, ear, scale);

        if (settled)
        {
            StopMotion();
            OnShapeSettled();
        }
    }

    void ApplyShapeValues(double w, double h, double r, double ear, double scale)
    {
        _steppingShape = true;
        try
        {
            Island.Width = w;
            Island.Height = h;
            Radius = r;
            Ear = ear;
            _notchScale.ScaleX = scale;
            _notchScale.ScaleY = scale;
        }
        finally
        {
            _steppingShape = false;
        }
        UpdateShape(w, h);
    }

    /// <summary>
    /// How a view rests out of sight. The open notch and the share view stay laid out (Hidden): their height is
    /// measured before they show, and opening must not wait for a first layout of everything in it. The small views
    /// leave the layout (Collapsed), so the moving shape doesn't measure them on every frame.
    /// </summary>
    Visibility Resting(UIElement view) => view == ExpandedView || view == ShareView ? Visibility.Hidden : Visibility.Collapsed;

    void SetView(UIElement view, bool show, bool animate)
    {
        bool shown = view.IsHitTestVisible;
        view.IsHitTestVisible = show;
        var scale = BenchPlain ? null : view.RenderTransform as ScaleTransform;
        if (BenchCache && view == ExpandedView && animate) view.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        if (!animate)
        {
            view.BeginAnimation(OpacityProperty, null);
            view.Opacity = show ? 1 : 0;
            ResetScale(scale);
            view.Visibility = show ? Visibility.Visible : Resting(view);
            return;
        }
        if (show == shown)
        {
            if (show) view.Visibility = Visibility.Visible;
            return; // already showing, or already gone (or on its way out)
        }

        bool open = view == ExpandedView;
        if (show)
        {
            view.Visibility = Visibility.Visible;
            var begin = TimeSpan.FromMilliseconds(open ? 35 : 95);
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(open ? 220 : 180))
            {
                BeginTime = begin,
                EasingFunction = FadeInEase,
            });
            if (scale != null)
            {
                var pop = new DoubleAnimation(open ? 0.91 : 0.84, 1, TimeSpan.FromMilliseconds(open ? OpenMs : 420))
                {
                    BeginTime = open ? TimeSpan.Zero : begin,
                    EasingFunction = Spring.Bouncy,
                    FillBehavior = FillBehavior.Stop, // ends on the resting 1, and its clock goes away
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(open ? 110 : 100)) { EasingFunction = FadeOut };
        fade.Completed += (_, _) =>
        {
            if (view.IsHitTestVisible) return; // shown again meanwhile
            ResetScale(scale);
            view.Visibility = Resting(view);
        };
        view.BeginAnimation(OpacityProperty, fade);
        if (scale != null)
        {
            var shrink = new DoubleAnimation(open ? 0.95 : 0.9, TimeSpan.FromMilliseconds(open ? 190 : 150)) { EasingFunction = FadeOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }
    }

    static void ResetScale(ScaleTransform? scale)
    {
        if (scale == null) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
    }

    /// <summary>
    /// The open notch's rows unfold from under the closed notch: each starts a little higher than it ends, the lower
    /// ones more, and they cascade with a slight spring bounce into place.
    /// </summary>
    void Unfold()
    {
        if (BenchPlain) return;
        int i = 0;
        foreach (var part in UnfoldingParts())
        {
            if (part.RenderTransform is not TranslateTransform move)
            {
                if (part.RenderTransform != null && !part.RenderTransform.Value.IsIdentity) continue; // moved by something else
                part.RenderTransform = move = new TranslateTransform();
            }
            double start = -Math.Min(8 + 5 * i, 34);
            move.Y = start;
            var slide = new DoubleAnimation(start, 0, TimeSpan.FromMilliseconds(OpenMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Min(i, 6) * 14),
                EasingFunction = Spring.Bouncy,
                FillBehavior = FillBehavior.Stop,
            };
            slide.Completed += (_, _) => move.Y = 0;
            move.BeginAnimation(TranslateTransform.YProperty, slide);
            i++;
        }
    }

    /// <summary>Switching between home and a tool page slides the incoming page in with a soft spring.</summary>
    void AnimateToolSwitch(UIElement incoming)
    {
        if (!_settings.Animations || BenchPlain) return;
        if (incoming.RenderTransform is not TranslateTransform move)
        {
            if (incoming.RenderTransform != null && !incoming.RenderTransform.Value.IsIdentity) return;
            incoming.RenderTransform = move = new TranslateTransform();
        }
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = FadeInEase,
            FillBehavior = FillBehavior.Stop,
        });
        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = Spring.Bouncy,
            FillBehavior = FillBehavior.Stop,
        };
        move.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>Gives a clicked control a quick springy recoil back to rest.</summary>
    void PulseElement(FrameworkElement element, double fromScale = 0.90)
    {
        if (!_settings.Animations || BenchPlain) return;
        if (element.RenderTransform is not ScaleTransform scale)
        {
            if (element.RenderTransform != null && !element.RenderTransform.Value.IsIdentity) return;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale = new ScaleTransform(1, 1);
        }
        var pop = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = Spring.Snappy,
            FillBehavior = FillBehavior.Stop,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>The rows of the open notch from top to bottom, as far as they show.</summary>
    IEnumerable<UIElement> UnfoldingParts()
    {
        int rows = 0;
        if (ClockFullView.Visibility == Visibility.Visible)
        {
            foreach (UIElement part in ClockFullView.Children)
                if (part.Visibility == Visibility.Visible && rows++ < 8) yield return part;
            yield break;
        }
        foreach (UIElement part in ExpandedPanel.Children)
        {
            if (part.Visibility != Visibility.Visible) continue;
            if (part != HomePanel) { yield return part; continue; }
            foreach (UIElement row in HomePanel.Children)
            {
                if (row.Visibility != Visibility.Visible) continue;
                if (row is Panel { Children.Count: > 0 } group && (row == ClockPanel || row == MessageRows))
                {
                    foreach (UIElement inner in group.Children)
                        if (inner.Visibility == Visibility.Visible && rows++ < 8) yield return inner;
                }
                else if (rows++ < 8) yield return row;
            }
        }
    }

    /// <summary>The open notch has landed: now the work that would have cost frames on the way.</summary>
    void FinishOpening()
    {
        if (BenchCache) ExpandedView.CacheMode = null;
        if (!_openSettling) return;
        _openSettling = false;
        if (!_expanded) return;
        RefreshOutput();
        RefreshBluetooth();
        ResolveAi(force: false);
        // Access may have been granted in Settings since we last asked.
        if (_settings.Messages && _chat.Access is not (MessagesService.AccessState.Allowed or MessagesService.AccessState.Unavailable))
            _ = _chat.StartAsync(ask: false);
    }
}
