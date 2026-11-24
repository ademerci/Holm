using System;
using System.Collections.Generic;

namespace WinNotch;

/// <summary>How Verity is right now.</summary>
internal enum VerityMood { Awake, Happy, Dancing, Asleep, Hungry }

/// <summary>
/// Verity, the little yellow smiley who moves into the notch once you switch her on in Settings › General. She
/// beams while you pet her, dances while music plays, sleeps at night and gets hungry without a treat now and then.
/// The Holm account syncs her switch, so she comes along to your other computers too.
/// </summary>
internal static class Verity
{
    /// <summary>How long she beams after a pet.</summary>
    public static readonly TimeSpan HappyFor = TimeSpan.FromSeconds(2.5);

    /// <summary>This long after her last treat she is hungry.</summary>
    public static readonly TimeSpan HungryAfter = TimeSpan.FromHours(8);

    /// <summary>Between 23:00 and 7:00 she sleeps.</summary>
    public static bool Night(DateTime localNow) => localNow.Hour >= 23 || localNow.Hour < 7;

    /// <summary>The two hours before she sleeps: she yawns now and then.</summary>
    public static bool Sleepy(DateTime localNow) => localNow.Hour is 21 or 22;

    /// <summary>A pet beats everything, music wakes her up, then the clock decides, and awake she may be hungry.</summary>
    public static VerityMood Mood(DateTime localNow, bool music, DateTime? pettedAt, DateTime? fedAt = null)
        => pettedAt is { } at && localNow >= at && localNow - at < HappyFor ? VerityMood.Happy
           : music ? VerityMood.Dancing
           : Night(localNow) ? VerityMood.Asleep
           : fedAt is { } fed && localNow - fed >= HungryAfter ? VerityMood.Hungry
           : VerityMood.Awake;
}

/// <summary>
/// How Verity gets around on her floor in the open notch, one frame at a time: she rests and breathes, hops to a
/// spot in little bounces, or leaps with a somersault. She keeps to the free stretches of her floor (not under the
/// buttons that share her row); put down elsewhere, she hops back to the nearest one. She crouches before a hop,
/// stretches in the air and squashes when she lands. Music makes her bounce on the spot; asleep she stays put. A treat
/// falls, she hops over and eats it; she can be picked up and thrown, and she wanders over to the mouse to see.
/// </summary>
internal sealed class VerityRoam
{
    enum Move { Rest, Hop, Leap, Held, Flying, Eating }

    const double HopSeconds = 0.42, HopStep = 34, LeapSeconds = 0.8, LandSeconds = 0.16, Beat = 0.5, EatSeconds = 1.6,
        Gravity = 900, Curious = 0.5;

    readonly double _size, _headroom;
    readonly Random _random;
    readonly List<(double From, double To)> _zones = new();
    double _width, _clock;
    Move _move = Move.Rest;
    double _t, _rest = 1, _target, _hopFrom, _sinceLanding = 1, _vx, _vy, _treatFall;
    double? _pointer;

    /// <summary>Her middle, in px from the left of the floor.</summary>
    public double X { get; private set; }
    /// <summary>How far above the floor she is, in px.</summary>
    public double Lift { get; private set; }
    /// <summary>Above 0 she is squashed flat, below 0 stretched tall.</summary>
    public double Squash { get; private set; }
    /// <summary>Her turn in degrees: a leap turns her once round, a throw rolls her.</summary>
    public double Angle { get; private set; }
    /// <summary>-1 left, 1 right: where she's heading.</summary>
    public int Facing { get; private set; } = 1;
    /// <summary>Where her eyes look, from -1 (left) to 1 (right): ahead on her way, else at the mouse.</summary>
    public double Gaze { get; private set; }
    /// <summary>On her way somewhere, not resting.</summary>
    public bool Moving => _move != Move.Rest;
    public bool Held => _move == Move.Held;
    public bool Eating => _move == Move.Eating;
    /// <summary>A treat falling or lying on her floor: where, and how high above the floor it still is.</summary>
    public (double X, double Height)? Treat { get; private set; }
    /// <summary>How much of the treat is left while she eats it, 1 to 0.</summary>
    public double TreatLeft => _move == Move.Eating ? Math.Max(0, 1 - _t / EatSeconds) : 1;

    /// <summary>She finished a treat.</summary>
    public event Action? Ate;

    /// <param name="headroom">How high above the floor she may go, in px.</param>
    public VerityRoam(double width, double size, Random random, double? headroom = null)
    {
        _size = size;
        _random = random;
        _headroom = headroom ?? size * 1.1;
        Resize(width);
        X = Math.Clamp(_width - size, Min, Max); // she starts on the right
    }

