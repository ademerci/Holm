using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
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
// Everyone has a key pair and an inbox: a topic on Holm's own relay (relay/, since 1.18), on ntfy.sh before that,
// or on an ntfy server set in the settings.
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
    /// <summary>Their profile picture as they sent it (a small JPEG or PNG, base64); null shows their initials.</summary>
    public string? Picture { get; set; }
    /// <summary>Their status line ("🎧 in focus"), as they sent it.</summary>
    public string? Status { get; set; }
    /// <summary>The color they chose for their initials, "#RRGGBB"; null for the one their name gives.</summary>
    public string? Color { get; set; }
    /// <summary><see cref="FriendProfile.Version"/> of our profile they last received; a different one goes out again.</summary>
    public string? ProfileSent { get; set; }

    /// <summary>Keeps what a friend told us about themselves when their card is replaced by a newer one.</summary>
    public void KeepProfileOf(Friend older)
    {
        Picture ??= older.Picture;
        Status ??= older.Status;
        Color ??= older.Color;
        ProfileSent ??= older.ProfileSent;
    }

    public void TakeProfile(FriendProfile profile)
    {
        if (profile.Name.Length > 0) Name = profile.Name;
        Status = profile.Status;
        Color = profile.Color;
        Picture = profile.Picture == null ? null : Convert.ToBase64String(profile.Picture);
    }
}

/// <summary>
/// How we show ourselves to friends: a name, a status line, a color and a small picture. It travels in one sealed
/// note, so the picture is kept small enough for the note to fit the inbox server's 4 KB message.
/// </summary>
public sealed record FriendProfile(string Name, string? Status, string? Color, byte[]? Picture)
{
    public const int MaxName = 40, MaxStatus = 60;
    /// <summary>The largest picture a note carries; <see cref="ProfilePicture"/> encodes below it.</summary>
    public const int MaxPictureBytes = 1500;
    static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{6}$");

    /// <summary>Changes whenever anything in the profile does: friends who have an older one get it again.</summary>
    public string Version
    {
        get
        {
            using var sha = SHA256.Create();
            byte[] text = Encoding.UTF8.GetBytes($"{Name}\n{Status}\n{Color}\n");
            sha.TransformBlock(text, 0, text.Length, null, 0);
            sha.TransformFinalBlock(Picture ?? Array.Empty<byte>(), 0, Picture?.Length ?? 0);
            return FriendIdentity.Base64Url(sha.Hash!)[..22];
        }
    }

    /// <summary>One line of plain text: no control characters, cut to <paramref name="max"/> whole characters.</summary>
    public static string? CleanText(string? text, int max)
    {
        if (text == null) return null;
        string clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        var elements = new System.Globalization.StringInfo(clean);
        if (elements.LengthInTextElements > max) clean = elements.SubstringByTextElements(0, max).Trim();
        return clean.Length == 0 ? null : clean;
    }

    public static string? CleanColor(string? color) => color != null && ColorPattern.IsMatch(color) ? color.ToUpperInvariant() : null;

    /// <summary>A JPEG or PNG no larger than <see cref="MaxPictureBytes"/>; anything else isn't taken.</summary>
    public static bool IsPicture(byte[]? bytes)
        => bytes is { Length: > 8 and <= MaxPictureBytes }
           && (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
               || bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47);
}

