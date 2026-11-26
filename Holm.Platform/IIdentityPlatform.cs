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
    static readonly object KeyGate = new();
    static readonly UnixFileMode PrivateDirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    static readonly UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    static readonly UnixFileMode GroupOrOtherBits =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

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
        lock (KeyGate)
        {
            if (File.Exists(_keyFile)) return ReadVerifiedKey();
            if (!create) throw new CryptographicException("No key on this computer.");

            string dir = Path.GetDirectoryName(_keyFile)!;
            EnsurePrivateDirectory(dir);

            byte[] key = RandomNumberGenerator.GetBytes(32);
            string temp = Path.Combine(dir, ".secret-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() + ".tmp");
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = PrivateFileMode;
                // Created for the user alone before anything is written into it, then atomically renamed.
                using (var file = new FileStream(temp, options))
                {
                    file.Write(key);
                    file.Flush(flushToDisk: true);
                }
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(temp, PrivateFileMode); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
                }
                try
                {
                    File.Move(temp, _keyFile, overwrite: false);
                    return key;
                }
                catch (IOException) when (File.Exists(_keyFile))
                {
                    // Another process created the key first; discard ours and use the installed key.
                    try { File.Delete(temp); } catch { }
                    return ReadVerifiedKey();
                }
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
        }
    }

    byte[] ReadVerifiedKey()
    {
        var info = new FileInfo(_keyFile);
        if (info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new CryptographicException("Key file must not be a symlink.");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(_keyFile);
            if ((mode & GroupOrOtherBits) != 0)
                throw new CryptographicException("Key file permissions are too broad.");
        }
        byte[] key = File.ReadAllBytes(_keyFile);
        if (key.Length != 32)
            throw new CryptographicException("Key file is invalid.");
        return key;
    }

    static void EnsurePrivateDirectory(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(dir);
            return;
        }
        if (Directory.Exists(dir))
        {
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new CryptographicException("Key directory must not be a symlink.");
        }
        else
        {
            Directory.CreateDirectory(dir, PrivateDirMode);
        }
        try { File.SetUnixFileMode(dir, PrivateDirMode); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
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
