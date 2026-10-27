using System;
using System.Linq;

namespace WinNotch;

/// <summary>What the idle notch shows on its right: the one thing that matters most.</summary>
internal enum IdleStatus { None, Reminder, Shelf }

/// <summary>The choices behind the idle notch and the "today" card of the open notch.</summary>
internal static class Today
{
    /// <summary>A reminder this close shows in the idle notch.</summary>
    public static readonly TimeSpan ReminderAhead = TimeSpan.FromHours(12);

    /// <summary>The next reminder within 12 hours, else what's on the shelf, else nothing.</summary>
    public static IdleStatus Pick(DateTime? nextReminderUtc, DateTime nowUtc, int shelfCount)
    {
        if (nextReminderUtc is { } due && due - nowUtc <= ReminderAhead) return IdleStatus.Reminder;
        return shelfCount > 0 ? IdleStatus.Shelf : IdleStatus.None;
    }

    /// <summary>The note's card: its first line, and the next one (or how many more there are).</summary>
    public static (string Title, string More) NoteCard(string? note)
    {
        var lines = (note ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return ("Notes", "Jot something down");
        if (lines.Count == 1) return (lines[0], "Your note");
        if (lines.Count == 2) return (lines[0], lines[1]);
        return (lines[0], $"{lines[1]} · +{lines.Count - 2} more");
    }

    /// <summary>The reminder card: the next one and when, or how to add one.</summary>
    public static (string Title, string More) ReminderCard(Reminder? next, DateTime nowLocal)
        => next == null ? ("No reminders", "Add one: 20 min laundry")
                        : (next.Text, ReminderParser.Describe(next.Due.ToLocalTime(), nowLocal));
}
