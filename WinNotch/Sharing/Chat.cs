using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinNotch.Sharing;

// Chat between friends: each message is one sealed friend note (see FriendNotes), so it goes through the same inbox
// as file offers and waves, and a friend whose Holm is closed gets it when it starts (the inbox keeps notes for about
// 12 hours). The history stays on this PC, in chats.json next to the settings.

/// <summary>Message text as it may travel: one note's worth, without control characters other than line breaks.</summary>
public static class ChatText
{
    /// <summary>
    /// The most UTF-8 bytes in one message: the sealed note carries them as base64 and has to stay under the inbox
    /// server's 4 KB message size. A longer text goes as several messages.
    /// </summary>
    public const int MaxBytes = 1500;

    /// <summary>The text a note carries (base64 of its UTF-8); null if it isn't one.</summary>
    public static string? FromNote(string? base64)
    {
        if (base64 == null || base64.Length > (MaxBytes + 2) / 3 * 4) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch (FormatException) { return null; }
    }

    /// <summary>The text with line breaks kept and other control characters dropped; null if nothing is left or it's too long.</summary>
    public static string? Clean(string? text)
    {
        if (text == null) return null;
        string clean = new string(text.Replace("\r\n", "\n").Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0 || Encoding.UTF8.GetByteCount(clean) > MaxBytes) return null;
        return clean;
    }

    /// <summary>Splits a long text into messages of at most <see cref="MaxBytes"/>, between whole characters, preferring spaces.</summary>
    public static List<string> Split(string text)
    {
        var parts = new List<string>();
        string rest = new string(text.Replace("\r\n", "\n").Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
        while (rest.Length > 0)
        {
            if (Encoding.UTF8.GetByteCount(rest) <= MaxBytes) { parts.Add(rest); break; }
            var elements = StringInfo.GetTextElementEnumerator(rest);
            int bytes = 0, cut = 0, lastSpace = -1;
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int size = Encoding.UTF8.GetByteCount(element);
                if (bytes + size > MaxBytes) break;
                bytes += size;
                cut = elements.ElementIndex + element.Length;
                if (char.IsWhiteSpace(element[0])) lastSpace = cut;
            }
            if (lastSpace > cut / 2) cut = lastSpace; // don't cut a word if a space is near enough
            parts.Add(rest[..cut].Trim());
            rest = rest[cut..].Trim();
        }
        parts.RemoveAll(p => p.Length == 0);
        return parts;
    }
}

public enum ChatState { Sending, Sent, Failed, Received }

/// <summary>One message in a conversation. <see cref="Sent"/> is a Unix time.</summary>
public sealed class FriendMessage
{
    public string Id { get; set; } = "";
    public bool Mine { get; set; }
    public string Text { get; set; } = "";
    public long Sent { get; set; }
    public ChatState State { get; set; }
}

/// <summary>The conversations with friends, by their public key, and which have unread messages. UI thread only.</summary>
public sealed class ChatStore
{
    /// <summary>Older messages are dropped: a notch chat, not an archive.</summary>
    public const int Keep = 300;

    readonly string? _path;
    Data _data = new();

    /// <summary>A message was added or changed; the friend's key.</summary>
    public event Action<string>? Changed;

    /// <param name="path">Where the history is kept; null keeps it in memory only (snapshots, tests).</param>
    public ChatStore(string? path)
    {
        _path = path;
        if (path == null || !File.Exists(path)) return;
        try { _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Json) ?? new Data(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Chats unreadable, starting empty: " + ex.Message);
            _data = new Data();
        }
    }

    public IReadOnlyList<FriendMessage> With(string key) => _data.Chats.TryGetValue(key, out var list) ? list : Array.Empty<FriendMessage>();

    public FriendMessage? Last(string key) => _data.Chats.TryGetValue(key, out var list) && list.Count > 0 ? list[^1] : null;

    public int Unread(string key) => _data.Unread.TryGetValue(key, out int n) ? n : 0;

    public int UnreadTotal => _data.Unread.Values.Sum();

    /// <summary>Adds a message; false if one with its id is there already (a note can come twice).</summary>
    public bool Add(string key, FriendMessage message, bool unread)
    {
        if (!_data.Chats.TryGetValue(key, out var list)) _data.Chats[key] = list = new List<FriendMessage>();
        if (list.Any(m => m.Id == message.Id)) return false;
        list.Add(message);
        // A message from a while ago (Holm was closed) goes where it belongs in time.
        if (list.Count > 1 && list[^2].Sent > message.Sent) list.Sort((a, b) => a.Sent.CompareTo(b.Sent));
        if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
        if (unread) _data.Unread[key] = Unread(key) + 1;
        Save();
        Changed?.Invoke(key);
        return true;
    }

    public void SetState(string key, string id, ChatState state)
    {
        if (With(key).FirstOrDefault(m => m.Id == id) is not { } message || message.State == state) return;
        message.State = state;
        Save();
        Changed?.Invoke(key);
    }

    public void MarkRead(string key)
    {
        if (!_data.Unread.Remove(key)) return;
        Save();
        Changed?.Invoke(key);
    }

    /// <summary>A friend was removed: their conversation goes too.</summary>
    public void Forget(string key)
    {
        bool had = _data.Chats.Remove(key) | _data.Unread.Remove(key);
        if (!had) return;
        Save();
        Changed?.Invoke(key);
    }

    void Save()
    {
        if (_path == null || AppSettings.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_data, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Couldn't save chats: " + ex.Message);
        }
    }

    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    sealed class Data
    {
        public Dictionary<string, List<FriendMessage>> Chats { get; set; } = new();
        public Dictionary<string, int> Unread { get; set; } = new();
    }
}
