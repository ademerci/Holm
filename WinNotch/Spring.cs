using System;

namespace WinNotch;

/// <summary>
/// A damped spring as an easing curve, worked out so it has come to rest when the animation ends. With
/// <see cref="Damping"/> 1 it glides in without overshooting; below 1 it swings past the end and settles back
/// with a gentle bounce, like the iPhone's island.
/// </summary>
internal sealed partial class Spring
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
