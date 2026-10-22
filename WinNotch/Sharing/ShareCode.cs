using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNotch.Sharing;

/// <summary>
/// The code that pairs a sender and a receiver over the internet, e.g. <c>WN-K7Q2A-…</c>.
/// It carries 128 random bits; the encryption key and the relay channel are both derived from it,
/// so whoever doesn't have the code can neither find the transfer nor read it.
/// </summary>
public static class ShareCode
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // RFC 4648 base32
    public const int SecretLength = 16;

    static readonly Regex Pattern = new(
        @"(?<![A-Z0-9])WN-([A-Z2-7]{5})-([A-Z2-7]{5})-([A-Z2-7]{5})-([A-Z2-7]{5})-([A-Z2-7]{6})(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretLength);

    /// <summary>"WN-" and 26 base32 characters in groups of 5, 5, 5, 5 and 6.</summary>
    public static string Format(byte[] secret)
    {
        if (secret.Length != SecretLength) throw new ArgumentException("A share secret is 16 bytes.", nameof(secret));
        var chars = new StringBuilder(26);
        int buffer = 0, bits = 0;
        foreach (byte b in secret)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) chars.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        string s = chars.ToString();
        return $"WN-{s[..5]}-{s[5..10]}-{s[10..15]}-{s[15..20]}-{s[20..]}";
    }

    /// <summary>Finds a code anywhere in some text (e.g. a chat message that was copied).</summary>
    public static bool TryFind(string? text, out byte[] secret, out string code)
    {
        secret = Array.Empty<byte>();
        code = "";
        if (string.IsNullOrEmpty(text) || text.Length > 10_000) return false;
        var match = Pattern.Match(text);
        if (!match.Success) return false;

        string chars = string.Concat(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value,
                                     match.Groups[4].Value, match.Groups[5].Value).ToUpperInvariant();
        var bytes = new byte[SecretLength];
        int buffer = 0, bits = 0, index = 0;
        foreach (char c in chars)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                if (index == SecretLength) break;
                bytes[index++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
        }
        if (index != SecretLength) return false;
        secret = bytes;
        code = Format(bytes);
        return true;
    }

    /// <summary>The AES-256 key both sides encrypt with.</summary>
    public static byte[] Key(byte[] secret) => Derive(secret, "WinNotch transfer key v1");

    /// <summary>The channel name on the relay. Different from the key, so the relay learns nothing about it.</summary>
    public static byte[] RelayToken(byte[] secret) => Derive(secret, "WinNotch relay token v1");

    static byte[] Derive(byte[] secret, string purpose)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt: null, info: Encoding.ASCII.GetBytes(purpose));
}
