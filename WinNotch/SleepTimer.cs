using System;

namespace WinNotch;

/// <summary>
/// Pauses the music after 15, 30 or 60 minutes. A <see cref="Countdown"/> underneath, so sleep or a busy PC never
/// makes it late. Times are UTC.
/// </summary>
internal sealed class SleepTimer
{
    public static readonly int[] Choices = { 15, 30, 60 };

    Countdown? _countdown;

    public bool Running => _countdown != null;

    /// <summary>What it was set to.</summary>
    public TimeSpan Length => _countdown?.Length ?? TimeSpan.Zero;

    public void Start(TimeSpan length, DateTime now) => _countdown = new Countdown(length, now);

    public void Cancel() => _countdown = null;

    public TimeSpan Remaining(DateTime now) => _countdown?.Remaining(now) ?? TimeSpan.Zero;

    /// <summary>True once, at the end: the timer is off afterwards, so the music is paused only once.</summary>
    public bool TakeDone(DateTime now)
    {
        if (_countdown?.IsDone(now) != true) return false;
        _countdown = null;
        return true;
    }

    /// <summary>"29 min" (whole minutes, rounded up), "< 1 min" in the last one.</summary>
    public static string Short(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "0 min";
        int minutes = (int)Math.Ceiling(left.TotalMinutes - 1e-9);
        return left < TimeSpan.FromMinutes(1) ? "< 1 min" : $"{minutes} min";
    }

    /// <summary>When the shown minute changes next, so the label can be updated exactly then and no more often.</summary>
    public static TimeSpan UntilNextMinute(TimeSpan left)
    {
        var rest = TimeSpan.FromTicks(left.Ticks % TimeSpan.TicksPerMinute);
        return rest > TimeSpan.Zero ? rest : TimeSpan.FromMinutes(1);
    }
}
