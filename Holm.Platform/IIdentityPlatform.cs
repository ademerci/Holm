using System;
using System.IO;
using System.Security.Cryptography;

namespace Holm.Platform;

/// <summary>Keeping a secret (the friends identity's private key) for this user only, and the user's name.</summary>
public interface IIdentityPlatform
{
    byte[] Protect(byte[] data, byte[] entropy);
    byte[] Unprotect(byte[] data, byte[] entropy);

    /// <summary>The account's full name ("Lena Hoffmann"), or its login name.</summary>
    string AccountName();
}

/// <summary>
/// Linux: sealed with AES-GCM under a key of this user's own, in a file only they can read (as ssh and
/// gpg keep theirs). Copying the settings to another computer leaves the secret behind, as with Windows' DPAPI.
/// </summary>
public class UnixIdentityPlatform : IIdentityPlatform
{
    readonly string _keyFile;

    public UnixIdentityPlatform(string folder) => _keyFile = Path.Combine(folder, "secret.key");

    public byte[] Protect(byte[] data, byte[] entropy)
    {
        byte[] key = Key(create: true);
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], sealedData = new byte[data.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, data, sealedData, tag, entropy);
        var result = new byte[1 + 12 + 16 + data.Length];
        result[0] = 1; // version
        nonce.CopyTo(result, 1);
        tag.CopyTo(result, 13);
        sealedData.CopyTo(result, 29);
        return result;
    }

    public byte[] Unprotect(byte[] data, byte[] entropy)
    {
        if (data.Length < 29 || data[0] != 1) throw new CryptographicException("Not sealed here.");
        byte[] key = Key(create: false);
        var plain = new byte[data.Length - 29];
        using (var aes = new AesGcm(key, 16)) aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), plain, entropy);
        return plain;
    }

    byte[] Key(bool create)
    {
        if (File.Exists(_keyFile)) return File.ReadAllBytes(_keyFile);
        if (!create) throw new CryptographicException("No key on this computer.");
        Directory.CreateDirectory(Path.GetDirectoryName(_keyFile)!);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        // Created for the user alone before anything is written into it.
        using (var file = new FileStream(_keyFile, new FileStreamOptions
               {
                   Mode = FileMode.CreateNew, Access = FileAccess.Write,
                   UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
               }))
            file.Write(key);
        return key;
    }

    public virtual string AccountName()
    {
        try
        {
            // The full name from the account's entry (GECOS: "Lena Hoffmann,,,").
            foreach (string line in File.ReadLines("/etc/passwd"))
            {
                var fields = line.Split(':');
                if (fields.Length > 4 && fields[0] == Environment.UserName)
                {
                    string full = fields[4].Split(',')[0].Trim();
                    if (full.Length > 0) return full;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Environment.UserName;
    }
}
