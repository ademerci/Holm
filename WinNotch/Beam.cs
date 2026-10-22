using System;
using System.Buffers.Binary;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch;

// Beam: send files to friends by dropping them on the notch.
//
// The files travel through an ntfy server (https://ntfy.sh unless you run your own), so it works on any
// network that reaches the internet, including guest and school Wi-Fi where devices can't see each other.
// Everything is end-to-end encrypted: the server only ever stores an opaque blob for a few hours.
//
// Each person has a topic (their inbox on the server) and a P-256 key pair. A friend code carries the
// name, topic and public key. Sender and recipient derive the same AES-256-GCM key from their own private
// key and the other's public key, so a file only opens for the recipient, and only if it really came from
// a friend: nobody else can produce a ciphertext that decrypts with that key.
//
// This file has no Windows dependencies, so it can be tested on its own.

/// <summary>Someone you can beam files to, as their friend code describes them.</summary>
internal sealed class BeamFriend
{
    public string Name { get; set; } = "";
    /// <summary>Their inbox on the server.</summary>
    public string Topic { get; set; } = "";
    /// <summary>Their public key: P-256 SubjectPublicKeyInfo, base64url.</summary>
    public string Key { get; set; } = "";
    /// <summary>Their server; null for the default one.</summary>
    public string? Server { get; set; }
}

internal sealed class BeamIdentity : IDisposable
{
    public const string DefaultServer = "https://ntfy.sh";
    const string CodePrefix = "WNB1.";
    static readonly Regex CodeInText = new(@"WNB1\.[A-Za-z0-9_-]{40,}");

    public string Name { get; }
    public string Topic { get; }
    /// <summary>Server URL without a trailing slash.</summary>
    public string Server { get; }
    public ECDiffieHellman Key { get; }
    public string PublicKey { get; }

    public BeamIdentity(string name, string topic, string? server, ECDiffieHellman key)
    {
        Name = name;
        Topic = topic;
        Server = NormalizeServer(server);
        Key = key;
        PublicKey = Base64Url(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>A new inbox (130 random bits, so nobody can guess it) and a new key pair.</summary>
    public static BeamIdentity Create(string name, string? server)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var topic = new StringBuilder("wnb-");
        foreach (byte b in RandomNumberGenerator.GetBytes(26)) topic.Append(alphabet[b & 31]);
        return new BeamIdentity(name, topic.ToString(), server, ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));
    }

    public static string NormalizeServer(string? server)
        => string.IsNullOrWhiteSpace(server) ? DefaultServer : server.Trim().TrimEnd('/');

    /// <summary>What you send to a friend so they can add you: "WNB1." + base64url(JSON).</summary>
    public string FriendCode
    {
        get
        {
            var card = new CodeCard { N = Name, T = Topic, K = PublicKey, S = Server == DefaultServer ? null : Server };
            return CodePrefix + Base64Url(JsonSerializer.SerializeToUtf8Bytes(card, CodeJson));
        }
    }

    /// <summary>The friend code in a piece of text (as pasted from a chat, say), or null.</summary>
    public static string? FindFriendCode(string? text) => text != null && CodeInText.Match(text) is { Success: true } match ? match.Value : null;

