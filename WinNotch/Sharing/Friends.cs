using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

// Friends: people you beam to by name, anywhere, without passing a code around each time.
//
// Everyone has a key pair and an inbox (a topic on an ntfy server, https://ntfy.sh unless set otherwise).
// A friend code carries your name, inbox and public key. To send files to a friend, Holm makes a fresh
// share secret, exactly like a Beam code, and leaves it in the friend's inbox as a sealed note; both notches
// then meet on the transit relay with it, as if the code had been copied. The inbox only ever carries these
// small notes, never files.
//
// A note is sealed with AES-256-GCM under a key both friends derive from their own private key and the other's
// public key (ECDH P-256), so only that friend can open it, and it can only have come from the holder of the
// sender's key. The recipient's inbox is authenticated as well, so a note can't be moved to another inbox.

/// <summary>Someone whose friend code you added. Kept in the settings.</summary>
public sealed class Friend
{
    public string Name { get; set; } = "";
    /// <summary>Their inbox on the server.</summary>
    public string Topic { get; set; } = "";
    /// <summary>Their public key: P-256 SubjectPublicKeyInfo, base64url.</summary>
    public string Key { get; set; } = "";
    /// <summary>Their inbox server; null for the default one.</summary>
    public string? Server { get; set; }
    /// <summary>Local request id for a newly accepted friend; lets a later cancellation undo that addition.</summary>
    public string? AddedByRequestId { get; set; }
}

/// <summary>Who we are to our friends: a name, an inbox and a key pair.</summary>
public sealed class FriendIdentity : IDisposable
{
    public const string DefaultServer = "https://ntfy.sh";
    const string CodePrefix = "WNF1.";
    static readonly Regex CodeInText = new(@"WNF1\.[A-Za-z0-9_-]{40,2000}");

    public string Name { get; }
    public string Topic { get; }
    /// <summary>Server URL without a trailing slash.</summary>
    public string Server { get; }
    public ECDiffieHellman Key { get; }
    public string PublicKey { get; }

    public FriendIdentity(string name, string topic, string? server, ECDiffieHellman key)
    {
        Name = name;
        Topic = topic;
        Server = NormalizeServer(server);
        Key = key;
        PublicKey = Base64Url(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>A new inbox (130 random bits, so nobody can guess it) and a new key pair.</summary>
    public static FriendIdentity Create(string name, string? server)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var topic = new StringBuilder("wnf-");
        foreach (byte b in RandomNumberGenerator.GetBytes(26)) topic.Append(alphabet[b & 31]);
        return new FriendIdentity(name, topic.ToString(), server, ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));
    }

    public FriendIdentity WithName(string name) => new(name, Topic, Server, Key);

    public static string NormalizeServer(string? server)
        => string.IsNullOrWhiteSpace(server) ? DefaultServer : server.Trim().TrimEnd('/');

    /// <summary>What you give a friend so they can add you: "WNF1." and base64url(JSON).</summary>
    public string FriendCode => CodePrefix + Base64Url(JsonSerializer.SerializeToUtf8Bytes(Card(), CodeJson));

    /// <summary>Us, as a friend sees us.</summary>
    public Friend AsFriend() => new() { Name = Name, Topic = Topic, Key = PublicKey, Server = Server == DefaultServer ? null : Server };

    CodeCard Card() => new() { N = Name, T = Topic, K = PublicKey, S = Server == DefaultServer ? null : Server };

    /// <summary>The friend code in a piece of text (as copied from a chat, say), or null.</summary>
    public static string? FindFriendCode(string? text)
        => text != null && text.Length <= 10_000 && CodeInText.Match(text) is { Success: true } match ? match.Value : null;