/// <summary>Who we are to our friends: a name, an inbox and a key pair.</summary>
public sealed class FriendIdentity : IDisposable
{
    /// <summary>The inbox server a card without one means: ntfy.sh, for every Holm before the relay (settable for tests).</summary>
    public static string DefaultServer { get; internal set; } = "https://ntfy.sh";
    /// <summary>
    /// Holm's own inbox server (relay/: a Cloudflare Worker). It takes ntfy's POST, so any Holm can leave notes there;
    /// Holm 1.18+ follows its inbox there and on <see cref="DefaultServer"/>, where older friends still write.
    /// </summary>
    public static string RelayServer { get; internal set; } = "https://relay.holmnotch.ch";
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
/// <param name="Profile">For <see cref="FriendNoteKind.Profile"/>: the sender's status, color and picture (the name is <paramref name="From"/>).</param>
/// <param name="Activity">For <see cref="FriendNoteKind.Ping"/> and <see cref="FriendNoteKind.Here"/>: what the sender is doing, if they share it.</param>
/// <param name="Text">For <see cref="FriendNoteKind.Chat"/>: the message.</param>
/// <param name="MessageId">For <see cref="FriendNoteKind.Chat"/>: the message's id, so a repeat isn't shown twice. For
/// <see cref="FriendNoteKind.Reaction"/>: the message reacted to.</param>
/// <param name="Gif">For <see cref="FriendNoteKind.Chat"/>: the message is this GIF (<paramref name="Text"/> holds the link too).</param>
public sealed record FriendNote(FriendNoteKind Kind, string From, long Sent, byte[]? Secret = null, string? Code = null,
                                string? RequestId = null, bool? Accepted = null, FriendProfile? Profile = null,
                                string? Text = null, string? MessageId = null, FriendActivity? Activity = null, int? Trophies = null,
                                string? Gif = null, string? Invite = null);

/// <summary>
/// What a friend is doing, as their notch shows it to friends who ask: listening to music, in a call, playing a game,
/// on a timer or streaming. <see cref="Since"/> is a Unix time.
/// </summary>
public sealed record FriendActivity(string Kind, string Title, string? Detail, long Since)
{
    public const string Music = "music", Call = "call", Game = "game", Timer = "timer", Stream = "stream";
    static readonly string[] Kinds = { Music, Call, Game, Timer, Stream };
    const int MaxText = 80;

    /// <summary>An activity as a note brings it, checked; null for a kind this version doesn't know or no title.</summary>
    public static FriendActivity? From(string? kind, string? title, string? detail, long since)
    {
        if (kind == null || Array.IndexOf(Kinds, kind) < 0) return null;
        if (FriendProfile.CleanText(title, MaxText) is not { } cleanTitle) return null;
        return new FriendActivity(kind, cleanTitle, FriendProfile.CleanText(detail, MaxText), Math.Max(0, since));
    }
}

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
    /// <summary>"I have something for you": a Holm 1.7 answers with <see cref="Here"/> once it runs. Older ones leave it unopened.</summary>
    Waiting,
    /// <summary>"This is me now": name, status, color and picture. Holm before 1.8 leaves it unopened.</summary>
    Profile,
    /// <summary>A chat message. Holm before 1.9 leaves it unopened.</summary>
    Chat,
    /// <summary>"Watch my screen": meet me on the relay with this secret. Holm before 1.9 leaves it unopened.</summary>
    Stream,
    /// <summary>A reaction to a chat message (an emoji in <c>Text</c>, none to take it away). Holm before 1.9.2 leaves it unopened.</summary>
    Reaction,
}

