using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinNotch;

/// <summary>Files waiting for a friend whose Holm wasn't running. Saved with the settings, so they survive a restart.</summary>
internal sealed class QueuedSend
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FriendKey { get; set; } = "";
    public string FriendName { get; set; } = "";
    /// <summary>What to send. Files Holm made (a folder's ZIP, a beamed text) were moved to <see cref="SendLater.Folder"/>.</summary>
    public List<string> Paths { get; set; } = new();
    public bool Text { get; set; }
    public DateTime QueuedUtc { get; set; }
    public DateTime? AskedUtc { get; set; }
    public DateTime? NotedUtc { get; set; }
}

/// <summary>
/// When to look for a friend who is offline, and for how long. Their Holm only says it's running when asked, and
/// every question is a note on the inbox server (a few hundred a day are allowed): so ask every 15 minutes for the
/// first two hours, then hourly; and every six hours leave a note that something waits (which a Holm 1.7 answers
/// as soon as it starts, even hours later). After a week it's given up.
/// </summary>
internal static class SendLater
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(7);
    public static readonly TimeSpan NoteEvery = TimeSpan.FromHours(6);
    public static string Folder => Path.Combine(Path.GetTempPath(), "Holm", "Later");

    public static TimeSpan AskEvery(TimeSpan waited) => waited < TimeSpan.FromHours(2) ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);

    public static bool Expired(QueuedSend send, DateTime now) => now - send.QueuedUtc >= GiveUpAfter;

    public static bool AskDue(QueuedSend send, DateTime now)
        => send.AskedUtc is not { } asked || now - asked >= AskEvery(now - send.QueuedUtc) || now < asked;

    public static bool NoteDue(QueuedSend send, DateTime now)
        => send.NotedUtc is not { } noted || now - noted >= NoteEvery || now < noted;

    /// <summary>How long until something is due for any of them; null when nothing waits.</summary>
    public static TimeSpan? NextLook(IEnumerable<QueuedSend> queue, DateTime now)
    {
        TimeSpan? next = null;
        foreach (var send in queue)
        {
            var times = new List<TimeSpan>
            {
                send.QueuedUtc + GiveUpAfter - now,
                (send.AskedUtc ?? DateTime.MinValue) + AskEvery(now - send.QueuedUtc) - now,
                (send.NotedUtc ?? DateTime.MinValue) + NoteEvery - now,
            };
            var soonest = times.Min();
            if (soonest < TimeSpan.Zero) soonest = TimeSpan.Zero;
            if (next == null || soonest < next) next = soonest;
        }
        return next;
    }

    /// <summary>"Holiday photos.zip", "3 files", "a text".</summary>
    public static string Describe(QueuedSend send)
        => send.Text ? "a text" : send.Paths.Count == 1 ? Path.GetFileName(send.Paths[0]) : $"{send.Paths.Count} files";

    /// <summary>Whether a path is one Holm made for sending (and so may move and delete).</summary>
    public static bool IsHolmsOwn(string path, string tempFolder)
    {
        string root = Path.GetFullPath(tempFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
