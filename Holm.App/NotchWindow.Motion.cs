using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.Platform;
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
    }

    readonly Dictionary<(object Target, string Property), Tween> _tweens = new();
    readonly List<((object Target, string Property) Key, Tween Tween)> _finishedTweens = new();
    bool _tweening;

    /// <summary>Animates one value; a new animation of the same target and property replaces the running one.</summary>
    void Animate(object target, string property, Action<double> set, double from, double to, double ms, double delayMs,
                 Func<double, double> ease, Action? done = null)
    {
        _tweens[(target, property)] = new Tween
        {
            Set = set, From = from, To = to, Start = Stopwatch.GetTimestamp(), DelayMs = delayMs, Ms = ms, Ease = ease, Done = done,
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
        foreach (var (key, tween) in _tweens)
        {
            double elapsed = (now - tween.Start) * 1000.0 / Stopwatch.Frequency - tween.DelayMs;
            if (elapsed < 0) continue;
            double t = tween.Ms <= 0 ? 1 : Math.Min(1, elapsed / tween.Ms);
            if (t < 1) tween.Set(tween.From + (tween.To - tween.From) * tween.Ease(t));
            else _finishedTweens.Add((key, tween));
        }
        foreach (var (key, done) in _finishedTweens)
        {
            if (!_tweens.TryGetValue(key, out var current) || current != done) continue;
            _tweens.Remove(key);
            done.Set(done.To);
            done.Done?.Invoke();
        }
        _finishedTweens.Clear();
        if (_tweens.Count == 0) { _tweening = false; return; }
        RequestAnimationFrame(OnTweenFrame);
    }

    // ───────────── views ─────────────

    void InitMotion()
    {
        foreach (var view in new Control[] { _idleView, _compactView, _timerView, _volumeView, _downloadView, _peekView, _taskView, _shareView, _callView, _badgeView, _messageView, _focumonView, _dictationView, _phoneView, _expandedView })
        {
            view.RenderTransform = new ScaleTransform(1, 1);
            // The open notch grows out of the closed one at the top; the small views pop from their middle.
            view.RenderTransformOrigin = view == _expandedView ? new RelativePoint(0.5, 0, RelativeUnit.Relative) : RelativePoint.Center;
        }

        _expandDelay.Tick += (_, _) =>
        {
            _expandDelay.Stop();
            if (_island.IsPointerOver && !_stayClosed && !_taskVisible && !_sharing && !_picking) SetExpanded(true);
        };
        _collapseDelay.Tick += (_, _) =>
        {
            _collapseDelay.Stop();
            // Dragging a slider, a menu open or typing: try again shortly.
            if (PinnedClockOpen) return;
            if (_menuOpen || KeepOpenForTyping || _volSlider.IsDragging || _mixerDragging) { _collapseDelay.Start(); return; }
            if (!_island.IsPointerOver) SetExpanded(false);
        };
        _island.PointerEntered += (_, e) =>
        {
            if (_positionHeld) return;
            _collapseDelay.Stop();
            // An enter without the pointer having moved isn't one: see CloseFromInside.
            if (_stayClosed && Point.Distance(e.GetPosition(this), _closedAt) > 3) _stayClosed = false;
            if (_hovered || _stayClosed || PhoneShowing) return;
            _expandDelay.Start();
            if (!_taskVisible && !_sharing && !_picking) SetHoverBoost(true);
        };
        _island.PointerExited += (_, _) =>
        {
            _prismRim?.ClearLight();
            if (_positionHeld) return;
            _expandDelay.Stop();
            SetHoverBoost(false);
            if (_hovered && !PinnedClockOpen) _collapseDelay.Start();
        };
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            _lastPointer = e.GetPosition(this);
            if (_prismRim != null && _island.IsPointerOver) _prismRim.AimLight(e.GetPosition(_prismRim).X);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // A click opens at once, without waiting for the hover delay.
        _island.PointerReleased += (_, e) =>
        {
            if (e.Handled || _positionHeld || e.InitialPressMouseButton != MouseButton.Left) return;
            // Jelly: a click on the open notch where nothing answers it makes the notch jiggle.
            if (_hovered && Spring.Jelly) { Jiggle(); return; }
            if (_hovered || _taskVisible || _sharing || _picking || PhoneShowing) return;
            _expandDelay.Stop();
            _stayClosed = false;
            SetExpanded(true);
        };
    }

    /// <summary>
    /// Closed on purpose from inside the notch (it opened a window): it doesn't open again under the pointer that hasn't
    /// moved, only once the pointer moves over it again, or with a click. Windows doesn't say the mouse entered when it
    /// didn't move; X11 does when another window takes the focus, and the closing notch's squash moves its lower edge
    /// out from under the pointer and back.
    /// </summary>
    bool _stayClosed;
    Point _closedAt, _lastPointer;

    void CloseFromInside()
    {
        _stayClosed = _island.IsPointerOver;
        _closedAt = _lastPointer;
        SetExpanded(false);
    }

    void SetExpanded(bool expand)
    {
        if (_hovered == expand || _hiddenForFullscreen || (expand && PhoneShowing)) return;
        _hoverBoost = false;
        _hovered = expand;
        Diag.Log(expand ? "Expanded" : "Collapsed");
        if (expand)
        {
            _trimTimer.Stop();
            OpenFromAiRing(); // opened from a ring: straight to the AI page
        }
        else
        {
            _trimTimer.Start();
            if (_unlockedWhileOpen != null) Dispatcher.UIThread.Post(ShowAchievementsHeldBack, DispatcherPriority.Background);
        }
        if (!expand)
        {
            SetClockView(false, animate: false);
            ShowTool(Tool.None); // the next time it opens on home
        }
        ApplyState(animate: true);
        ShowPrivacy();
        // Access may have been granted in the system's settings since we last asked.
        if (expand && _settings.Messages && _notificationsRunning && _notifications.Access is not (NotificationAccess.Allowed or NotificationAccess.Unavailable))
            _ = StartNotificationsAsync(ask: false);
    }

    /// <summary>Shows or hides one of the notch's views: the WPF app's pop and fade, or at once.</summary>
    void SetView(Control view, bool show, bool animate)
    {
        bool shown = view.IsHitTestVisible;
        view.IsHitTestVisible = show;
        var scale = view.RenderTransform as ScaleTransform;
        if (!animate || (!show && view.Opacity == 0))
        {
            StopTween(view, "opacity");
            StopTween(view, "scale");
            view.Opacity = show ? 1 : 0;
            view.IsVisible = show;
            if (scale != null) scale.ScaleX = scale.ScaleY = 1;
            return;
        }
        if (show == shown) return; // already showing, or already gone (or on its way out)

        bool open = view == _expandedView;
        void SetScale(double v) { if (scale != null) scale.ScaleX = scale.ScaleY = v; }
        if (show)
        {
            view.IsVisible = true;
            bool reversing = view.Opacity > 0;
            double begin = reversing ? 0 : open ? 35 : 95;
            Animate(view, "opacity", v => view.Opacity = v, view.Opacity, 1, open ? 220 : 180, begin, QuadOut);
            if (scale != null)
                Animate(view, "scale", SetScale, reversing ? scale.ScaleX : open ? 0.91 : 0.84, 1, open ? OpenMs : 420, open ? 0 : begin, Bouncy);
            return;
        }
        Animate(view, "opacity", v => view.Opacity = v, view.Opacity, 0, open ? 110 : 100, 0, QuadIn,
            () =>
            {
                view.IsVisible = false;
                StopTween(view, "scale");
                SetScale(1);
            });
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
            if (part == _statusSection)
            {
                foreach (var inner in _statusSection.Children)
                    if (inner.IsVisible) yield return inner;
                continue;
            }
            if (part is not StackPanel section) { yield return part; continue; } // the tool bar, a tool's page
            foreach (var row in section.Children)
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
        ApplyState(animate: false);
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
