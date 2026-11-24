using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Sharing;

// Devices of one Holm account: PCs signed in to the same account, and the phone app, share the friends identity (the
// account syncs it), so they share one inbox too. A note sealed by that identity for itself is from one of our own
// devices. Each device has an id of its own (AppSettings.DeviceId) and puts it in such notes ("D"), so a device skips
// its own notes when they come back from the inbox, and files go to one device only ("N").

/// <summary>Another device of ours that said it is running, and when.</summary>
public sealed record OwnDevice(string Id, string Name, DateTime Seen)
{
    public const int MaxName = 40;

    /// <summary>A device id is a GUID in the "N" format (32 hex digits).</summary>
    public static bool IsId(string? id) => id is { Length: 32 } && Guid.TryParseExact(id, "N", out _);

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// What the phone app tells the account's PCs, for their notch: a notification or a call that came in, the song that
/// started, a timer that runs or went off, the battery that is full. <see cref="Since"/> is a Unix time (a timer's end); <see cref="On"/> is
/// false when it stopped (the music paused, the timer was cancelled).
/// </summary>
public sealed record PhoneStatus(string Kind, string Title, string? Detail, long Since, bool On)
{
    public const string Notification = "notification", Call = "call", Music = "music", Timer = "timer", Battery = "battery";
    static readonly string[] Kinds = { Notification, Call, Music, Timer, Battery };
    const int MaxTitle = 80, MaxDetail = 160;

    /// <summary>A status as a note brings it, checked; null for a kind this version doesn't know or no title.</summary>
    public static PhoneStatus? From(string? kind, string? title, string? detail, long? since, bool? on)
    {
        if (kind == null || Array.IndexOf(Kinds, kind) < 0) return null;
        if (Clean(title, MaxTitle) is not { } cleanTitle) return null;
        return new PhoneStatus(kind, cleanTitle, Clean(detail, MaxDetail), Math.Max(0, since ?? 0), on ?? true);
    }

    static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string one = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return one.Length == 0 ? null : one.Length > max ? one[..max] : one;
    }
}

/// <summary>The devices of ours heard from lately. Only touched on the UI thread.</summary>
public sealed class OwnDevices
{
    /// <summary>
    /// How long a device counts as there after it last spoke. The phone app speaks when it is opened and answers while it
    /// is open; a PC answers whenever another device asks.
    /// </summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    readonly Dictionary<string, OwnDevice> _seen = new(StringComparer.Ordinal);

    /// <summary>Notes that the device spoke; true when it is new here or has a new name (worth showing).</summary>
    public bool Saw(string id, string name, DateTime now)
    {
        if (!OwnDevice.IsId(id)) return false;
        string clean = new string(name.Where(c => !char.IsControl(c)).Take(OwnDevice.MaxName).ToArray()).Trim();
        if (clean.Length == 0) clean = "Holm";
        bool shown = _seen.TryGetValue(id, out var before) && before.Name == clean && now - before.Seen < Fresh;
        _seen[id] = new OwnDevice(id, clean, now);
        return !shown;
    }

    /// <summary>The devices that spoke within <see cref="Fresh"/>, by name.</summary>
    public IReadOnlyList<OwnDevice> Recent(DateTime now)
        => _seen.Values.Where(d => now - d.Seen < Fresh && d.Seen <= now + TimeSpan.FromMinutes(1))
                       .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();

    public OwnDevice? Find(string id) => _seen.GetValueOrDefault(id);

    /// <summary>Sending to it just failed: it's gone, whatever it said a minute ago.</summary>
    public void Forget(string id) => _seen.Remove(id);
}