    double Lowest => _size / 2;
    double Highest => Math.Max(Lowest, _width - _size / 2);

    /// <summary>The free stretch she is on, or the nearest one.</summary>
    (double From, double To) Zone
    {
        get
        {
            var best = _zones[0];
            double bestDistance = double.MaxValue;
            foreach (var zone in _zones)
            {
                double distance = X < zone.From + _size / 2 ? zone.From + _size / 2 - X : X > zone.To - _size / 2 ? X - (zone.To - _size / 2) : 0;
                if (distance < bestDistance) (best, bestDistance) = (zone, distance);
            }
            return best;
        }
    }

    double Min => Zone.From + _size / 2;
    double Max => Math.Max(Min, Zone.To - _size / 2);
    bool Placed => X >= Min - 0.5 && X <= Max + 0.5;

    public void Resize(double width) => SetFloor(width, new[] { (0.0, width) });

    /// <summary>Her floor: its whole width, and the stretches of it that are free. Nowhere free, the whole floor is hers.</summary>
    public void SetFloor(double width, IReadOnlyList<(double From, double To)> free)
    {
        _width = Math.Max(width, _size);
        _zones.Clear();
        foreach (var zone in free)
        {
            var inside = (From: Math.Max(0, zone.From), To: Math.Min(_width, zone.To));
            if (inside.To - inside.From >= _size) _zones.Add(inside);
        }
        if (_zones.Count == 0) _zones.Add((0, _width));
        X = Math.Clamp(X, Lowest, Highest);
        if (Treat is { } treat) Treat = (Math.Clamp(treat.X, Lowest, Highest), treat.Height);
    }

    /// <summary>The mouse over the notch, in px from the left of her floor, or null when it's gone.</summary>
    public void Notice(double? x) => _pointer = x;

    /// <summary>A pet, a new message or an achievement: she leaps for joy, right where she is.</summary>
    public void Jump()
    {
        if (_move is Move.Held or Move.Flying or Move.Eating) return;
        Begin(Move.Leap);
        _target = X;
    }

    /// <summary>A treat drops near <paramref name="x"/>, onto the free stretch she's on: she hops over and eats it.</summary>
    public void Feed(double x)
    {
        if (Treat != null) return;
        Treat = (Math.Clamp(x, Min, Max), _headroom + _size);
        _treatFall = 0;
    }

    public void Grab()
    {
        Begin(Move.Held);
        Lift = Math.Max(Lift, 0);
    }

    /// <summary>Held by the mouse: her middle at <paramref name="x"/>, <paramref name="lift"/> px above the floor.</summary>
    public void HoldAt(double x, double lift)
    {
        if (_move != Move.Held) return;
        X = Math.Clamp(x, Lowest, Highest);
        Lift = Math.Clamp(lift, 0, _headroom);
    }

    /// <summary>Let go at this speed (px a second, up is positive): she flies, bounces and lands.</summary>
    public void Throw(double vx, double vy)
    {
        if (_move != Move.Held) return;
        _vx = Math.Clamp(vx, -900, 900);
        _vy = Math.Clamp(vy, -600, 600);
        Begin(Move.Flying);
    }

