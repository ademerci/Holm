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

/// <summary>
/// Reactions to a message, as on Instagram: each side has at most one emoji on a message, a double click gives a heart,
/// and the quick reactions in the bar can be changed.
/// </summary>
public static class ChatReaction
{
    /// <summary>Instagram's quick reactions, in its order.</summary>
    public static readonly IReadOnlyList<string> Defaults = new[] { "❤️", "😂", "😮", "😢", "😡", "👍" };

    /// <summary>How many quick reactions the bar has.</summary>
    public const int QuickCount = 6;

    /// <summary>What a double click on a message gives.</summary>
    public const string DoubleClick = "❤️";

    /// <summary>More emoji to pick from with “+”, and to choose quick reactions from.</summary>
    public static readonly IReadOnlyList<string> Picker = new[]
    {
        "❤️", "😂", "😮", "😢", "😡", "👍", "🔥", "😍", "🥰", "😘", "😊", "😁", "🤣", "😅", "😭", "🥺",
        "😎", "🤔", "🙄", "😴", "🤯", "😱", "🥳", "😇", "😈", "💀", "🤡", "👀", "👏", "🙌", "🙏", "💪",
        "👎", "👌", "✌️", "🤝", "💯", "✨", "🎉", "💔", "💕", "💙", "💚", "💜", "🖤", "🤍", "⭐", "⚡",
        "✅", "❌", "❓", "‼️", "😬", "🫠", "🫡", "🤗", "🤩", "😤", "🤤", "🤪", "😜", "🐐", "🍕", "⚽",
    };

    /// <summary>One emoji as a reaction may carry it; null if it isn't a single short emoji.</summary>
    public static string? Clean(string? emoji)
    {
        if (emoji == null) return null;
        string text = emoji.Trim();
        if (text.Length is 0 or > 16 || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
        if (new StringInfo(text).LengthInTextElements != 1) return null;
        // Letters and digits aren't reactions (a keycap emoji starts with a digit but goes on with a combining mark).
        if (text.Length == 1 && char.IsLetterOrDigit(text[0])) return null;
        if (char.IsLetter(text, 0)) return null;
        return text;
    }

    /// <summary>The quick reactions as saved, cleaned and filled up to <see cref="QuickCount"/> with the defaults.</summary>
    public static List<string> Quick(IEnumerable<string>? saved)
    {
        var quick = new List<string>();
        foreach (string emoji in saved ?? Enumerable.Empty<string>())
            if (Clean(emoji) is { } clean && !quick.Contains(clean) && quick.Count < QuickCount) quick.Add(clean);
        foreach (string emoji in Defaults)
            if (quick.Count < QuickCount && !quick.Contains(emoji)) quick.Add(emoji);
        return quick;
    }
}

/// <summary>GIFs in the chat: a message carries the link to the GIF, its picture is fetched by whoever shows it.</summary>
public static class ChatGif
{
    /// <summary>The longest link a GIF message may have.</summary>
    public const int MaxUrl = 600;

    /// <summary>The link as it may travel: https, no spaces or control characters, not too long; null otherwise.</summary>
    public static string? Clean(string? url)
    {
        if (url == null) return null;
        string text = url.Trim();
        if (text.Length is 0 or > MaxUrl || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || IsLocal(uri.Host)) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>
    /// Whether a typed or pasted text is a link to a GIF, so it goes as one: a link ending in .gif, or a page on
    /// GIPHY or Tenor (turned into the link of the picture where that is possible).
    /// </summary>
    public static string? FromTyped(string? text)
    {
        if (Clean(text) is not { } url) return null;
        var uri = new Uri(url);
        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath;
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return url;
        // giphy.com/gifs/some-words-ID  or  giphy.com/gifs/ID  →  the picture itself
        if (host is "giphy.com" or "www.giphy.com" && path.StartsWith("/gifs/", StringComparison.Ordinal))
        {
            string last = path.TrimEnd('/').Split('/')[^1];
            string id = last.Contains('-') ? last[(last.LastIndexOf('-') + 1)..] : last;
            if (id.Length is > 4 and < 40 && id.All(char.IsLetterOrDigit)) return $"https://media.giphy.com/media/{id}/giphy.gif";
        }
        if (host is "media.giphy.com" or "i.giphy.com" || host.EndsWith(".giphy.com") && path.Contains("/media/")) return url;
        if (host is "media.tenor.com" or "c.tenor.com") return url;
        return null;
    }

    static bool IsLocal(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) || !host.Contains('.');
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
    /// <summary>Our reaction to the message (an emoji), if any.</summary>
    public string? MyReaction { get; set; }
    /// <summary>The friend's reaction to the message, if any.</summary>
    public string? TheirReaction { get; set; }
    /// <summary>The link to the GIF this message is; <see cref="Text"/> holds it too, for Holm before 1.9.1.</summary>
    public string? Gif { get; set; }
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

    /// <summary>
    /// Sets our reaction (<paramref name="mine"/>) or the friend's on a message; null takes it away. False if there is
    /// no such message or nothing changed.
    /// </summary>
    public bool SetReaction(string key, string id, bool mine, string? emoji)
    {
        if (With(key).FirstOrDefault(m => m.Id == id) is not { } message) return false;
        string? clean = ChatReaction.Clean(emoji);
        if ((mine ? message.MyReaction : message.TheirReaction) == clean) return false;
        if (mine) message.MyReaction = clean;
        else message.TheirReaction = clean;
        Save();
        Changed?.Invoke(key);
        return true;
    }

    public FriendMessage? Find(string key, string id) => With(key).FirstOrDefault(m => m.Id == id);

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
