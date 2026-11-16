using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// How the views move, as in the WPF app (MainWindow.Motion.cs) with the same timings and curves: the small views
/// pop in from their middle with a bounce, the open notch grows out of the closed one at the top and its rows unfold
/// from under it in a cascade, a tool page slides in, a clicked button recoils. The curves are the WPF app's Spring
/// (linked) and quadratic fades; one frame loop runs them while any is moving, and stops when none is.
///
/// Also the mouse, as in MainWindow.xaml.cs: the notch swells a little under the pointer and opens after 140 ms (or
/// at once on a click), and closes 300 ms after the pointer has left, unless a menu, a field or a slider holds it.
/// </summary>
public sealed partial class NotchWindow
{
    const int OpenMs = 480;

    static double QuadOut(double t) => 1 - (1 - t) * (1 - t);
    static double QuadIn(double t) => t * t;
    static double Bouncy(double t) => Spring.Bouncy.Ease(t);
    static double Snappy(double t) => Spring.Snappy.Ease(t);

    readonly StackPanel _expandedPanel = new();
    readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(140) };
    readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };

    // ───────────── tweens ─────────────

    sealed class Tween
    {
        public required Action<double> Set;
        public double From, To;
        public long Start;
        public double DelayMs, Ms;
        public required Func<double, double> Ease;
        public Action? Done;
        public bool Holding; // a delayed tween waits without touching its value, as a WPF BeginTime does
    }

    readonly Dictionary<(object Target, string Property), Tween> _tweens = new();
    bool _tweening;

    /// <summary>Animates one value; a new animation of the same target and property replaces the running one.</summary>
    void Animate(object target, string property, Action<double> set, double from, double to, double ms, double delayMs,
                 Func<double, double> ease, Action? done = null)
    {
        _tweens[(target, property)] = new Tween
        {
            Set = set, From = from, To = to, Start = Stopwatch.GetTimestamp(), DelayMs = delayMs, Ms = ms, Ease = ease, Done = done,
            Holding = delayMs > 0,
        };
        if (delayMs <= 0) set(from);
        if (_tweening) return;
        _tweening = true;
        RequestAnimationFrame(OnTweenFrame);
    }

    void StopTween(object target, string property) => _tweens.Remove((target, property));

    void OnTweenFrame(TimeSpan _)
    {
        long now = Stopwatch.GetTimestamp();
        var finished = new List<(object, string)>();
        foreach (var (key, tween) in _tweens)
        {
            double elapsed = (now - tween.Start) * 1000.0 / Stopwatch.Frequency - tween.DelayMs;
            if (elapsed < 0) continue;
            if (tween.Holding) { tween.Holding = false; }
            double t = tween.Ms <= 0 ? 1 : Math.Min(1, elapsed / tween.Ms);
            tween.Set(tween.From + (tween.To - tween.From) * tween.Ease(t));
            if (t >= 1) finished.Add(key);
        }
        foreach (var key in finished)
        {
            if (!_tweens.Remove(key, out var done)) continue;
            done.Set(done.To);
            done.Done?.Invoke();
        }
        if (_tweens.Count == 0) { _tweening = false; return; }
        RequestAnimationFrame(OnTweenFrame);
    }

    // ───────────── views ─────────────

    void InitMotion()
    {
        foreach (var view in new Control[] { _idleView, _compactView, _timerView, _volumeView, _downloadView, _peekView, _taskView, _shareView, _expandedView })
        {
            view.RenderTransform = new ScaleTransform(1, 1);
            // The open notch grows out of the closed one at the top; the small views pop from their middle.
            view.RenderTransformOrigin = view == _expandedView ? new RelativePoint(0.5, 0, RelativeUnit.Relative) : RelativePoint.Center;
        }

        _expandDelay.Tick += (_, _) =>
        {
            _expandDelay.Stop();
            if (_island.IsPointerOver && !_taskVisible && !_sharing && !_picking) SetExpanded(true);
        };
        _collapseDelay.Tick += (_, _) =>
        {
            _collapseDelay.Stop();
            // Dragging a slider, a menu open or typing: try again shortly.
            if (PinnedClockOpen) return;
            if (_menuOpen || KeepOpenForTyping || _volSlider.IsDragging || _mixerDragging) { _collapseDelay.Start(); return; }
            if (!_island.IsPointerOver) SetExpanded(false);
        };
        _island.PointerEntered += (_, _) =>
        {
            _collapseDelay.Stop();
            if (_hovered) return;
            _expandDelay.Start();
            if (!_taskVisible && !_sharing && !_picking) SetHoverBoost(true);
        };
        _island.PointerExited += (_, _) =>
        {
            _expandDelay.Stop();
            SetHoverBoost(false);
            if (_hovered && !PinnedClockOpen) _collapseDelay.Start();
        };
        // A click opens at once, without waiting for the hover delay.
        _island.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || _hovered || _taskVisible || _sharing || _picking) return;
            _expandDelay.Stop();
            SetExpanded(true);
        };
    }

    void SetExpanded(bool expand)
    {
        if (_hovered == expand) return;
        _hoverBoost = false;
        _hovered = expand;
        Diag.Log(expand ? "Expanded" : "Collapsed");
        if (!expand)
        {
            SetClockView(false, animate: false);
            ShowTool(Tool.None); // the next time it opens on home
        }
        ApplyState(animate: true);
    }

    /// <summary>Shows or hides one of the notch's views: the WPF app's pop and fade, or at once.</summary>
    void SetView(Control view, bool show, bool animate)
    {
        bool shown = view.IsHitTestVisible;
        view.IsHitTestVisible = show;
        var scale = view.RenderTransform as ScaleTransform;
        if (!animate)
        {
            StopTween(view, "opacity");
            StopTween(view, "scale");
            view.Opacity = show ? 1 : 0;
            if (scale != null) scale.ScaleX = scale.ScaleY = 1;
            return;
        }
        if (show == shown) return; // already showing, or already gone (or on its way out)

        bool open = view == _expandedView;
        void SetScale(double v) { if (scale != null) scale.ScaleX = scale.ScaleY = v; }
        if (show)
        {
            double begin = open ? 35 : 95;
            Animate(view, "opacity", v => view.Opacity = v, view.Opacity, 1, open ? 220 : 180, begin, QuadOut);
            if (scale != null)
                Animate(view, "scale", SetScale, open ? 0.91 : 0.84, 1, open ? OpenMs : 420, open ? 0 : begin, Bouncy);
            return;
        }
        Animate(view, "opacity", v => view.Opacity = v, view.Opacity, 0, open ? 110 : 100, 0, QuadIn,
            () => { if (!view.IsHitTestVisible) SetScale(1); });
        if (scale != null)
            Animate(view, "scale", SetScale, scale.ScaleX, open ? 0.95 : 0.9, open ? 190 : 150, 0, QuadIn);
    }

    /// <summary>
    /// The open notch's rows unfold from under the closed notch: each starts a little higher than it ends, the lower
    /// ones more, and they cascade with a slight spring bounce into place.
    /// </summary>
    void Unfold()
    {
        int i = 0;
        foreach (var part in UnfoldingParts())
        {
            if (part.RenderTransform is not TranslateTransform move)
            {
                if (part.RenderTransform != null) continue; // moved by something else
                part.RenderTransform = move = new TranslateTransform();
            }
            double start = -Math.Min(8 + 5 * i, 34);
            move.Y = start;
            var slide = move;
            Animate(part, "unfold", v => slide.Y = v, start, 0, OpenMs, Math.Min(i, 6) * 14, Bouncy);
            i++;
        }
    }

    /// <summary>The rows of the open notch from top to bottom, as far as they show (at most eight move).</summary>
    IEnumerable<Control> UnfoldingParts()
    {
        int rows = 0;
        if (_clockFullView.IsVisible)
        {
            foreach (var part in _clockFullView.Children)
                if (part.IsVisible && rows++ < 8) yield return part;
            yield break;
        }
        foreach (var part in _expandedPanel.Children)
        {
            if (!part.IsVisible) continue;
            if (part != _homePanel) { yield return part; continue; }
            foreach (var row in _homePanel.Children)
            {
                if (!row.IsVisible) continue;
                if (row == _clockPanel)
                {
                    foreach (var inner in _clockPanel.Children)
                        if (inner.IsVisible && rows++ < 8) yield return inner;
                }
                else if (rows++ < 8) yield return row;
            }
        }
    }

    /// <summary>Switching between home and a tool page slides the incoming page in with a soft spring.</summary>
    void AnimateToolSwitch(Control incoming)
    {
        if (!_settings.Animations) return;
        if (incoming.RenderTransform is not TranslateTransform move)
        {
            if (incoming.RenderTransform != null) return;
            incoming.RenderTransform = move = new TranslateTransform();
        }
        Animate(incoming, "switch-opacity", v => incoming.Opacity = v, 0.15, 1, 180, 0, QuadOut);
        Animate(incoming, "switch-slide", v => move.Y = v, 12, 0, 340, 0, Bouncy);
    }

    /// <summary>Gives a clicked control a quick springy recoil back to rest.</summary>
    void PulseElement(Control element, double fromScale = 0.90)
    {
        if (!_settings.Animations) return;
        if (element.RenderTransform is not ScaleTransform scale)
        {
            if (element.RenderTransform != null) return;
            element.RenderTransformOrigin = RelativePoint.Center;
            element.RenderTransform = scale = new ScaleTransform(1, 1);
        }
        Animate(element, "pulse", v => scale.ScaleX = scale.ScaleY = v, fromScale, 1, 280, 0, Snappy);
    }

    // ───────────── sizes ─────────────

    static readonly (string Name, double Scale)[] Sizes = { ("Small", 0.85), ("Normal", 1), ("Large", 1.15), ("Extra large", 1.3) };

    /// <summary>Apply a size preference immediately and keep both states centred on the screen.</summary>
    void ApplyScale()
    {
        Width = BaseWidth * Math.Max(CollapsedScale, ExpandedScale);
        ApplyState(animate: false);
        PositionWindow();
    }

    void FillSizeMenu(MenuItem menu, bool expanded)
    {
        menu.Items.Clear();
        double selected = expanded ? ExpandedScale : CollapsedScale;
        foreach (var (name, scale) in Sizes)
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = Math.Abs(selected - scale) < 0.01 };
            item.Click += (_, _) =>
            {
                if (expanded) _settings.ExpandedScale = scale;
                else _settings.CollapsedScale = scale;
                _settings.Save();
                ApplyScale();
            };
            menu.Items.Add(item);
        }
    }
}
