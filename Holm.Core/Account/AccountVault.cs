using System;
using System.Security.Cryptography;
using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Account;

// End-to-end encryption for the Holm account.
//
// The password never leaves this computer. From it and the email address Holm derives a master key (PBKDF2-SHA256,
// 600 000 rounds), and from that two keys that have nothing to do with each other (HKDF):
//   - the sign-in key, which Firebase gets in place of the password, so Firebase can check it but learns nothing that
//     opens the data;
//   - the vault key, which never leaves this computer and opens the account's data key.
// The data key is random, made once per account and kept in Firebase sealed with the vault key (AES-256-GCM). Every
// piece of synced data is sealed with the data key, bound to the account and the piece's name, so it can't be swapped
// between accounts or sections. Whoever runs the Firebase project sees only sealed bytes.
//
// The flip side: a forgotten password can't be reset. The data in the account is then lost; the data on each PC stays.

/// <summary>The keys made from an account's email and password.</summary>
public sealed class AccountKeys
{
    const int Rounds = 600_000;
    const string Salt = "holm-account-v1:";

    /// <summary>What Firebase gets as the password: base64url of 32 derived bytes.</summary>
    public string SignInSecret { get; }
    /// <summary>Opens the account's data key; never sent anywhere.</summary>
    internal byte[] VaultKey { get; }

    AccountKeys(string signInSecret, byte[] vaultKey)
    {
        SignInSecret = signInSecret;
        VaultKey = vaultKey;
    }

    public static string NormalizeEmail(string email) => email.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();

    /// <summary>Slow on purpose (about half a second): run it off the UI thread.</summary>
    public static AccountKeys Derive(string email, string password)
    {
        // NFC: "ä" typed on one system may arrive as a + combining dots on another; both must make the same keys.
        byte[] master = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)),
                                                  Encoding.UTF8.GetBytes(Salt + NormalizeEmail(email)),
                                                  Rounds, HashAlgorithmName.SHA256, 32);
        try
        {
            byte[] signIn = HKDF.Expand(HashAlgorithmName.SHA256, master, 32, "holm sign-in"u8.ToArray());
            byte[] vault = HKDF.Expand(HashAlgorithmName.SHA256, master, 32, "holm vault"u8.ToArray());
            return new AccountKeys(FriendIdentity.Base64Url(signIn), vault);
        }
        finally { CryptographicOperations.ZeroMemory(master); }
    }
}

/// <summary>Sealing and opening the account's data with AES-256-GCM. Sealed text is "v1." and base64url(nonce|tag|data).</summary>
public static class AccountVault
{
    const string Prefix = "v1.";
    const int NonceSize = 12, TagSize = 16;

    public static byte[] NewDataKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>The data key, sealed with the vault key for this account (<paramref name="uid"/>).</summary>
    public static string WrapDataKey(AccountKeys keys, string uid, byte[] dataKey) => Seal(keys.VaultKey, dataKey, "vault:" + uid);

    /// <exception cref="CryptographicException">The password is wrong, or the sealed key belongs to another account.</exception>
    public static byte[] UnwrapDataKey(AccountKeys keys, string uid, string wrapped) => Open(keys.VaultKey, wrapped, "vault:" + uid);

    /// <summary>One section of the account (settings, friends, …), sealed for this account and this section only.</summary>
    public static string SealSection(byte[] dataKey, string uid, string section, byte[] plain) => Seal(dataKey, plain, $"data:{uid}/{section}");

    /// <exception cref="CryptographicException">The data was changed, or belongs to another account or section.</exception>
    public static byte[] OpenSection(byte[] dataKey, string uid, string section, string sealedText) => Open(dataKey, sealedText, $"data:{uid}/{section}");

    static string Seal(byte[] key, byte[] plain, string context)
    {
        var sealedBytes = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(sealedBytes.AsSpan(0, NonceSize));
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(sealedBytes.AsSpan(0, NonceSize), plain, sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize),
                        Encoding.UTF8.GetBytes(context));
        return Prefix + FriendIdentity.Base64Url(sealedBytes);
    }

    static byte[] Open(byte[] key, string sealedText, string context)
    {
        if (!sealedText.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown format.");
        byte[] sealedBytes;
        try { sealedBytes = FriendIdentity.FromBase64Url(sealedText[Prefix.Length..]); }
        catch (FormatException ex) { throw new CryptographicException("Damaged.", ex); }
        if (sealedBytes.Length < NonceSize + TagSize) throw new CryptographicException("Too short.");
        var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize), plain,
                        Encoding.UTF8.GetBytes(context));
        return plain;
    }
}