    /// <summary>Reads the friend code anywhere in the text. Null if there is none or it's damaged.</summary>
    public static BeamFriend? ParseFriendCode(string? text)
    {
        if (FindFriendCode(text) is not { } code) return null;
        try
        {
            var card = JsonSerializer.Deserialize<CodeCard>(FromBase64Url(code[CodePrefix.Length..]), CodeJson);
            if (card == null || string.IsNullOrWhiteSpace(card.T) || string.IsNullOrWhiteSpace(card.K)) return null;
            if (!Regex.IsMatch(card.T, "^[A-Za-z0-9_-]{1,64}$")) return null;
            using (var check = ECDiffieHellman.Create()) check.ImportSubjectPublicKeyInfo(FromBase64Url(card.K), out _);
            string? server = card.S == null ? null : NormalizeServer(card.S);
            if (server != null && !(Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")) return null;
            string name = string.IsNullOrWhiteSpace(card.N) ? "Friend" : card.N.Trim();
            return new BeamFriend { Name = name.Length > 40 ? name[..40] : name, Topic = card.T, Key = card.K, Server = server };
        }
        catch { return null; }
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

/// <summary>What travels inside the encryption next to the file.</summary>
internal sealed class BeamHeader
{
    public string Name { get; set; } = "";
    /// <summary>Who sent it, as they call themselves.</summary>
    public string From { get; set; } = "";
    /// <summary>Unix seconds.</summary>
    public long Sent { get; set; }
}

internal static class BeamCrypto
{
    static readonly byte[] Magic = "WNB1"u8.ToArray();
    static readonly byte[] KeyLabel = "WinNotch Beam v1"u8.ToArray();
    const int NonceSize = 12, TagSize = 16;

    /// <summary>The key both sides of a friendship derive: SHA-256 over a label and the ECDH secret.</summary>
    static byte[] SharedKey(ECDiffieHellman mine, string theirPublicKey)
    {
        using var theirs = ECDiffieHellman.Create();
        theirs.ImportSubjectPublicKeyInfo(BeamIdentity.FromBase64Url(theirPublicKey), out _);
        return mine.DeriveKeyFromHash(theirs.PublicKey, HashAlgorithmName.SHA256, KeyLabel, null);
    }

    /// <summary>
    /// "WNB1", nonce, tag, then AES-256-GCM over [header length][header JSON][file]. The recipient's topic is
    /// authenticated too, so a blob can't be replayed into someone else's inbox.
    /// </summary>
    public static byte[] Seal(BeamIdentity me, BeamFriend to, BeamHeader header, ReadOnlySpan<byte> file)
    {
        byte[] headerJson = JsonSerializer.SerializeToUtf8Bytes(header);
        var plain = new byte[4 + headerJson.Length + file.Length];
        BinaryPrimitives.WriteInt32LittleEndian(plain, headerJson.Length);
        headerJson.CopyTo(plain, 4);
        file.CopyTo(plain.AsSpan(4 + headerJson.Length));

        var body = new byte[Magic.Length + NonceSize + TagSize + plain.Length];
        var nonce = body.AsSpan(Magic.Length, NonceSize);
        var tag = body.AsSpan(Magic.Length + NonceSize, TagSize);
        Magic.CopyTo(body, 0);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(SharedKey(me.Key, to.Key), TagSize))
            aes.Encrypt(nonce, plain, body.AsSpan(Magic.Length + NonceSize + TagSize), tag, Encoding.UTF8.GetBytes(to.Topic));
        CryptographicOperations.ZeroMemory(plain);
        return body;
    }

    /// <summary>Decrypts a blob from <paramref name="senderKey"/>. Null if it wasn't sealed by that sender for us.</summary>
    public static (BeamHeader Header, byte[] File)? Open(BeamIdentity me, string senderKey, byte[] body)
    {
        int start = Magic.Length + NonceSize + TagSize;
        if (body.Length < start + 4 || !body.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;
        var plain = new byte[body.Length - start];
        try
        {
            using var aes = new AesGcm(SharedKey(me.Key, senderKey), TagSize);
            aes.Decrypt(body.AsSpan(Magic.Length, NonceSize), body.AsSpan(start), body.AsSpan(Magic.Length + NonceSize, TagSize),
                        plain, Encoding.UTF8.GetBytes(me.Topic));
        }
        catch (CryptographicException) { return null; }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(plain);
        if (headerLength < 2 || headerLength > plain.Length - 4) return null;
        var header = JsonSerializer.Deserialize<BeamHeader>(plain.AsSpan(4, headerLength));
        if (header == null) return null;
        return (header, plain[(4 + headerLength)..]);
    }
}

internal enum BeamFailure { TooLarge, Rejected, Unreachable }

internal sealed class BeamException : Exception
{
    public BeamFailure Failure { get; }
    public BeamException(BeamFailure failure, string message, Exception? inner = null) : base(message, inner) => Failure = failure;
}

/// <summary>A beam waiting in our inbox on the server. <see cref="Time"/> is the server's Unix time.</summary>
internal sealed record BeamIncoming(string Id, long Time, string SenderKey, string Url, long Size);

/// <summary>Talks to the ntfy server: uploading to a friend's inbox and following our own.</summary>
internal static class BeamClient
{
    /// <summary>Upper bound for one file, whatever the server allows; files are held in memory.</summary>
    public const long MaxBytes = 100L * 1024 * 1024;
    const string MessagePrefix = "wnb1 ";

    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan, // the inbox is one long-lived request; uploads have their own token
        DefaultRequestHeaders = { { "User-Agent", "WinNotch-Beam/1" } },
    };

    static string InboxUrl(string server, string topic) => $"{BeamIdentity.NormalizeServer(server)}/{Uri.EscapeDataString(topic)}";

    public static async Task SendAsync(BeamIdentity me, BeamFriend to, string fileName, byte[] file, CancellationToken ct)
    {
        var header = new BeamHeader { Name = fileName, From = me.Name, Sent = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        byte[] body = BeamCrypto.Seal(me, to, header, file);
        using var request = new HttpRequestMessage(HttpMethod.Put, InboxUrl(to.Server ?? BeamIdentity.DefaultServer, to.Topic))
        {
            Content = new ByteArrayContent(body),
        };
        // The real file name is inside the encrypted part; the server only sees this.
        request.Headers.Add("Filename", "beam.bin");
        request.Headers.Add("Message", MessagePrefix + me.PublicKey);
        HttpResponseMessage response;
        try { response = await Http.SendAsync(request, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new BeamException(BeamFailure.Unreachable, "Couldn't reach " + Host(to.Server), ex);
        }
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            string detail = await ErrorOf(response);
            throw response.StatusCode == HttpStatusCode.RequestEntityTooLarge
                ? new BeamException(BeamFailure.TooLarge, detail)
                : new BeamException(BeamFailure.Rejected, $"{(int)response.StatusCode}: {detail}");
        }
    }

    static string Host(string? server) => Uri.TryCreate(BeamIdentity.NormalizeServer(server), UriKind.Absolute, out var uri) ? uri.Host : "the server";

    static async Task<string> ErrorOf(HttpResponseMessage response)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? response.ReasonPhrase ?? "";
        }
        catch { }
        return response.ReasonPhrase ?? "";
    }

    /// <summary>The server's per-file limit for us (ntfy.sh: 2 MB without an account), or null if it doesn't say.</summary>
    public static async Task<long?> FileLimitAsync(string? server, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(BeamIdentity.NormalizeServer(server) + "/v1/account", ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("limits", out var limits) && limits.TryGetProperty("attachment_file_size", out var size)
                   && size.TryGetInt64(out long bytes) && bytes > 0 ? bytes : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return null; }
    }

    /// <summary>
    /// Follows our inbox until <paramref name="ct"/> fires, reconnecting as needed. <paramref name="since"/> is a Unix
    /// time to catch up from (null: only new messages); <paramref name="handled"/> reports the time of each message, so
    /// a restart can pick up what arrived meanwhile (the server keeps messages for 12 hours, files for 3).
    /// Catching up by time can repeat the last message, so callers skip ids they have already seen. (Catching up by
    /// id would be exact, but for an id that has expired ntfy replays everything it still has.)
    /// </summary>
    public static async Task FollowAsync(BeamIdentity me, long? since, Func<BeamIncoming, Task> received, Action<long> handled,
                                         Action<string>? log, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string url = InboxUrl(me.Server, me.Topic) + "/json" + (since != null ? "?since=" + since : "");
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                log?.Invoke("Beam inbox connected");
                delay = TimeSpan.FromSeconds(2);
                while (true)
                {
                    // ntfy sends a keepalive every 45 s; silence means the connection died (sleep, Wi-Fi change).
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromSeconds(100));
                    string? line = await reader.ReadLineAsync(idle.Token);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    var incoming = Parse(line, out bool isMessage, out long time);
                    // Keepalives move the catch-up point on too, so a dropped connection doesn't lose what came meanwhile.
                    if (time > 0) since = time;
                    if (!isMessage) continue;
                    if (incoming != null) await received(incoming);
                    handled(time);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log?.Invoke("Beam inbox: " + ex.Message); }

            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(2).Ticks));
        }
    }

    /// <summary>
    /// One line of the JSON stream: a beam if it is one. <paramref name="isMessage"/> is set for every message, beam
    /// or not, and <paramref name="time"/> for every event (0 if missing).
    /// </summary>
    internal static BeamIncoming? Parse(string line, out bool isMessage, out long time)
    {
        isMessage = false;
        time = 0;
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("time", out var timeElement)) timeElement.TryGetInt64(out time);
            isMessage = root.TryGetProperty("event", out var ev) && ev.GetString() == "message";
            if (!isMessage || !root.TryGetProperty("id", out var idElement) || idElement.GetString() is not { } id) return null;
            if (!root.TryGetProperty("message", out var message) || message.GetString() is not { } text || !text.StartsWith(MessagePrefix, StringComparison.Ordinal))
                return null;
            if (!root.TryGetProperty("attachment", out var attachment) || !attachment.TryGetProperty("url", out var urlElement)
                || urlElement.GetString() is not { } fileUrl) return null;
            long size = attachment.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out long s) ? s : -1;
            return new BeamIncoming(id, time, text[MessagePrefix.Length..].Trim(), fileUrl, size);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Fetches an encrypted file, but only from our own server and up to <see cref="MaxBytes"/>.</summary>
    public static async Task<byte[]> DownloadAsync(BeamIdentity me, BeamIncoming incoming, CancellationToken ct)
    {
        if (!Uri.TryCreate(incoming.Url, UriKind.Absolute, out var url) || !Uri.TryCreate(me.Server, UriKind.Absolute, out var server)
            || !string.Equals(url.Host, server.Host, StringComparison.OrdinalIgnoreCase) || url.Scheme != server.Scheme)
            throw new BeamException(BeamFailure.Rejected, "File isn't on our server");
        if (incoming.Size > MaxBytes) throw new BeamException(BeamFailure.TooLarge, "File too large");

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (copy.Length + read > MaxBytes) throw new BeamException(BeamFailure.TooLarge, "File too large");
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}

internal static class BeamFiles
{
    static readonly string[] ReservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                                               "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    /// <summary>
    /// The sender picks the name, so it may not reach outside Downloads, replace anything there, or hide its
    /// real extension behind right-to-left marks ("photo\u202Egpj.exe" shows as "photoexe.jpg").
    /// </summary>
    public static string SafeName(string name)
    {
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'))).ToArray());
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }))
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "beam";
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) stem = "_" + stem;
        if (extension.Length > 16) extension = "";
        if (stem.Length > 100) stem = stem[..100];
        return stem + extension;
    }
}
