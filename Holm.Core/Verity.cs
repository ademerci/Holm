using System;

namespace WinNotch;

/// <summary>How Verity is right now.</summary>
internal enum VerityMood { Awake, Happy, Dancing, Asleep }

/// <summary>
/// Verity, the little yellow smiley who moves into the notch once you type her name on Settings › General. She
/// beams while you pet her, dances while music plays and sleeps at night. Found once, she stays: the achievement
/// "Verity" is what says she lives here, so the Holm account brings her to your other computers too.
/// </summary>
internal static class Verity
{
    public const string Code = "verity";

    /// <summary>How long she beams after a pet.</summary>
    public static readonly TimeSpan HappyFor = TimeSpan.FromSeconds(2.5);

    /// <summary>Between 23:00 and 7:00 she sleeps.</summary>
    public static bool Night(DateTime localNow) => localNow.Hour >= 23 || localNow.Hour < 7;

    /// <summary>A pet beats everything, music wakes her up, and otherwise the clock decides.</summary>
    public static VerityMood Mood(DateTime localNow, bool music, DateTime? pettedAt)
        => pettedAt is { } at && localNow >= at && localNow - at < HappyFor ? VerityMood.Happy
           : music ? VerityMood.Dancing
           : Night(localNow) ? VerityMood.Asleep
           : VerityMood.Awake;
}

/// <summary>
/// How Verity gets around on her floor in the open notch, one frame at a time: she rests and breathes, hops to a
/// spot in little bounces, or leaps high with a somersault. On the floor her face stays upright. She crouches before a hop,
/// stretches in the air and squashes when she lands. Music makes her bounce on the spot; asleep she stays put.
/// </summary>
internal sealed class VerityRoam
{
    enum Move { Rest, Hop, Leap }

    const double HopSeconds = 0.42, HopStep = 34, LeapSeconds = 0.8, LandSeconds = 0.16, Beat = 0.5;

    readonly double _size;
    readonly Random _random;
    double _width, _clock;
    Move _move = Move.Rest;
    double _t, _rest = 1, _target, _hopFrom, _sinceLanding = 1;

    /// <summary>Her middle, in px from the left of the floor.</summary>
    public double X { get; private set; }
    /// <summary>How far above the floor she is, in px.</summary>
    public double Lift { get; private set; }
    /// <summary>Above 0 she is squashed flat, below 0 stretched tall.</summary>
    public double Squash { get; private set; }
    /// <summary>Her turn in degrees: only a leap turns her, once round.</summary>
    public double Angle { get; private set; }
    /// <summary>-1 left, 1 right: where she's heading, and where her eyes look.</summary>
    public int Facing { get; private set; } = 1;
    /// <summary>On her way somewhere (her eyes look ahead), not resting.</summary>
    public bool Moving => _move != Move.Rest;

    public VerityRoam(double width, double size, Random random)
    {
        _size = size;
        _random = random;
        _width = Math.Max(width, size);
        X = _width - size; // she starts on the right
    }

    double Min => _size / 2;
    double Max => Math.Max(Min, _width - _size / 2);

    public void Resize(double width)
    {
        _width = Math.Max(width, _size);
        X = Math.Clamp(X, Min, Max);
        _target = Math.Clamp(_target, Min, Max);
    }

    /// <summary>A pet: she leaps for joy, right where she is.</summary>
    public void Jump()
    {
        Begin(Move.Leap);
        _target = X;
    }

    public void Step(double seconds, VerityMood mood)
    {
        seconds = Math.Clamp(seconds, 0, 0.1); // a stalled frame mustn't throw her across the notch
        _clock += seconds;
        _t += seconds;
        _sinceLanding += seconds;
        double land = Math.Max(0, 1 - _sinceLanding / LandSeconds) * 0.2;

        if (mood == VerityMood.Dancing && _move == Move.Rest)
        {
            // On the beat: up, down, squash.
            double p = _clock % Beat / Beat;
            Lift = 4 * p * (1 - p) * _size * 0.3;
            Squash = p < 0.15 ? 0.14 * (1 - p / 0.15) : -0.06;
            return;
        }

        switch (_move)
        {
            case Move.Rest:
                Lift = 0;
                Squash = land + 0.025 * Math.Sin(_clock * Math.PI * 2 / 2.6); // breathing
                if (_t >= _rest && mood != VerityMood.Asleep) Pick();
                break;

            case Move.Hop:
            {
                double p = Math.Min(1, _t / HopSeconds);
                double step = Math.Min(HopStep, Math.Abs(_target - _hopFrom));
                X = Math.Clamp(_hopFrom + Facing * step * p, Min, Max);
                Lift = 4 * p * (1 - p) * _size * 0.45;
                Squash = p < 0.12 ? 0.16 * (1 - p / 0.12) : -0.07;
                if (p < 1) break;
                Land();
                if (Math.Abs(_target - X) < 1 || mood == VerityMood.Asleep) Rest();
                else { _t = 0; _hopFrom = X; }
                break;
            }

            case Move.Leap:
            {
                double p = Math.Min(1, _t / LeapSeconds);
                Lift = 4 * p * (1 - p) * _size * 1.1;
                Squash = p < 0.1 ? 0.2 * (1 - p / 0.1) : -0.08;
                Angle = Facing * 360 * p * p * (3 - 2 * p);
                if (p >= 1) { Angle = 0; Land(); Rest(); }
                break;
            }
        }
    }

    void Pick()
    {
        double roll = _random.NextDouble();
        if (roll < 0.15) { Begin(Move.Leap); _target = X; return; }
        // Somewhere at least a hop away, on either side.
        double target = X;
        for (int i = 0; i < 8 && Math.Abs(target - X) < HopStep; i++) target = Min + _random.NextDouble() * (Max - Min);
        if (Math.Abs(target - X) < 1) { Rest(); return; }
        _target = target;
        Facing = target > X ? 1 : -1;
        Begin(Move.Hop);
        _hopFrom = X;
    }

    void Begin(Move move)
    {
        Angle = 0;
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
