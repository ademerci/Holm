using System;

namespace WinNotch;

/// <summary>
/// Internet outages as the notch tells them: one that lasts past a short grace (a Wi-Fi roam isn't one), and its
/// end with how long it took. Around sleep the network drops anyway, so that is hushed.
/// </summary>
internal sealed class Outage
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    const int IPV4_INTERNET = 0x40, IPV6_INTERNET = 0x400;

    DateTime? _lostAt;
    DateTime _hushedUntil;
    bool _told;

    /// <summary>NLM_CONNECTIVITY flags: internet over IPv4 or IPv6. A captive portal or local network only is not.</summary>
    public static bool HasInternet(int connectivity) => (connectivity & (IPV4_INTERNET | IPV6_INTERNET)) != 0;

    /// <summary>The PC goes to sleep or just woke up: drops until then are no outages.</summary>
    public void Hush(DateTime until)
    {
        _hushedUntil = until;
        _lostAt = null;
        _told = false;
    }

    public void Lost(DateTime now)
    {
        if (now < _hushedUntil || _lostAt != null) return;
        _lostAt = now;
        _told = false;
    }

    /// <summary>After the grace: whether to say it's gone (once).</summary>
    public bool StillLost(DateTime now)
    {
        if (_lostAt is not { } since || _told || now - since < Grace || now < _hushedUntil) return false;
        _told = true;
        return true;
    }

    /// <summary>It's back: how long it was gone, if that was told; null otherwise.</summary>
    public TimeSpan? Back(DateTime now)
    {
        var since = _lostAt;
        bool told = _told;
        _lostAt = null;
        _told = false;
        return since is { } start && told ? now - start : null;
    }

    /// <summary>"40 s", "3 min", "1 h 5 min".</summary>
    public static string Describe(TimeSpan length)
    {
        if (length < TimeSpan.FromMinutes(1)) return $"{Math.Max(1, (int)length.TotalSeconds)} s";
        if (length < TimeSpan.FromHours(1)) return $"{(int)length.TotalMinutes} min";
        int minutes = length.Minutes;
        return minutes == 0 ? $"{(int)length.TotalHours} h" : $"{(int)length.TotalHours} h {minutes} min";
    }
}
