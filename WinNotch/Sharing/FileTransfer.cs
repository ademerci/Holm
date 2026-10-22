using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

public sealed record ShareFile(string Name, long Size);

/// <summary>What the sender proposes. The receiver sees this before anything is written to disk.</summary>
public sealed record ShareOffer(string Sender, IReadOnlyList<ShareFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>
/// The conversation over a <see cref="SecureChannel"/>: the sender offers file names and sizes, the
/// receiver answers yes or no, and only after a yes do the bytes follow. The same protocol runs over
/// the local network and over the internet relay.
/// </summary>
public static class FileTransfer
{
    const byte Offer = 1, Answer = 2, Data = 3, Done = 4, Received = 5;
    const int ChunkSize = 64 * 1024;
    public const int MaxFiles = 1000;
    public const long MaxTotalBytes = 100L * 1024 * 1024 * 1024;

    sealed record OfferDto(string Sender, List<FileDto> Files);
    sealed record FileDto(string Name, long Size);

    /// <summary>Offers the files, and sends them if the receiver accepts.</summary>
    /// <returns>True when the receiver accepted and confirmed everything arrived; false when they declined.</returns>
    public static async Task<bool> SendAsync(SecureChannel channel, string sender, IReadOnlyList<string> paths,
                                             IProgress<double>? progress, CancellationToken ct)
    {
        if (paths.Count == 0 || paths.Count > MaxFiles) throw new ArgumentException("Send between 1 and 1000 files.", nameof(paths));
        var files = paths.Select(p => new FileDto(Path.GetFileName(p), new FileInfo(p).Length)).ToList();
        long total = files.Sum(f => f.Size);
        if (total > MaxTotalBytes) throw new IOException("That's more than 100 GB.");

        var offer = JsonSerializer.SerializeToUtf8Bytes(new OfferDto(Trim(sender, 64), files));
        await channel.SendAsync(Prefix(Offer, offer), ct).ConfigureAwait(false);

        var answer = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        if (answer.Length != 2 || answer[0] != Answer) throw new InvalidDataException("Unexpected answer.");
        if (answer[1] != 1) return false;

        long sent = 0;
        var frame = new byte[1 + ChunkSize];
        frame[0] = Data;
        progress?.Report(0);
        for (int i = 0; i < paths.Count; i++)
        {
            long remaining = files[i].Size; // exactly what was offered, even if the file changed meanwhile
            await using var input = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize, useAsync: true);
            while (remaining > 0)
            {
                int read = await input.ReadAsync(frame.AsMemory(1, (int)Math.Min(ChunkSize, remaining)), ct).ConfigureAwait(false);
                if (read == 0) throw new IOException($"{files[i].Name} got shorter while sending.");
                await channel.SendAsync(frame.AsMemory(0, 1 + read), ct).ConfigureAwait(false);
                remaining -= read;
                sent += read;
                progress?.Report(total == 0 ? 1 : (double)sent / total);
            }
        }
        await channel.SendAsync(new[] { Done }, ct).ConfigureAwait(false);

        var confirmation = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        if (confirmation.Length != 1 || confirmation[0] != Received) throw new InvalidDataException("The receiver didn't confirm.");
        progress?.Report(1);
        return true;
    }

    /// <summary>
    /// Reads the offer, asks <paramref name="decide"/>, and on a yes saves the files into <paramref name="folder"/>.
    /// </summary>
    /// <returns>The saved paths, or null when declined.</returns>
    public static async Task<IReadOnlyList<string>?> ReceiveAsync(SecureChannel channel, Func<ShareOffer, Task<bool>> decide,
                                                                  string folder, IProgress<double>? progress, CancellationToken ct)
    {
        var first = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        if (first.Length < 2 || first[0] != Offer) throw new InvalidDataException("Expected an offer.");
        var offer = ParseOffer(first.AsSpan(1));

        bool accept = await decide(offer).ConfigureAwait(false);
        if (accept && !HasRoom(folder, offer.TotalBytes))
        {
            await channel.SendAsync(new[] { Answer, (byte)0 }, ct).ConfigureAwait(false);
            throw new IOException("Not enough free disk space.");
        }
        await channel.SendAsync(new[] { Answer, (byte)(accept ? 1 : 0) }, ct).ConfigureAwait(false);
        if (!accept) return null;

        Directory.CreateDirectory(folder);
        var saved = new List<string>();
        long total = offer.TotalBytes, received = 0;
        string? current = null;
        progress?.Report(0);
        try
        {
            foreach (var file in offer.Files)
            {
                current = UniquePath(folder, file.Name);
                long remaining = file.Size;
                await using (var output = new FileStream(current, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
                {
                    while (remaining > 0)
                    {
                        var frame = await channel.ReceiveAsync(ct).ConfigureAwait(false);
                        if (frame.Length < 2 || frame[0] != Data) throw new InvalidDataException("Expected file data.");
                        int length = frame.Length - 1;
                        if (length > remaining) throw new InvalidDataException("More data than offered.");
                        await output.WriteAsync(frame.AsMemory(1, length), ct).ConfigureAwait(false);
                        remaining -= length;
                        received += length;
                        progress?.Report(total == 0 ? 1 : (double)received / total);
                    }
                }
                MarkFromInternet(current);
                saved.Add(current);
                current = null;
            }

            var done = await channel.ReceiveAsync(ct).ConfigureAwait(false);
            if (done.Length != 1 || done[0] != Done) throw new InvalidDataException("Expected the end of the transfer.");
            await channel.SendAsync(new[] { Received }, ct).ConfigureAwait(false);
            progress?.Report(1);
            return saved;
        }
        catch
        {
            // Never leave a half-written file behind.
            if (current != null) { try { File.Delete(current); } catch { } }
            throw;
        }
    }

    static ShareOffer ParseOffer(ReadOnlySpan<byte> json)
    {
        OfferDto? dto;
        try { dto = JsonSerializer.Deserialize<OfferDto>(json); }
        catch (JsonException) { throw new InvalidDataException("Unreadable offer."); }
        if (dto?.Files == null || dto.Files.Count == 0 || dto.Files.Count > MaxFiles) throw new InvalidDataException("Bad file list.");
        if (dto.Files.Any(f => f == null || f.Size < 0)) throw new InvalidDataException("Bad file size.");
        long total = 0;
        foreach (var f in dto.Files)
        {
            total += f.Size;
            if (total > MaxTotalBytes) throw new InvalidDataException("Offer too large.");
        }
        var files = dto.Files.Select(f => new ShareFile(SafeFileName(f.Name), f.Size)).ToList();
        string sender = Trim(dto.Sender, 64);
        return new ShareOffer(sender.Length > 0 ? sender : "Someone", files);
    }

    static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A plain file name that can only land in the target folder: no folders, no "..", no characters
    /// Windows forbids, no device names, not too long.
    /// </summary>
    public static string SafeFileName(string? name)
    {
        name ??= "";
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0 || name.All(c => c == '.')) name = "file";
        string stem = Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(stem.TrimEnd(' ', '.').ToUpperInvariant())) name = "_" + name;
        if (name.Length > 150)
        {
            string ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = "";
            name = name[..(150 - ext.Length)] + ext;
        }
        return name;
    }

    /// <summary>"photo.png", or "photo (1).png" if that is taken.</summary>
    public static string UniquePath(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 1; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    /// <summary>The "downloaded from the internet" mark, so Windows warns before opening programs or macros.</summary>
    static void MarkFromInternet(string path)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch { } // not NTFS, or not allowed: the file is still fine
    }

    static bool HasRoom(string folder, long bytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return root == null || new DriveInfo(root).AvailableFreeSpace > bytes + 50L * 1024 * 1024;
        }
        catch { return true; }
    }

    static string Trim(string? text, int max)
    {
        text = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }

    static byte[] Prefix(byte tag, byte[] payload)
    {
        var message = new byte[1 + payload.Length];
        message[0] = tag;
        payload.CopyTo(message, 1);
        return message;
    }
}
