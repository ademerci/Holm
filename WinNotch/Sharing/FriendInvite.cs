using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinNotch.Sharing;

// Adding a friend with a short code. Instead of a long friend code to copy, you get an invite code like
// HOLM-7KQ2X-9XF4M to tell a friend, type into a chat or read out. Everything comes from the code: the inbox on the
// server where your friend card waits (sealed with a key from the code, so only someone with the code can read it)
// and a proof your friend's request carries, so your notch knows they had your code and adds them without asking.
// The card is kept by the server for 12 hours; a code works for anyone you give it to until then.

/// <summary>Short invite codes: their format, what they lead to, and the sealed card behind them.</summary>
public static class FriendInvite
{
    /// <summary>Crockford's base32: no I, L, O or U, so a code read out or typed by hand is hard to get wrong.</summary>
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    /// <summary>Ten characters: 50 random bits.</summary>
    public const int Length = 10;
    /// <summary>How long a code works: as long as the server keeps the card.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    public const string Prefix = "hinv1 ";

    static readonly Regex InText = new(@"(?<![A-Za-z0-9])HOLM[-\s]?([0-9A-Za-z]{5})[-\s]?([0-9A-Za-z]{5})(?![A-Za-z0-9])",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly byte[] Salt = Encoding.ASCII.GetBytes("Holm friend invite v1");

    /// <summary>A new code: ten random characters.</summary>
    public static string New()
    {
        var chars = new char[Length];
        byte[] random = RandomNumberGenerator.GetBytes(Length);
        for (int i = 0; i < Length; i++) chars[i] = Alphabet[random[i] & 31];
        return Format(new string(chars));
    }

    /// <summary>"HOLM-" and the ten characters in two groups of five.</summary>
    public static string Format(string chars) => $"HOLM-{chars[..5]}-{chars[5..]}";

    /// <summary>
    /// The code in what someone typed or pasted: "HOLM-7KQ2X-9XF4M" anywhere in a message, or just the ten characters
    /// with or without a dash, in any case. Letters people mix up are read as what they look like (O as 0, I and L as 1).
    /// Null if there's no valid code.
    /// </summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10_000) return null;
        var match = InText.Match(text);
        string raw;
        if (match.Success) raw = match.Groups[1].Value + match.Groups[2].Value;
        else
        {
            // Typed into the Add friend box: just the characters.
            raw = text.Trim().Replace("-", "").Replace(" ", "");
            if (raw.StartsWith("HOLM", StringComparison.OrdinalIgnoreCase)) raw = raw[4..];
            if (raw.Length != Length) return null;
        }
        var chars = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            char c = char.ToUpperInvariant(raw[i]) switch { 'O' => '0', 'I' or 'L' => '1', var x => x };
            if (Alphabet.IndexOf(c) < 0) return null;
            chars[i] = c;
        }
        return Format(new string(chars));
    }

    /// <summary>A code with its "HOLM-" prefix in copied text, as from a chat; plain words never count.</summary>
    public static string? FindInMessage(string? text)
        => text != null && text.Length <= 10_000 && InText.IsMatch(text) ? Find(text) : null;

    static byte[] Derive(string code, string purpose, int length)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.ASCII.GetBytes(code), length, Salt, Encoding.ASCII.GetBytes(purpose));

    /// <summary>The server inbox where the card waits: unguessable without the code.</summary>
    public static string Topic(string code)
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz234567";
        var topic = new StringBuilder("holm-inv-");
        foreach (byte b in Derive(code, "topic", 20)) topic.Append(lower[b & 31]);
        return topic.ToString();
    }

    /// <summary>What a request made with this code carries, so the inviter knows it came from someone with the code.</summary>
    public static string Proof(string code) => Convert.ToHexString(Derive(code, "proof", 16)).ToLowerInvariant();

    public static bool IsProof(string? text) => text is { Length: 32 } && Regex.IsMatch(text, "^[0-9a-f]{32}$");

    /// <summary>The inviter's friend code, sealed with the invite code's key and good until <paramref name="expires"/> (Unix time).</summary>
    public static string Seal(string code, string friendCode, long expires)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new Card { C = friendCode, E = expires });
        var sealedCard = new byte[12 + 16 + plain.Length];
        RandomNumberGenerator.Fill(sealedCard.AsSpan(0, 12));
        using (var aes = new AesGcm(Derive(code, "key", 32), 16))
            aes.Encrypt(sealedCard.AsSpan(0, 12), plain, sealedCard.AsSpan(28), sealedCard.AsSpan(12, 16), Encoding.ASCII.GetBytes(Topic(code)));
        return Prefix + FriendIdentity.Base64Url(sealedCard);
    }

    /// <summary>The friend behind an invite, if <paramref name="text"/> is a card sealed for this code that hasn't expired.</summary>
    public static Friend? Open(string code, string? text, long now)
    {
        if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length > 4096) return null;
        try
        {
            byte[] sealedCard = FriendIdentity.FromBase64Url(text[Prefix.Length..]);
            if (sealedCard.Length < 12 + 16 + 2) return null;
            var plain = new byte[sealedCard.Length - 28];
            using (var aes = new AesGcm(Derive(code, "key", 32), 16))
                aes.Decrypt(sealedCard.AsSpan(0, 12), sealedCard.AsSpan(28), sealedCard.AsSpan(12, 16), plain, Encoding.ASCII.GetBytes(Topic(code)));
            var card = JsonSerializer.Deserialize<Card>(plain);
            if (card == null || card.E < now) return null;
            return FriendIdentity.ParseFriendCode(card.C);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException) { return null; }
    }

    sealed class Card
    {
        public string? C { get; set; }
        public long E { get; set; }
    }
}

/// <summary>An invite code of ours that still works; requests that carry its proof are accepted without asking.</summary>
public sealed class OwnInvite
{
    public string Code { get; set; } = "";
    public string Proof { get; set; } = "";
    /// <summary>Unix time.</summary>
    public long Expires { get; set; }
}

/// <summary>A friend request we sent that hasn't been answered yet. Kept, so a late yes still makes you friends.</summary>
public sealed class PendingFriendRequest
{
    public string RequestId { get; set; } = "";
    public Friend Card { get; set; } = new();
    /// <summary>Unix time.</summary>
    public long Sent { get; set; }
    /// <summary>Made with their invite code: they'll accept without being asked.</summary>
    public bool ByInvite { get; set; }
}
