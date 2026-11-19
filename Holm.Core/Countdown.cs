using System;

namespace WinNotch;

/// <summary>
/// A kitchen timer: runs down from a length, can pause and get more time. It keeps the moment it ends rather than
/// counting ticks, so a late or skipped tick (sleep, a busy PC) never makes it run slow. Times are UTC.
/// </summary>
internal sealed class Countdown
{
    DateTime _end;      // while running
    TimeSpan _left;     // while paused

    public Countdown(TimeSpan length, DateTime now)
    {
        if (length <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        _end = now + length;
    }

    /// <summary>What it was set to, plus any time added.</summary>
    public TimeSpan Length { get; private set; }

    public bool Paused { get; private set; }

    public TimeSpan Remaining(DateTime now)
    {
        var left = Paused ? _left : _end - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public bool IsDone(DateTime now) => !Paused && now >= _end;

    public void Pause(DateTime now)
    {
        if (Paused) return;
        _left = Remaining(now);
        Paused = true;
    }

    public void Resume(DateTime now)
    {
        if (!Paused) return;
        _end = now + _left;
        Paused = false;
    }

    /// <summary>More time; after it went off, it starts again from now.</summary>
    public void Add(TimeSpan more, DateTime now)
    {
        Length += more;
        if (Paused) _left += more;
        else _end = (now > _end ? now : _end) + more;
    }

    /// <summary>"4:59", "24:00", "1:05:00": whole seconds, rounded up, so it reads 0:00 only at the very end.</summary>
    public static string Format(TimeSpan left)
    {
        long seconds = (long)Math.Ceiling(left.TotalSeconds - 1e-9);
        if (seconds < 0) seconds = 0;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>"25 minutes", "1 hour", "1 h 30 min".</summary>
    public static string Describe(TimeSpan length)
    {
        int minutes = (int)Math.Round(length.TotalMinutes);
        if (minutes < 60) return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        int hours = minutes / 60, rest = minutes % 60;
        return rest == 0 ? (hours == 1 ? "1 hour" : $"{hours} hours") : $"{hours} h {rest} min";
    }
}
