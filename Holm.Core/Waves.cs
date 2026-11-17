using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch;

/// <summary>
/// How often waves may go out and pop up. Every wave is a note on the inbox server, which allows a few hundred a
/// day, and a friend who waves twenty times shouldn't fill your screen: once per friend every 30 seconds each way,
/// and at most 20 sent an hour.
/// </summary>
internal sealed class WaveLimit
{
    public static readonly TimeSpan PerFriend = TimeSpan.FromSeconds(30);
    public const int PerHour = 20;
    /// <summary>A wave older than this came while Holm wasn't running: it's no wave any more.</summary>
    public const long FreshSeconds = 120;

    readonly Dictionary<string, DateTime> _last = new();
    readonly Queue<DateTime> _hour = new();
    readonly int _perHour;

    public WaveLimit(int perHour = PerHour) => _perHour = perHour;

    /// <summary>Whether this one may go (or show); counts it if so.</summary>
    public bool Allow(string friendKey, DateTime now)
    {
        if (_last.TryGetValue(friendKey, out var last) && now - last < PerFriend && now >= last) return false;
        while (_hour.Count > 0 && now - _hour.Peek() >= TimeSpan.FromHours(1)) _hour.Dequeue();
        if (_hour.Count >= _perHour) return false;
        _last[friendKey] = now;
        _hour.Enqueue(now);
        return true;
    }

    /// <summary>When the next wave to this friend may go, for the menu ("in 20 s").</summary>
    public TimeSpan Wait(string friendKey, DateTime now)
        => _last.TryGetValue(friendKey, out var last) && now - last < PerFriend && now >= last ? PerFriend - (now - last) : TimeSpan.Zero;
}