public static class FriendNotes
{
    /// <summary>Notes are text messages: this prefix, the sender's public key, and the sealed note.</summary>
    public const string Prefix = "wnf1 ";
    static readonly byte[] KeyLabel = "WinNotch friends v1"u8.ToArray(); // former name, kept for friends on older versions
    const int NonceSize = 12, TagSize = 16;
    /// <summary>
    /// Letters and emoji as UTF-8, not as escapes: a status of emoji would otherwise triple in size. The JSON is sealed
    /// and only ever read back by Holm, never put in a web page, so the relaxed escaping is safe.
    /// </summary>
    static readonly JsonSerializerOptions NoteJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    sealed class NoteDto
    {
        public string? K { get; set; }
        public string? F { get; set; }
        public long T { get; set; }
        public string? S { get; set; }
        public string? C { get; set; }
        public string? R { get; set; }
        public bool? A { get; set; }
        public string? U { get; set; }
        public string? O { get; set; }
        public string? P { get; set; }
        public string? M { get; set; }
        public string? I { get; set; }
        // Activity, in "ping" and "here": kind, title, detail, since. Holm before 1.9 ignores these.
        public string? Y { get; set; }
        public string? V { get; set; }
        public string? W { get; set; }
        public long? Z { get; set; }
        /// <summary>How many achievements the sender has, in "ping" and "here".</summary>
        public int? Q { get; set; }
        /// <summary>In "chat": the text is the link to a GIF. Holm before 1.9.2 shows the link.</summary>
        public bool? G { get; set; }
        /// <summary>In "request": the proof of the invite code it was made with (FriendInvite.Proof). Older Holm ignores it.</summary>
        public string? X { get; set; }
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
                FriendNoteKind.Waiting => "waiting",
                FriendNoteKind.Profile => "profile",
                FriendNoteKind.Chat => "chat",
                FriendNoteKind.Stream => "stream",
                FriendNoteKind.Reaction => "react",
                _ => throw new ArgumentOutOfRangeException(nameof(note)),
            },
            F = note.From,
            T = note.Sent,
            S = note.Secret == null ? null : Convert.ToBase64String(note.Secret),
            C = note.Code,
            R = note.RequestId,
            A = note.Accepted,
            U = note.Profile?.Status,
            O = note.Profile?.Color,
            P = note.Profile?.Picture == null ? null : Convert.ToBase64String(note.Profile.Picture),
            // As base64 of the UTF-8: JSON would write each emoji as twelve characters.
            M = note.Text == null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(note.Text)),
            I = note.MessageId,
            Y = note.Activity?.Kind,
            V = note.Activity?.Title,
            W = note.Activity?.Detail,
            Z = note.Activity?.Since,
            Q = note.Trophies,
            G = note.Gif != null ? true : null,
            X = note.Invite,
        };
        return SealDto(me, to, dto);
    }

    static int? Trophies(int? count) => count is >= 0 and <= 10_000 ? count : null;

    /// <summary>What a profile note says, checked: a picture that isn't a small JPEG or PNG is left out.</summary>
    static FriendProfile ProfileOf(string from, NoteDto dto)
    {
        byte[]? picture = null;
        if (dto.P is { Length: <= (FriendProfile.MaxPictureBytes + 2) / 3 * 4 } p)
        {
            try { picture = Convert.FromBase64String(p); } catch (FormatException) { }
        }
        return new FriendProfile(FriendProfile.CleanText(from, FriendProfile.MaxName) ?? "",
                                 FriendProfile.CleanText(dto.U, FriendProfile.MaxStatus),
                                 FriendProfile.CleanColor(dto.O),
                                 FriendProfile.IsPicture(picture) ? picture : null);
    }

    /// <summary>Seals a note of any kind, also one this version doesn't know (to test how unknown kinds are met).</summary>
    internal static string SealKind(FriendIdentity me, Friend to, string kind, string from, long sent)
        => SealDto(me, to, new NoteDto { K = kind, F = from, T = sent });

    static string SealDto(FriendIdentity me, Friend to, NoteDto dto)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(dto, NoteJson);
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
                    => new FriendNote(FriendNoteKind.FriendRequest, from, dto.T, Code: dto.C, RequestId: dto.R,
                                      Invite: FriendInvite.IsProof(dto.X) ? dto.X : null),
                "response" when Guid.TryParseExact(dto.R, "N", out _) && dto.A.HasValue
                    => new FriendNote(FriendNoteKind.FriendResponse, from, dto.T, RequestId: dto.R, Accepted: dto.A),
                "cancel-request" when FriendIdentity.ParseFriendCode(dto.C) is { } card && card.Key == parts[0] && Guid.TryParseExact(dto.R, "N", out _)
                    => new FriendNote(FriendNoteKind.FriendRequestCancelled, from, dto.T, Code: dto.C, RequestId: dto.R),
                "removed" => new FriendNote(FriendNoteKind.RemovedYou, from, dto.T),
                "ping" => new FriendNote(FriendNoteKind.Ping, from, dto.T, Activity: FriendActivity.From(dto.Y, dto.V, dto.W, dto.Z ?? 0), Trophies: Trophies(dto.Q)),
                "here" => new FriendNote(FriendNoteKind.Here, from, dto.T, Activity: FriendActivity.From(dto.Y, dto.V, dto.W, dto.Z ?? 0), Trophies: Trophies(dto.Q)),
                "wave" => new FriendNote(FriendNoteKind.Wave, from, dto.T),
                "waiting" => new FriendNote(FriendNoteKind.Waiting, from, dto.T),
                "profile" => new FriendNote(FriendNoteKind.Profile, from, dto.T, Profile: ProfileOf(from, dto)),
                "stream" when dto.S != null && Convert.FromBase64String(dto.S) is { Length: ShareCode.SecretLength } streamSecret
                    => new FriendNote(FriendNoteKind.Stream, from, dto.T, Secret: streamSecret),
                "chat" when ChatText.Clean(ChatText.FromNote(dto.M)) is { } message && Guid.TryParseExact(dto.I, "N", out _)
                    => new FriendNote(FriendNoteKind.Chat, from, dto.T, Text: message, MessageId: dto.I,
                                      Gif: dto.G == true ? ChatGif.Clean(message) : null),
                // An emoji, or none (the reaction was taken away); anything else in it and the note is left unopened.
                "react" when Guid.TryParseExact(dto.I, "N", out _) && (dto.M == null || ChatReaction.Clean(ChatText.FromNote(dto.M)) != null)
                    => new FriendNote(FriendNoteKind.Reaction, from, dto.T, Text: dto.M == null ? null : ChatReaction.Clean(ChatText.FromNote(dto.M)), MessageId: dto.I),
                _ => null, // a kind from a newer Holm: left unopened, as older ones leave "wave"
            };
            if (note != null) senderKey = parts[0];
            return note;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException) { return null; }
    }
}

