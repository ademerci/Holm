using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WinNotch;

/// <summary>Something copied: a text, or a picture kept as a PNG in Holm's temp folder.</summary>
internal sealed class ClipEntry
{
    /// <summary>Same content, same key: copying it again moves it to the top instead of adding it twice.</summary>
    public string Key { get; init; } = "";
    public string? Text { get; init; }
    public string? ImagePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTime CopiedAt { get; set; }
    /// <summary>A small picture for the list (a frozen BitmapSource), made by the window.</summary>
    public object? Thumb { get; init; }

    public bool IsImage => ImagePath != null;
}

/// <summary>
/// The last <see cref="Max"/> things copied, newest first. Kept in memory only (pictures in a temp folder that is
/// emptied when Holm starts and quits): never saved with the settings, never logged, never sent.
/// </summary>
internal sealed class ClipboardHistory
{
    public const int Max = 20;
    /// <summary>Longer texts are left out: nobody picks a whole book from a list.</summary>
    public const int MaxTextLength = 200_000;

    readonly List<ClipEntry> _items = new();

    public IReadOnlyList<ClipEntry> Items => _items;

    /// <summary>Moves an entry with this key to the top, as if just copied. False when there is none.</summary>
    public bool Bump(string key, DateTime now)
    {
        int i = _items.FindIndex(e => e.Key == key);
        if (i < 0) return false;
        var entry = _items[i];
        entry.CopiedAt = now;
        _items.RemoveAt(i);
        _items.Insert(0, entry);
        return true;
    }

    /// <summary>Adds an entry on top (replacing one with the same key) and returns the ones that fell off the end.</summary>
    public List<ClipEntry> Add(ClipEntry entry)
    {
        var gone = new List<ClipEntry>();
        int i = _items.FindIndex(e => e.Key == entry.Key);
        if (i >= 0) { gone.Add(_items[i]); _items.RemoveAt(i); }
        _items.Insert(0, entry);
        while (_items.Count > Max)
        {
            gone.Add(_items[^1]);
            _items.RemoveAt(_items.Count - 1);
        }
        return gone;
    }

    public ClipEntry? Remove(string key)
    {
        var entry = _items.FirstOrDefault(e => e.Key == key);
        if (entry != null) _items.Remove(entry);
        return entry;
    }

    public List<ClipEntry> Clear()
    {
        var all = _items.ToList();
        _items.Clear();
        return all;
    }

    /// <summary>Whether a copied text belongs in the list: not blank, not huge.</summary>
    public static bool Worth(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;

    public static string TextKey(string text) => "t:" + Hash(Encoding.UTF8.GetBytes(text));

    public static string ImageKey(byte[] pixels, int width, int height) => $"i:{width}x{height}:" + Hash(pixels);

    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data), 0, 16);

    /// <summary>How long ago, short: "now", "4 min", "2 h", "3 d".</summary>
    public static string Ago(DateTime then, DateTime now)
    {
        var age = now - then;
        if (age < TimeSpan.FromMinutes(1)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h";
        return $"{(int)age.TotalDays} d";
    }

    /// <summary>One line for the list: runs of spaces and line breaks become one space, long texts end in "…".</summary>
    public static string Preview(string text, int max = 90)
    {
        var line = new StringBuilder(Math.Min(text.Length, max + 1));
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = line.Length > 0; continue; }
            if (space) { line.Append(' '); space = false; }
            line.Append(c);
            if (line.Length > max) break;
        }
        return line.Length > max ? line.ToString(0, max).TrimEnd() + "…" : line.ToString();
    }
}

/// <summary>What to do with a clipboard change, from the formats that apps use to mark private content.</summary>
internal enum ClipVerdict
{
    /// <summary>Ordinary content: it may go in the history.</summary>
    Keep,
    /// <summary>The app asked clipboard tools to leave it alone: don't read it, don't keep it.</summary>
    Skip,
    /// <summary>A password manager's "ExcludeClipboardContentFromMonitorProcessing": skip it, and it may be wiped later.</summary>
    Private,
}

internal static class ClipboardPrivacy
{
    /// <summary>Set by password managers (KeePass, 1Password, Bitwarden …): clipboard tools must not look at it.</summary>
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    /// <summary>The older way of asking clipboard viewers to look away.</summary>
    public const string ViewerIgnoreFormat = "Clipboard Viewer Ignore";
    /// <summary>A DWORD; 0 keeps it out of Windows' own clipboard history (Win+V), so out of ours too.</summary>
    public const string HistoryFormat = "CanIncludeInClipboardHistory";

    public static ClipVerdict Judge(bool exclude, bool viewerIgnore, bool? canIncludeInHistory)
    {
        if (exclude) return ClipVerdict.Private;
        if (viewerIgnore || canIncludeInHistory == false) return ClipVerdict.Skip;
        return ClipVerdict.Keep;
    }
}

/// <summary>
/// Wipes a copied password after <see cref="After"/>, but only if the clipboard still holds it: the clipboard's
/// sequence number goes up with every change, so the same number means nobody copied anything since.
/// </summary>
internal sealed class ClipboardWipe
{
    public static readonly TimeSpan After = TimeSpan.FromSeconds(30);

    uint? _armed;

    public bool Armed => _armed != null;

    /// <summary>A password was copied; <paramref name="sequence"/> is the clipboard's number for it.</summary>
    public void Arm(uint sequence) => _armed = sequence;

    /// <summary>Something else was copied: that is no business of ours.</summary>
    public void Disarm() => _armed = null;

    /// <summary>Whether to wipe now; true at most once per password.</summary>
    public bool TakeDue(uint currentSequence)
    {
        bool due = _armed == currentSequence;
        _armed = null;
        return due;
    }
}
