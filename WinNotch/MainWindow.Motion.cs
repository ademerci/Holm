using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinNotch;

/// <summary>
/// A damped spring as an easing curve, worked out so it has come to rest when the animation ends. With
/// <see cref="Damping"/> 1 it glides in without overshooting; below 1 it swings a little past the end and settles back,
/// like the iPhone's island.
/// </summary>
internal sealed class Spring : IEasingFunction
{
    /// <summary>The notch opening, and views popping in: a touch of bounce (1.5 % past the end).</summary>
    public static readonly Spring Bouncy = new(0.8);
    /// <summary>The notch closing: no overshoot, so a shrinking height never goes below zero.</summary>
    public static readonly Spring Smooth = new(1);

    readonly double _damping, _omega, _damped;

    public Spring(double damping)
    {
        _damping = Math.Clamp(damping, 0.3, 1);
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

// How the notch moves: the shape springs open and glides shut, the open notch's parts unfold from under the closed
// one, and the small views pop in. Only transforms and opacity move, never the layout of what's inside, so a frame
// stays cheap; views that are out of sight are taken out of the layout so the moving shape doesn't measure them.
public partial class MainWindow
{
    const int OpenMs = 520, CloseMs = 400;

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

    FrameworkElement[] AllViews => new FrameworkElement[]
    {
        IdleView, CompactView, BadgeView, TimerView, FocumonView, CallView, PeekView, MessageView,
        VolumeView, DownloadView, TaskView, PhoneView, ShareView, ExpandedView,
    };

    void InitMotion()
    {
        foreach (var view in AllViews)
        {
            view.RenderTransform = new ScaleTransform();
            // The open notch grows out of the closed one at the top; the small views pop from their middle.
            view.RenderTransformOrigin = view == ExpandedView ? new Point(0.5, 0) : new Point(0.5, 0.5);
            if (!view.IsHitTestVisible) view.Visibility = Resting(view);
        }
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
            var begin = TimeSpan.FromMilliseconds(open ? 40 : 110);
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(open ? 240 : 200))
            {
                BeginTime = begin,
                EasingFunction = FadeInEase,
            });
            if (scale != null)
            {
                var pop = new DoubleAnimation(open ? 0.93 : 0.86, 1, TimeSpan.FromMilliseconds(open ? OpenMs : 440))
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

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(open ? 120 : 110)) { EasingFunction = FadeOut };
        fade.Completed += (_, _) =>
        {
            if (view.IsHitTestVisible) return; // shown again meanwhile
            ResetScale(scale);
            view.Visibility = Resting(view);
        };
        view.BeginAnimation(OpacityProperty, fade);
        if (scale != null)
        {
            var shrink = new DoubleAnimation(open ? 0.95 : 0.9, TimeSpan.FromMilliseconds(open ? 200 : 160)) { EasingFunction = FadeOut };
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
    /// ones more, and they all spring into place with the shape.
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
            var slide = new DoubleAnimation(-Math.Min(6 + 5 * i, 30), 0, TimeSpan.FromMilliseconds(OpenMs))
            {
                EasingFunction = Spring.Bouncy,
                FillBehavior = FillBehavior.Stop,
            };
            move.BeginAnimation(TranslateTransform.YProperty, slide);
            i++;
        }
    }

    /// <summary>The rows of the open notch from top to bottom, as far as they show.</summary>
    IEnumerable<UIElement> UnfoldingParts()
    {
        int rows = 0;
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