/// <summary>
/// A friend's inbox on its server (Holm's relay, or ntfy): leaving notes there, and following our own. Both speak ntfy's
/// POST and polling; our inbox is followed on the relay with a WebSocket and on ntfy with its JSON stream.
/// </summary>
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

    static bool IsRelay(string? server) => FriendIdentity.NormalizeServer(server) == FriendIdentity.RelayServer;

    /// <summary>
    /// Leaves a note in a friend's inbox. A note for the relay that it doesn't take (down, over its daily limit) goes to
    /// ntfy instead: a Holm that follows the relay follows ntfy as well. <paramref name="keep"/> false is for presence
    /// notes, worth nothing later: the server passes them on live and doesn't keep them ("Cache: no", as ntfy has it).
    /// </summary>
    /// <exception cref="IOException">The server couldn't be reached or refused the note; the message says which.</exception>
    public static async Task PostAsync(Friend to, string note, CancellationToken ct, bool keep = true)
    {
        try { await PostToAsync(to.Server, to.Topic, note, keep, ct).ConfigureAwait(false); }
        catch (IOException) when (IsRelay(to.Server) && !ct.IsCancellationRequested)
        {
            await PostToAsync(FriendIdentity.DefaultServer, to.Topic, note, keep, ct).ConfigureAwait(false);
        }
    }

    static async Task PostToAsync(string? server, string topic, string note, bool keep, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Post, InboxUrl(server, topic)) { Content = new StringContent(note, Encoding.UTF8, "text/plain") };
            if (!keep) request.Headers.Add("Cache", "no");
            response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new IOException("Couldn't reach " + Host(server), ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new IOException($"{Host(server)} refused the note ({(int)response.StatusCode})");
        }
    }

    /// <summary>
    /// Leaves a sealed invite card at an invite code's inbox, where it waits 12 hours: on the relay, and on ntfy for
    /// friends whose Holm is older than the relay. Either one is enough.
    /// </summary>
    /// <exception cref="IOException">Neither server took it.</exception>
    public static async Task PublishInviteAsync(string topic, string card, CancellationToken ct)
    {
        var relay = PostToAsync(FriendIdentity.RelayServer, topic, card, true, ct);
        var legacy = PostToAsync(FriendIdentity.DefaultServer, topic, card, true, ct);
        await EitherAsync(relay, legacy).ConfigureAwait(false);
    }

    /// <summary>The invite cards waiting at an invite code's inbox on the relay and on ntfy, newest first.</summary>
    /// <exception cref="IOException">Neither server could be reached.</exception>
    public static async Task<List<string>> FetchInviteAsync(string topic, CancellationToken ct)
    {
        var relay = FetchInviteFromAsync(FriendIdentity.RelayServer, topic, ct);
        var legacy = FetchInviteFromAsync(FriendIdentity.DefaultServer, topic, ct);
        await EitherAsync(relay, legacy).ConfigureAwait(false);
        var cards = new List<string>();
        if (legacy.IsCompletedSuccessfully) cards.AddRange(legacy.Result);
        if (relay.IsCompletedSuccessfully) cards.AddRange(relay.Result);
        cards.Reverse();
        return cards;
    }

    /// <summary>Waits for both; fine if one of them worked, else the relay's error.</summary>
    static async Task EitherAsync(Task relay, Task legacy)
    {
        try { await Task.WhenAll(relay, legacy).ConfigureAwait(false); }
        catch (IOException) when (relay.IsCompletedSuccessfully || legacy.IsCompletedSuccessfully) { }
        catch (IOException) when (relay.Exception?.InnerException is IOException why) { throw why; }
    }

    /// <summary>The invite cards at one server, oldest first.</summary>
    static async Task<List<string>> FetchInviteFromAsync(string server, string topic, CancellationToken ct)
    {
        var cards = new List<string>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            string url = InboxUrl(server, topic) + "/json?poll=1&since=12h";
            using var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new IOException($"{Host(server)} answered {(int)response.StatusCode}");
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            foreach (string line in body.Split('\n'))
                if (TryParse(line, out bool isMessage, out _, out _, out string? text) && isMessage && text != null
                    && text.StartsWith(FriendInvite.Prefix, StringComparison.Ordinal))
                    cards.Add(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new IOException("Couldn't reach " + Host(server), ex);
        }
        return cards;
    }

    /// <summary>
    /// Follows our inbox on an ntfy server until <paramref name="ct"/> fires, reconnecting as needed. <paramref name="received"/>
    /// gets each note's id, its age in seconds by the server's clock (so a PC clock that is off doesn't matter) and its text.
    /// <paramref name="since"/> is a Unix time to catch up from (null: only new notes); <paramref name="handled"/> reports
    /// the time of each message, so a restart can pick up what came meanwhile (ntfy keeps messages for 12 hours).
    /// Catching up by time can repeat the last message, so the caller skips ids it has already seen. (By id it would be
    /// exact, but for an id that has expired ntfy replays everything it still has.)
    /// </summary>
    public static async Task FollowAsync(string? server, string topic, long? since, Func<string, long, string, Task> received, Action<long> handled,
                                         Action<string>? log, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string url = InboxUrl(server, topic) + "/json" + (since != null ? "?since=" + since : "");
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
                log?.Invoke("Friends inbox connected: " + Host(server));
                delay = TimeSpan.FromSeconds(2);
                var events = new InboxEvents(received, handled);
                while (true)
                {
                    // ntfy sends a keepalive every 45 s; silence means the connection died (sleep, a new Wi-Fi).
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromSeconds(100));
                    string? line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                    if (line == null) break;
                    if (await events.LineAsync(line).ConfigureAwait(false) is > 0 and long time) since = time; // keepalives move the catch-up point on too
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log?.Invoke($"Friends inbox ({Host(server)}): {ex.Message}"); }

            try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(2).Ticks));
        }
    }

    /// <summary>
    /// Follows our inbox on the relay with a WebSocket, like <see cref="FollowAsync"/>: the relay sends an "open" event,
    /// the notes kept since <paramref name="since"/> (it keeps a week's), then each new one. Between notes the line is
    /// quiet and costs the relay nothing; WebSocket pings, which the relay answers without waking up, notice a dead
    /// connection. <paramref name="connected"/> runs each time the relay let us in.
    /// </summary>
    public static async Task FollowRelayAsync(string topic, long? since, Func<string, long, string, Task> received, Action<long> handled,
                                              Action? connected, Action<string>? log, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30); // a ping without a pong ends the connection
                ws.Options.SetRequestHeader("User-Agent", "Holm-Friends/1");
                string url = Regex.Replace(InboxUrl(FriendIdentity.RelayServer, topic), "^http", "ws") + "/ws" + (since != null ? "?since=" + since : "");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    await ws.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
                }
                log?.Invoke("Friends inbox connected: " + Host(FriendIdentity.RelayServer));
                delay = TimeSpan.FromSeconds(2);
                connected?.Invoke();
                var events = new InboxEvents(received, handled);
                using var message = new MemoryStream();
                while (true)
                {
                    var part = await ws.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 64 * 1024) throw new IOException("A message too large for a note");
                    if (!part.EndOfMessage) continue;
                    string line = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    // The relay sends no keepalives: notes move the catch-up point, the open event only when there was none.
                    if (await events.LineAsync(line).ConfigureAwait(false) is > 0 and long time && (events.LastWasMessage || since == null)) since = time;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log?.Invoke($"Friends inbox ({Host(FriendIdentity.RelayServer)}): {ex.Message}"); }

            try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(2).Ticks));
        }
    }

    /// <summary>The events of one connection to our inbox, ntfy's or the relay's: both come as the same JSON lines.</summary>
    sealed class InboxEvents(Func<string, long, string, Task> received, Action<long> handled)
    {
        long _serverNow; // a connection opens with the server's current time, before any catching up
        public bool LastWasMessage { get; private set; }

        /// <summary>Handles one line; its time, or null for a line that isn't an event.</summary>
        public async Task<long?> LineAsync(string line)
        {
            LastWasMessage = false;
            if (line.Length == 0 || !TryParse(line, out bool isMessage, out long time, out string? id, out string? text)) return null;
            _serverNow = Math.Max(_serverNow, time);
            if (!isMessage || id == null) return time;
            LastWasMessage = true;
            if (text != null && text.StartsWith(FriendNotes.Prefix, StringComparison.Ordinal))
                await received(id, Math.Max(0, _serverNow - time), text).ConfigureAwait(false);
            handled(time);
            return time;
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
