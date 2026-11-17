using System;

namespace WinNotch;

/// <summary>
/// When the system drive runs low: under 10 % or under 10 GB free. The warning comes once, and only again after
/// the drive had room (12 % and 12 GB) in between, so a drive hovering at the limit doesn't nag.
/// </summary>
internal static class DiskSpace
{
    public const long Gb = 1024L * 1024 * 1024;
    const long LowBytes = 10 * Gb, RoomyBytes = 12 * Gb;
    const double LowShare = 0.10, RoomyShare = 0.12;

    public static bool IsLow(long free, long total) => total > 0 && (free < LowBytes || free < total * LowShare);

    public static bool IsRoomy(long free, long total) => total > 0 && free >= RoomyBytes && free >= total * RoomyShare;

    /// <summary>
    /// Whether to warn now. <paramref name="warned"/> remembers (in the settings) that this low spell was told
    /// already; it is cleared once there is room again.
    /// </summary>
    public static bool ShouldWarn(long free, long total, ref bool warned)
    {
        if (IsRoomy(free, total)) warned = false;
        if (warned || !IsLow(free, total)) return false;
        warned = true;
        return true;
    }

    /// <summary>"8.4 GB free · 6 %".</summary>
    public static string Describe(long free, long total)
    {
        double gb = free / (double)Gb;
        string size = gb < 10 ? gb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Math.Floor(gb).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        int percent = total > 0 ? (int)Math.Floor(100.0 * free / total) : 0;
        return $"{size} GB free · {percent} %";
    }
}
