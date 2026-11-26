using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App.Controls;

/// <summary>
/// Verity drawn directly: a yellow ball with two eyes and a smile. She blinks now and then, bounces while she
/// dances, shuts her eyes with a "z" while she sleeps, sends up hearts when she's petted, pouts when she's hungry,
/// chews a treat, and is surprised, cheers or yawns for a moment (<see cref="Express"/>). Her timers run only
/// while <see cref="Active"/> (her view shows), so a hidden Verity costs nothing.
/// </summary>
internal sealed class VerityFace : Control
{
    static readonly TimeSpan CueFor = TimeSpan.FromSeconds(1.6);

    static readonly IBrush Body = new RadialGradientBrush
    {
        Center = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#FFF7E35A"), 0), new GradientStop(Color.Parse("#FFF0D52E"), 0.62), new GradientStop(Color.Parse("#FFD4A915"), 1) },
    }.ToImmutable();
    // The soft sheen on her upper left, no hard spot.
    static readonly IBrush Glow = new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFA, 0xD0), 0), new GradientStop(Color.FromArgb(0, 0xFF, 0xFA, 0xD0), 1) },
    }.ToImmutable();
    static readonly IBrush Face = new SolidColorBrush(Color.Parse("#FF15120A")).ToImmutable();
    static readonly IBrush Heart = new SolidColorBrush(Color.Parse("#FFFF375F")).ToImmutable();
    static readonly TimeSpan HeartsFor = TimeSpan.FromSeconds(1.4);

    static readonly TimeSpan HopFor = TimeSpan.FromSeconds(0.5);

    readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _blink = new(), _hop = new(), _yawn = new();
    readonly Random _random = new();
    VerityMood _mood;
    VerityCue _cue;
    bool _active, _animate = true, _blinking, _posed, _chewing, _sleepy;
    double _squash, _angle, _look;
    DateTime _started = DateTime.UtcNow, _pettedAt = DateTime.MinValue, _hopAt = DateTime.MinValue, _cueAt = DateTime.MinValue;

    public VerityFace()
    {
        // Late in the evening she yawns now and then.
        _yawn.Tick += (_, _) =>
        {
            _yawn.Interval = TimeSpan.FromSeconds(25 + _random.NextDouble() * 30);
            Express(VerityCue.Yawn);
        };
        _frame.Tick += (_, _) => { InvalidateVisual(); Schedule(); };
        _blink.Tick += (_, _) =>
        {
            _blinking = !_blinking;
            _blink.Interval = _blinking ? TimeSpan.FromMilliseconds(140) : TimeSpan.FromSeconds(2.5 + _random.NextDouble() * 4);
            InvalidateVisual();
        };
        // Left alone (in the closed notch), she hops on the spot now and then.
        _hop.Tick += (_, _) =>
        {
            _hop.Interval = TimeSpan.FromSeconds(5 + _random.NextDouble() * 8);
            _hopAt = DateTime.UtcNow;
            Schedule();
        };
    }

    /// <summary>
    /// Her pose from <see cref="WinNotch.VerityRoam"/>: squashed (above 0) or stretched, turned by an angle (a
    /// leap's somersault), and her eyes looking left (-1) or right (1). Once posed, she leaves moving to whoever poses her.
    /// </summary>
    public void Pose(double squash, double angle, double look)
    {
        _posed = true;
        if (_squash == squash && _angle == angle && _look == look) return;
        (_squash, _angle, _look) = (squash, angle, look);
        InvalidateVisual();
    }

    public VerityMood Mood
    {
        get => _mood;
        set { if (_mood != value) { _mood = value; InvalidateVisual(); Schedule(); } }
    }

    /// <summary>Her view shows: only then does she blink, dance or float hearts.</summary>
    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Schedule(); } }
    }

    /// <summary>Settings › Animations: off, she still changes her face but doesn't move.</summary>
    public bool Animate
    {
        get => _animate;
        set { if (_animate != value) { _animate = value; Schedule(); } }
    }

    /// <summary>A pet: hearts rise from her.</summary>
    public void Pet()
    {
        _pettedAt = DateTime.UtcNow;
        InvalidateVisual();
        Schedule();
    }

    /// <summary>A moment on her face: surprised, cheering (with hearts) or a yawn.</summary>
    public void Express(VerityCue cue)
    {
        if (cue == VerityCue.Yawn && _mood is VerityMood.Asleep or VerityMood.Happy) return;
        _cue = cue;
        _cueAt = DateTime.UtcNow;
        if (cue == VerityCue.Cheer) _pettedAt = _cueAt;
        InvalidateVisual();
        Schedule();
    }

    /// <summary>Left alone (in the closed notch): a hop on the spot, now.</summary>
    public void Hop()
    {
        _hopAt = DateTime.UtcNow;
        Schedule();
    }

    /// <summary>She's eating: her mouth chews.</summary>
    public bool Chewing
    {
        get => _chewing;
        set { if (_chewing != value) { _chewing = value; InvalidateVisual(); Schedule(); } }
    }

    /// <summary>Late in the evening: she yawns now and then.</summary>
    public bool Sleepy
    {
        get => _sleepy;
        set { if (_sleepy != value) { _sleepy = value; Schedule(); } }
    }

    bool HeartsShowing => DateTime.UtcNow - _pettedAt < HeartsFor;
    bool Hopping => DateTime.UtcNow - _hopAt < HopFor;
    VerityCue Cue => DateTime.UtcNow - _cueAt < CueFor ? _cue : VerityCue.None;

    void Schedule()
    {
        bool moving = _active && _animate && (!_posed && (_mood == VerityMood.Dancing || Hopping) || HeartsShowing || Cue != VerityCue.None || _chewing);
        if (moving && !_frame.IsEnabled) { _started = DateTime.UtcNow; _frame.Start(); }
        else if (!moving && _frame.IsEnabled) { _frame.Stop(); InvalidateVisual(); }
        bool blinks = _active && _mood is VerityMood.Awake or VerityMood.Dancing;
        if (blinks && !_blink.IsEnabled) { _blinking = false; _blink.Interval = TimeSpan.FromSeconds(1.5 + _random.NextDouble() * 3); _blink.Start(); }
        else if (!blinks && _blink.IsEnabled) { _blink.Stop(); _blinking = false; }
        bool hops = _active && _animate && !_posed && _mood == VerityMood.Awake;
        if (hops && !_hop.IsEnabled) { _hop.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 6); _hop.Start(); }
        else if (!hops && _hop.IsEnabled) _hop.Stop();
        bool yawns = _active && _sleepy && _mood is VerityMood.Awake or VerityMood.Hungry;
        if (yawns && !_yawn.IsEnabled) { _yawn.Interval = TimeSpan.FromSeconds(8 + _random.NextDouble() * 20); _yawn.Start(); }
        else if (!yawns && _yawn.IsEnabled) _yawn.Stop();
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        bool big = size >= 24;
        double ball = big ? size * 0.82 : size; // room above her for hearts and the "z"
        double x = (Bounds.Width - ball) / 2, y = Bounds.Height - ball;
        double seconds = (DateTime.UtcNow - _started).TotalSeconds;

        double squash = 0, angle = 0, look = 0;
        if (_posed) (squash, angle, look) = (_squash, _angle, _look);
        else if (_mood == VerityMood.Dancing && _active && _animate)
        {
            // Dancing: a hop on every beat, squashed a little where she lands.
            double hop = Math.Abs(Math.Sin(seconds * Math.PI * 2));
            y -= hop * ball * (big ? 0.12 : 0.08);
            squash = (1 - hop) * 0.06;
        }
        else if (Hopping && _active && _animate)
        {
            // A little hop on the spot: crouch, up stretched, down squashed.
            double p = (DateTime.UtcNow - _hopAt).TotalSeconds / HopFor.TotalSeconds;
            y -= 4 * p * (1 - p) * ball * 0.22;
            squash = p < 0.15 ? 0.14 * (1 - p / 0.15) : p > 0.85 ? 0.14 * (p - 0.85) / 0.15 : -0.07;
        }
        // Squashed she grows wide and stays on the floor; stretched she grows tall.
        var body = new Rect(x - ball * squash / 2, y + ball * squash, ball * (1 + squash), ball * (1 - squash));
        double r = body.Width / 2, ry = body.Height / 2;
        var c = body.Center;
        var turned = dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y));
        dc.DrawEllipse(Body, null, c, r, ry);
        dc.DrawEllipse(Glow, null, new Point(c.X - r * 0.3, c.Y - ry * 0.42), r * 0.42, ry * 0.3);

        // Her face, as in the picture: two tall, narrow eyes close together above the middle, and one wide, thin
        // smile whose corners turn up a little. It stays the same in every mood; only sleep and a blink close her
        // eyes. On her way somewhere, her face turns a little toward where she's going.
        double stroke = Math.Max(1, ball * 0.026);
        var pen = new Pen(Face, stroke, lineCap: PenLineCap.Round);
        var cue = Cue;
        double cueTime = (DateTime.UtcNow - _cueAt).TotalSeconds / CueFor.TotalSeconds;
        bool closed = _mood == VerityMood.Asleep || _blinking || cue == VerityCue.Yawn;
        double gaze = look * r * 0.1;
        foreach (double side in new[] { -1.0, 1.0 })
        {
            var eye = new Point(c.X + side * r * 0.24 + gaze, c.Y - ry * 0.2);
            if (cue == VerityCue.Cheer) // ^ ^
                dc.DrawGeometry(null, pen, Arc(eye.X - r * 0.1, eye.Y + ry * 0.06, eye.X + r * 0.1, eye.Y + ry * 0.06, eye.Y - ry * 0.1));
            else if (closed) dc.DrawGeometry(null, pen, Arc(eye.X - r * 0.09, eye.Y + ry * 0.04, eye.X + r * 0.09, eye.Y + ry * 0.04, eye.Y + ry * 0.1));
            else if (cue == VerityCue.Surprised) dc.DrawEllipse(Face, null, eye, Math.Max(1.2, r * 0.11), Math.Max(1.8, ry * 0.17));
            else dc.DrawEllipse(Face, null, eye, Math.Max(0.9, r * 0.075), Math.Max(1.6, ry * 0.145));
        }
        double cornerY = c.Y + ry * 0.24, mx = c.X + gaze * 0.6;
        if (cue is VerityCue.Surprised or VerityCue.Yawn)
        {
            // An "o", or a yawn that opens wide and closes again.
            double open = cue == VerityCue.Yawn ? Math.Sin(Math.Min(1, cueTime) * Math.PI) : 1;
            double wide = cue == VerityCue.Yawn ? r * 0.17 : r * 0.13, tall = cue == VerityCue.Yawn ? ry * (0.06 + 0.22 * open) : ry * 0.14;
            dc.DrawEllipse(Face, null, new Point(mx, cornerY + ry * 0.12), wide, tall);
        }
        else if (_chewing)
        {
            // Munching: the mouth opens and shuts.
            double chew = Math.Abs(Math.Sin((DateTime.UtcNow - _started).TotalSeconds * Math.PI * 3));
            dc.DrawEllipse(Face, null, new Point(mx, cornerY + ry * 0.1), r * (0.2 - 0.06 * chew), ry * (0.03 + 0.1 * chew));
        }
        else
        {
            // Hungry, a small pout; cheering, a wide open smile; else her smile.
            bool hungry = _mood == VerityMood.Hungry && cue == VerityCue.None;
            double half = hungry ? r * 0.36 : r * 0.7, dip = hungry ? -ry * 0.12 : ry * 0.42, lift = hungry ? ry * 0.08 : -ry * 0.1;
            var smile = new StreamGeometry();
            using (var g = smile.Open())
            {
                g.BeginFigure(new Point(mx - half, cornerY + lift), cue == VerityCue.Cheer);
                g.CubicBezierTo(new Point(mx - half * 0.74, cornerY + dip), new Point(mx + half * 0.74, cornerY + dip), new Point(mx + half, cornerY + lift));
                g.EndFigure(cue == VerityCue.Cheer);
            }
            dc.DrawGeometry(cue == VerityCue.Cheer ? Face : null, pen, smile);
        }

        turned.Dispose(); // the hearts and the "z" stay upright
        if (!big) return;
        if (_mood == VerityMood.Asleep)
        {
            var z = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size * 0.2, Look.InkA(0x99));
            dc.DrawText(z, new Point(c.X + r * 0.75, size * 0.02));
        }
        if (HeartsShowing)
        {
            double t = (DateTime.UtcNow - _pettedAt).TotalSeconds / HeartsFor.TotalSeconds;
            for (int i = 0; i < 3; i++)
            {
                double k = Math.Clamp(t * 1.4 - i * 0.2, 0, 1);
                if (k <= 0 || k >= 1) continue;
                double hx = c.X + (i - 1) * r * 0.75, hy = y + ball * 0.2 - k * size * 0.45;
                using (dc.PushOpacity(1 - k))
                    dc.DrawGeometry(Heart, null, HeartAt(hx, hy, size * 0.14));
            }
        }
    }

    /// <summary>A curve from one point to another, bending through <paramref name="throughY"/> in the middle.</summary>
    static Geometry Arc(double x1, double y1, double x2, double y2, double throughY)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(x1, y1), false);
        g.QuadraticBezierTo(new Point((x1 + x2) / 2, 2 * throughY - (y1 + y2) / 2), new Point(x2, y2));
        g.EndFigure(false);
        return geometry;
    }

    static Geometry HeartAt(double x, double y, double s)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(x, y + s * 0.9), true);
        g.CubicBezierTo(new Point(x - s * 1.1, y + s * 0.2), new Point(x - s * 0.5, y - s * 0.6), new Point(x, y));
        g.CubicBezierTo(new Point(x + s * 0.5, y - s * 0.6), new Point(x + s * 1.1, y + s * 0.2), new Point(x, y + s * 0.9));
        g.EndFigure(true);
        return geometry;
    }
}
