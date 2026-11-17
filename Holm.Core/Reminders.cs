using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch;

/// <summary>Something to be reminded of. Kept in the settings, so it survives a restart. <see cref="Due"/> is UTC.</summary>
internal sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
}

/// <summary>
/// Reads a reminder typed in plain words, in English or German: "20 min laundry", "1 h 30 min call Lena",
/// "14:30 dentist", "9 Uhr Arzt", "tomorrow 8:15 train", "morgen 7 Uhr Joggen", "2:30pm pick up the kids".
/// A time of day that has passed today means tomorrow.
/// </summary>
internal static class ReminderParser
{
    public static readonly TimeSpan Longest = TimeSpan.FromDays(7);

    // "20 min", "1h", "1 h 30 min", "1,5 Stunden", "90 s" (one or more of these in a row)
    static readonly Regex Relative = new(
        @"(?<![\w:.,])(?:in\s+)?(?<all>(?:\d+(?:[.,]\d+)?\s*(?:stunden|stunde|std|hours|hour|hrs|hr|h|minuten|minute|minutes|mins|min|m|sekunden|seconds|secs|sec|s)(?:\b|(?=\d))\s*)+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex RelativePart = new(
        @"(?<n>\d+(?:[.,]\d+)?)\s*(?<unit>stunden|stunde|std|hours|hour|hrs|hr|h|minuten|minute|minutes|mins|min|m|sekunden|seconds|secs|sec|s)(?:\b|(?=\d))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "14:30", "14.30", "9 Uhr", "9:15 Uhr", "2pm", "2:30 pm", with "um"/"at" in front
    static readonly Regex Clock = new(
        @"(?<![\w:.,])(?:(?:um|at)\s+)?(?<h>\d{1,2})(?:(?<sep>[:.])(?<m>\d{2}))?\s*(?<suffix>uhr|am|pm|a\.m\.|p\.m\.)?(?![\w:.,])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Day = new(@"(?<![\w])(?<day>übermorgen|uebermorgen|morgen|tomorrow|heute|today)(?![\w])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// When (local time) and what. False if no time could be found, or it is more than a week away.
    /// <paramref name="now"/> is local time.
    /// </summary>
    public static bool TryParse(string? input, DateTime now, out DateTime due, out string text)
    {
        due = default;
        text = "";
        string s = (input ?? "").Trim();
        if (s.Length == 0 || s.Length > 200) return false;

        int days = 0;
        bool dayGiven = false;
        if (Day.Match(s) is { Success: true } day)
        {
            string word = day.Groups["day"].Value.ToLowerInvariant();
            days = word is "übermorgen" or "uebermorgen" ? 2 : word is "morgen" or "tomorrow" ? 1 : 0;
            dayGiven = true;
            s = Cut(s, day);
        }

        if (!dayGiven && Relative.Match(s) is { Success: true } relative)
        {
            var length = TimeSpan.Zero;
            foreach (Match part in RelativePart.Matches(relative.Groups["all"].Value))
            {
                if (!double.TryParse(part.Groups["n"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
                length += part.Groups["unit"].Value.ToLowerInvariant() switch
                {
                    "stunden" or "stunde" or "std" or "hours" or "hour" or "hrs" or "hr" or "h" => TimeSpan.FromHours(n),
                    "sekunden" or "seconds" or "secs" or "sec" or "s" => TimeSpan.FromSeconds(n),
                    _ => TimeSpan.FromMinutes(n),
                };
            }
            if (length <= TimeSpan.Zero || length > Longest) return false;
            due = now + length;
            text = Tidy(Cut(s, relative));
            return true;
        }

        foreach (Match clock in Clock.Matches(s))
        {
            bool hasMinutes = clock.Groups["m"].Success;
            string suffix = clock.Groups["suffix"].Value.ToLowerInvariant().Replace(".", "");
            // A bare number is no time ("buy 3 apples"); "9 Uhr", "9am" and "9:15" are.
            if (!hasMinutes && suffix.Length == 0) continue;
            int hour = int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture);
            int minute = hasMinutes ? int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (suffix is "am" or "pm")
            {
                if (hour is < 1 or > 12) return false;
                hour = hour % 12 + (suffix == "pm" ? 12 : 0);
            }
            if (hour > 23 || minute > 59) return false;

            due = now.Date.AddDays(days).AddHours(hour).AddMinutes(minute);
            if (due <= now)
            {
                if (dayGiven && days > 0) return false; // "tomorrow" in the past can't be
                due = due.AddDays(1);
            }
            text = Tidy(Cut(s, clock));
            return true;
        }
        return false;
    }

    static string Cut(string s, Match match) => s.Remove(match.Index, match.Length).Insert(match.Index, " ");

    /// <summary>What's left once the time is taken out, without dangling words and dashes.</summary>
    static string Tidy(string rest)
    {
        string text = Regex.Replace(rest, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"^(?:[-–:,]|in|um|at|to|for|für|zum|zur)\s+", "", RegexOptions.IgnoreCase).Trim();
        text = text.Trim('-', '–', ':', ',', ' ');
        return text.Length > 0 ? char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..] : "Reminder";
    }

    /// <summary>"in 20 min", "today at 14:30", "tomorrow at 09:00", "Fri at 08:15" (local times).</summary>
    public static string Describe(DateTime due, DateTime now)
    {
        var left = due - now;
        if (left > TimeSpan.Zero && left < TimeSpan.FromMinutes(60))
            return $"in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes - 1e-9))} min";
        string time = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        int days = (due.Date - now.Date).Days;
        return days switch
        {
            0 => "today at " + time,
            1 => "tomorrow at " + time,
            _ => due.ToString("ddd", CultureInfo.GetCultureInfo("en-US")) + " at " + time,
        };
    }
}

/// <summary>The reminders waiting, in order; which are due; and when to look next. Times are UTC.</summary>
internal sealed class ReminderList
{
    readonly List<Reminder> _items;

    public ReminderList(List<Reminder> items)
    {
        _items = items;
        _items.Sort((a, b) => a.Due.CompareTo(b.Due));
    }

    public IReadOnlyList<Reminder> Items => _items;

    public Reminder? Next => _items.Count > 0 ? _items[0] : null;

    public Reminder Add(string text, DateTime dueUtc)
    {
        var reminder = new Reminder { Text = text, Due = dueUtc };
        int at = _items.FindIndex(r => r.Due > dueUtc);
        _items.Insert(at < 0 ? _items.Count : at, reminder);
        return reminder;
    }

    public bool Remove(string id) => _items.RemoveAll(r => r.Id == id) > 0;

    /// <summary>Takes out every reminder whose time has come (also the ones missed while the PC was off).</summary>
    public List<Reminder> TakeDue(DateTime nowUtc)
    {
        var due = _items.Where(r => r.Due <= nowUtc).ToList();
        _items.RemoveAll(r => r.Due <= nowUtc);
        return due;
    }

    /// <summary>
    /// How long to wait before looking again: until the next one is due, but at most a quarter of an hour, so a
    /// clock change or a long sleep never makes a reminder late. Null when there is none.
    /// </summary>
    public TimeSpan? Wait(DateTime nowUtc)
    {
        if (Next is not { } next) return null;
        var wait = next.Due - nowUtc;
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        return wait > TimeSpan.FromMinutes(15) ? TimeSpan.FromMinutes(15) : wait;
    }
}