    /// <summary>Reads a friend code anywhere in the text. Null if there is none or it is damaged.</summary>
    public static Friend? ParseFriendCode(string? text)
    {
        if (FindFriendCode(text) is not { } code) return null;
        try { return FromCard(JsonSerializer.Deserialize<CodeCard>(FromBase64Url(code[CodePrefix.Length..]), CodeJson)); }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException) { return null; }
    }

    static Friend? FromCard(CodeCard? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.T) || string.IsNullOrWhiteSpace(card.K)) return null;
        if (!Regex.IsMatch(card.T, "^[A-Za-z0-9_-]{1,64}$")) return null;
        using (var check = ECDiffieHellman.Create())
        {
            check.ImportSubjectPublicKeyInfo(FromBase64Url(card.K), out _);
            if (check.KeySize != 256) return null;
        }
        string? server = card.S == null ? null : NormalizeServer(card.S);
        if (server != null && !(Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")) return null;
        string name = new string((card.N ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "Friend";
        return new Friend { Name = name.Length > 40 ? name[..40] : name, Topic = card.T, Key = card.K, Server = server };
    }

    public void Dispose() => Key.Dispose();

    sealed class CodeCard
    {
        public string? N { get; set; }
        public string? T { get; set; }
        public string? K { get; set; }
        public string? S { get; set; }
    }

    static readonly JsonSerializerOptions CodeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>A note from one friend to another.</summary>
/// <param name="Secret">For <see cref="FriendNoteKind.Files"/>: the share secret to meet on the relay with.</param>
/// <param name="Code">For friend requests and legacy <see cref="FriendNoteKind.AddedYou"/> notes: the sender's friend code.</param>
/// <param name="RequestId">For friend requests, responses and cancellations: the request to which the note belongs.</param>
/// <param name="Accepted">For friend responses: whether the request was accepted.</param>
/// <param name="Kind">The action being sent: file offer, friend addition, or friend removal.</param>
public sealed record FriendNote(FriendNoteKind Kind, string From, long Sent, byte[]? Secret = null, string? Code = null,
                                string? RequestId = null, bool? Accepted = null);

public enum FriendNoteKind
{
    /// <summary>"I'd like to send you files": come to the relay with this secret.</summary>
    Files,
    /// <summary>"I added you as a friend": here is my code.</summary>
    AddedYou,
    /// <summary>"Would you like to be friends?": here is my code and a request id.</summary>
    FriendRequest,
    /// <summary>"I accept/decline your friend request": includes its request id.</summary>
    FriendResponse,
    /// <summary>"I cancelled my friend request": includes the sender's code and request id.</summary>
    FriendRequestCancelled,
    /// <summary>"I removed you as a friend": remove my card from your list too.</summary>
    RemovedYou,
    /// <summary>"Is your Holm running?" Asked when you're about to send files.</summary>
    Ping,
    /// <summary>"Yes, my Holm is running": the answer to <see cref="Ping"/>.</summary>
    Here,
    /// <summary>A wave: "thinking of you". Holm before 1.7 doesn't know it and leaves it unopened.</summary>
    Wave,
}

public static class FriendNotes
{
    /// <summary>Notes are text messages: this prefix, the sender's public key, and the sealed note.</summary>
    public const string Prefix = "wnf1 ";
    static readonly byte[] KeyLabel = "WinNotch friends v1"u8.ToArray(); // former name, kept for friends on older versions
    const int NonceSize = 12, TagSize = 16;

    sealed class NoteDto
    {
        public string? K { get; set; }
        public string? F { get; set; }
        public long T { get; set; }
        public string? S { get; set; }
        public string? C { get; set; }
        public string? R { get; set; }
        public bool? A { get; set; }
    }

    /// <summary>The key two friends share: SHA-256 over a label and their ECDH secret.</summary>
    static byte[] SharedKey(ECDiffieHellman mine, string theirPublicKey)
    {
        using var theirs = ECDiffieHellman.Create();
        theirs.ImportSubjectPublicKeyInfo(FriendIdentity.FromBase64Url(theirPublicKey), out _);
        return mine.DeriveKeyFromHash(theirs.PublicKey, HashAlgorithmName.SHA256, KeyLabel, null);
    }

    public static string Seal(FriendIdentity me, Friend to, FriendNote note)
    {
        var dto = new NoteDto
        {
            K = note.Kind switch
            {
                FriendNoteKind.Files => "files",
                FriendNoteKind.AddedYou => "added",
                FriendNoteKind.FriendRequest => "request",
                FriendNoteKind.FriendResponse => "response",
                FriendNoteKind.FriendRequestCancelled => "cancel-request",
                FriendNoteKind.RemovedYou => "removed",
                FriendNoteKind.Ping => "ping",
                FriendNoteKind.Here => "here",
                FriendNoteKind.Wave => "wave",
                _ => throw new ArgumentOutOfRangeException(nameof(note)),
            },
            F = note.From,
            T = note.Sent,
            S = note.Secret == null ? null : Convert.ToBase64String(note.Secret),
            C = note.Code,
            R = note.RequestId,
            A = note.Accepted,
        };
        return SealDto(me, to, dto);
    }

    /// <summary>Seals a note of any kind, also one this version doesn't know (to test how unknown kinds are met).</summary>
    internal static string SealKind(FriendIdentity me, Friend to, string kind, string from, long sent)
        => SealDto(me, to, new NoteDto { K = kind, F = from, T = sent });

    static string SealDto(FriendIdentity me, Friend to, NoteDto dto)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(dto);
        var sealedNote = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(sealedNote.AsSpan(0, NonceSize));
        using (var aes = new AesGcm(SharedKey(me.Key, to.Key), TagSize))
            aes.Encrypt(sealedNote.AsSpan(0, NonceSize), plain, sealedNote.AsSpan(NonceSize + TagSize), sealedNote.AsSpan(NonceSize, TagSize),
                        Encoding.UTF8.GetBytes(to.Topic));
        CryptographicOperations.ZeroMemory(plain);
        return Prefix + me.PublicKey + " " + FriendIdentity.Base64Url(sealedNote);
    }

    /// <summary>
    /// Opens a note from our inbox. <paramref name="senderKey"/> is who sealed it, proven by the fact that it
    /// opened; whether they are a friend is up to the caller. Null for anything that isn't a valid note for us.
    /// </summary>
    public static FriendNote? Open(FriendIdentity me, string text, out string senderKey)
    {
        senderKey = "";
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length > 4096) return null;
        var parts = text[Prefix.Length..].Split(' ');
        if (parts.Length != 2) return null;
        try
        {
            byte[] sealedNote = FriendIdentity.FromBase64Url(parts[1]);
            if (sealedNote.Length < NonceSize + TagSize + 2) return null;
            var plain = new byte[sealedNote.Length - NonceSize - TagSize];
            using (var aes = new AesGcm(SharedKey(me.Key, parts[0]), TagSize))
                aes.Decrypt(sealedNote.AsSpan(0, NonceSize), sealedNote.AsSpan(NonceSize + TagSize), sealedNote.AsSpan(NonceSize, TagSize), plain,
                            Encoding.UTF8.GetBytes(me.Topic));
            var dto = JsonSerializer.Deserialize<NoteDto>(plain);
            if (dto == null) return null;
            string from = new string((dto.F ?? "").Where(c => !char.IsControl(c)).Take(40).ToArray()).Trim();
            FriendNote? note = dto.K switch
            {
                "files" when dto.S != null && Convert.FromBase64String(dto.S) is { Length: ShareCode.SecretLength } secret
                    => new FriendNote(FriendNoteKind.Files, from, dto.T, Secret: secret),
                "added" when FriendIdentity.ParseFriendCode(dto.C) is { } card && card.Key == parts[0]
                    => new FriendNote(FriendNoteKind.AddedYou, from, dto.T, Code: dto.C),
                "request" when FriendIdentity.ParseFriendCode(dto.C) is { } card && card.Key == parts[0] && Guid.TryParseExact(dto.R, "N", out _)
                    => new FriendNote(FriendNoteKind.FriendRequest, from, dto.T, Code: dto.C, RequestId: dto.R),
                "response" when Guid.TryParseExact(dto.R, "N", out _) && dto.A.HasValue
                    => new FriendNote(FriendNoteKind.FriendResponse, from, dto.T, RequestId: dto.R, Accepted: dto.A),
                "cancel-request" when FriendIdentity.ParseFriendCode(dto.C) is { } card && card.Key == parts[0] && Guid.TryParseExact(dto.R, "N", out _)
                    => new FriendNote(FriendNoteKind.FriendRequestCancelled, from, dto.T, Code: dto.C, RequestId: dto.R),
                "removed" => new FriendNote(FriendNoteKind.RemovedYou, from, dto.T),
                "ping" => new FriendNote(FriendNoteKind.Ping, from, dto.T),
                "here" => new FriendNote(FriendNoteKind.Here, from, dto.T),
                "wave" => new FriendNote(FriendNoteKind.Wave, from, dto.T),
                _ => null, // a kind from a newer Holm: left unopened, as older ones leave "wave"
            };
            if (note != null) senderKey = parts[0];
            return note;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException) { return null; }
    }
}

/// <summary>A friend's inbox on the ntfy server: leaving notes there, and following our own.</summary>
public static class FriendInbox
{
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan, // our inbox is one long request; posting has its own token
        DefaultRequestHeaders = { { "User-Agent", "Holm-Friends/1" } },
    };

    static string InboxUrl(string? server, string topic) => $"{FriendIdentity.NormalizeServer(server)}/{Uri.EscapeDataString(topic)}";

    static string Host(string? server)
        => Uri.TryCreate(FriendIdentity.NormalizeServer(server), UriKind.Absolute, out var uri) ? uri.Host : "the server";

    /// <exception cref="IOException">The server couldn't be reached or refused the note; the message says which.</exception>
    public static async Task PostAsync(Friend to, string note, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            response = await Http.PostAsync(InboxUrl(to.Server, to.Topic), new StringContent(note, Encoding.UTF8, "text/plain"), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new IOException("Couldn't reach " + Host(to.Server), ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new IOException($"{Host(to.Server)} refused the note ({(int)response.StatusCode})");
        }
    }

    /// <summary>
    /// Follows our inbox until <paramref name="ct"/> fires, reconnecting as needed. <paramref name="received"/> gets each
    /// note's id, its age in seconds by the server's clock (so a PC clock that is off doesn't matter) and its text.
    /// <paramref name="since"/> is a Unix time to catch up from (null: only new notes); <paramref name="handled"/> reports
    /// the time of each message, so a restart can pick up what came meanwhile (ntfy keeps messages for 12 hours).
    /// Catching up by time can repeat the last message, so the caller skips ids it has already seen. (By id it would be
    /// exact, but for an id that has expired ntfy replays everything it still has.)
    /// </summary>
    public static async Task FollowAsync(FriendIdentity me, long? since, Func<string, long, string, Task> received, Action<long> handled,
                                         Action<string>? log, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string url = InboxUrl(me.Server, me.Topic) + "/json" + (since != null ? "?since=" + since : "");
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
                log?.Invoke("Friends inbox connected");
                delay = TimeSpan.FromSeconds(2);
                long serverNow = 0; // the stream opens with the server's current time, before any catching up
                while (true)
                {
                    // ntfy sends a keepalive every 45 s; silence means the connection died (sleep, a new Wi-Fi).
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromSeconds(100));
                    string? line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Length == 0 || !TryParse(line, out bool isMessage, out long time, out string? id, out string? text)) continue;
                    if (time > 0) since = time; // keepalives move the catch-up point on too
                    serverNow = Math.Max(serverNow, time);
                    if (!isMessage || id == null) continue;
                    if (text != null && text.StartsWith(FriendNotes.Prefix, StringComparison.Ordinal))
                        await received(id, Math.Max(0, serverNow - time), text).ConfigureAwait(false);
                    handled(time);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log?.Invoke("Friends inbox: " + ex.Message); }

            try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(2).Ticks));
        }
    }

    /// <summary>One line of the JSON stream. False for anything that isn't an ntfy event.</summary>
    public static bool TryParse(string line, out bool isMessage, out long time, out string? id, out string? text)
    {
        isMessage = false;
        time = 0;
        id = text = null;
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (root.TryGetProperty("time", out var t)) t.TryGetInt64(out time);
            isMessage = root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String && ev.GetString() == "message";
            if (root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String) id = i.GetString();
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) text = m.GetString();
            return true;
        }
        catch (JsonException) { return false; }
    }
}