    public void Step(double seconds, VerityMood mood)
    {
        seconds = Math.Clamp(seconds, 0, 0.1); // a stalled frame mustn't throw her across the notch
        _clock += seconds;
        _t += seconds;
        _sinceLanding += seconds;
        double land = Math.Max(0, 1 - _sinceLanding / LandSeconds) * 0.2;
        StepTreat(seconds);
        double gaze = _move is Move.Hop ? Facing : _pointer is { } mouse && _move == Move.Rest && mood != VerityMood.Asleep ? Math.Clamp((mouse - X) / 80, -1, 1) : 0;
        Gaze += (gaze - Gaze) * Math.Min(1, seconds * 10);

        if (mood == VerityMood.Dancing && _move == Move.Rest && Treat == null && Placed)
        {
            // On the beat: up, down, squash.
            double p = _clock % Beat / Beat;
            Lift = 4 * p * (1 - p) * Math.Min(_size * 0.3, _headroom);
            Squash = p < 0.15 ? 0.14 * (1 - p / 0.15) : -0.06;
            return;
        }

        switch (_move)
        {
            case Move.Rest:
                Lift = 0;
                Squash = land + 0.025 * Math.Sin(_clock * Math.PI * 2 / 2.6); // breathing
                Angle = Upright(Angle, seconds);
                if (Treat is { Height: 0 } treat)
                {
                    if (Math.Abs(treat.X - X) < 2) Begin(Move.Eating);
                    else HopTo(treat.X);
                }
                else if (!Placed) HopTo(Math.Clamp(X, Min, Max)); // put down beside her stretch, or it moved
                else if (_t >= _rest && mood != VerityMood.Asleep) Pick();
                break;

            case Move.Hop:
            {
                double p = Math.Min(1, _t / HopSeconds);
                double step = Math.Min(HopStep, Math.Abs(_target - _hopFrom));
                X = Math.Clamp(_hopFrom + Facing * step * p, Lowest, Highest);
                Lift = 4 * p * (1 - p) * Math.Min(_size * 0.45, _headroom);
                Squash = p < 0.12 ? 0.16 * (1 - p / 0.12) : -0.07;
                Angle = Upright(Angle, seconds);
                if (p < 1) break;
                Land();
                if (Math.Abs(_target - X) < 1 || mood == VerityMood.Asleep) Rest();
                else { _t = 0; _hopFrom = X; }
                break;
            }

            case Move.Leap:
            {
                double p = Math.Min(1, _t / LeapSeconds);
                Lift = 4 * p * (1 - p) * _headroom;
                Squash = p < 0.1 ? 0.2 * (1 - p / 0.1) : -0.08;
                Angle = Facing * 360 * p * p * (3 - 2 * p);
                if (p >= 1) { Angle = 0; Land(); Rest(); }
                break;
            }

            case Move.Held:
                Squash = -0.1; // dangling
                Angle = Upright(Angle, seconds);
                break;

            case Move.Flying:
                X += _vx * seconds;
                Lift += _vy * seconds;
                _vy -= Gravity * seconds;
                Angle += _vx * seconds / (_size / 2) * 180 / Math.PI; // rolls as she flies
                Squash = -0.06;
                if (X < Lowest || X > Highest) { X = Math.Clamp(X, Lowest, Highest); _vx = -_vx * 0.6; }
                if (Lift > _headroom) { Lift = _headroom; _vy = -Math.Abs(_vy) * 0.3; }
                if (Lift > 0) break;
                Lift = 0;
                _sinceLanding = 0;
                if (_vy < -150) { _vy = -_vy * 0.45; _vx *= 0.7; Squash = 0.2; } // bounces
                else Rest();
                break;

            case Move.Eating:
                Lift = 0;
                Squash = 0.05 * Math.Abs(Math.Sin(_t * Math.PI * 6)); // chewing
                Angle = Upright(Angle, seconds);
                if (_t < EatSeconds) break;
                Treat = null;
                Rest();
                Ate?.Invoke();
                break;
        }
    }

    void StepTreat(double seconds)
    {
        if (Treat is not { Height: > 0 } treat) return;
        _treatFall += Gravity * 0.6 * seconds;
        Treat = (treat.X, Math.Max(0, treat.Height - _treatFall * seconds));
    }

    /// <summary>Rolled or turned: back upright the short way, quickly.</summary>
    static double Upright(double angle, double seconds)
    {
        double off = angle - Math.Round(angle / 360) * 360;
        off -= off * Math.Min(1, seconds * 12);
        return Math.Abs(off) < 0.5 ? 0 : off;
    }

    void Pick()
    {
        double roll = _random.NextDouble();
        if (roll < 0.15) { Begin(Move.Leap); _target = X; return; }
        // Curious: over to the mouse, as far as her stretch goes.
        if (_pointer is { } p && _random.NextDouble() < Curious && Math.Abs(Math.Clamp(p, Min, Max) - X) >= 8) { HopTo(Math.Clamp(p, Min, Max)); return; }
        // Somewhere at least a hop away, on either side.
        double target = X;
        for (int i = 0; i < 8 && Math.Abs(target - X) < HopStep; i++) target = Min + _random.NextDouble() * (Max - Min);
        if (Math.Abs(target - X) < 1) { Rest(); return; }
        HopTo(target);
    }

    void HopTo(double target)
    {
        _target = target;
        Facing = target > X ? 1 : -1;
        Begin(Move.Hop);
        _hopFrom = X;
    }

    void Begin(Move move)
    {
        _move = move;
        _t = 0;
    }

    void Rest()
    {
        Begin(Move.Rest);
        _rest = 1.2 + _random.NextDouble() * 2.8;
    }

    void Land()
    {
        Lift = 0;
        _sinceLanding = 0;
    }
}
