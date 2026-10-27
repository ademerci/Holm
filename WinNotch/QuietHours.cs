using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch;

/// <summary>
/// Quiet hours: between two times of day (over midnight too) messages don't pop out of the notch; they still count
/// on the badge. VIPs get through: a whole app ("app:whatsapp") or a person ("who:Lena Hoffmann").
/// </summary>
internal static class QuietHours
{
    public const string AppPrefix = "app:", PersonPrefix = "who:";

    /// <summary>"22:00" → 22 h; null for anything else.</summary>
    public static TimeSpan? Parse(string? hhmm)
    {
        if (hhmm == null || !TimeSpan.TryParseExact(hhmm.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var time)) return null;
        return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) ? time : null;
    }

    public static string Format(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="now"/> falls in [from, until); "22:00 to 07:00" runs over midnight. Equal times mean never.</summary>
    public static bool IsQuiet(TimeSpan from, TimeSpan until, DateTime now)
    {
        var time = now.TimeOfDay;
        if (from == until) return false;
        return from < until ? time >= from && time < until : time >= from || time < until;
    }

    /// <summary>Whether a message from this app with this first line (who wrote, or the chat) is from a VIP.</summary>
    public static bool IsVip(string app, string title, IEnumerable<string> vips)
    {
        foreach (var vip in vips)
        {
            if (vip.StartsWith(AppPrefix, StringComparison.Ordinal))
            {
                if (string.Equals(vip[AppPrefix.Length..], app, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (vip.StartsWith(PersonPrefix, StringComparison.Ordinal) && Names(vip[PersonPrefix.Length..], title)) return true;
        }
        return false;
    }

    /// <summary>The person's name as whole words in the first line: "Lena" in "Lena Hoffmann" or "Family: Lena", not in "Magdalena".</summary>
    static bool Names(string name, string title)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        return Regex.IsMatch(title, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Whether a message that just came in should stay silent (it still counts on the badge).</summary>
    public static bool Silences(bool on, string? from, string? until, DateTime now, string app, string title, IEnumerable<string> vips)
        => on && Parse(from) is { } start && Parse(until) is { } end && IsQuiet(start, end, now) && !IsVip(app, title, vips);

    /// <summary>The people to offer as VIPs: who wrote lately, without duplicates, at most <paramref name="max"/>.</summary>
    public static List<string> RecentPeople(IEnumerable<(string Title, DateTimeOffset Time)> messages, int max = 8)
        => messages.Where(m => !string.IsNullOrWhiteSpace(m.Title))
                   .OrderByDescending(m => m.Time)
                   .Select(m => m.Title.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .Take(max)
                   .ToList();
}
