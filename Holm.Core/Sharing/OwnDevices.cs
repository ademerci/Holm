using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Sharing;

// Devices of one Holm account: PCs signed in to the same account share the friends identity (the
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

/// <summary>The devices of ours heard from lately. Only touched on the UI thread.</summary>
public sealed class OwnDevices
{
    /// <summary>
    /// How long a device counts as there after it last spoke. A PC answers whenever another device asks.
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
        // The same device under a new id (for instance after a reinstall): the old one is gone, not a
        // second device that never answers.
        var stale = _seen.Values.Where(d => d.Id != id && string.Equals(d.Name, clean, StringComparison.OrdinalIgnoreCase))
                                .Select(d => d.Id).ToList();
        foreach (var old in stale) _seen.Remove(old);
        _seen[id] = new OwnDevice(id, clean, now);
        return !shown || stale.Count > 0;
    }

    /// <summary>The devices that spoke within <see cref="Fresh"/>, by name.</summary>
    public IReadOnlyList<OwnDevice> Recent(DateTime now)
        => _seen.Values.Where(d => now - d.Seen < Fresh && d.Seen <= now + TimeSpan.FromMinutes(1))
                       .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();

    public OwnDevice? Find(string id) => _seen.GetValueOrDefault(id);

    /// <summary>Sending to it just failed: it's gone, whatever it said a minute ago.</summary>
    public void Forget(string id) => _seen.Remove(id);
}
