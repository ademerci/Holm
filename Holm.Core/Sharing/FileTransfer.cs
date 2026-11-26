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
/// <param name="IsText">A beamed text: one small UTF-8 file meant for the clipboard rather than Downloads.</param>
public sealed record ShareOffer(string Sender, IReadOnlyList<ShareFile> Files, bool IsText = false)
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
    const byte Offer = 1, Answer = 2, Data = 3, Done = 4, Received = 5, Cancelled = 6;
    const int ChunkSize = 64 * 1024;
    public const int MaxFiles = 1000;
    public const long MaxTotalBytes = 100L * 1024 * 1024 * 1024;

    /// <summary>A beamed text is at most this big; anything larger is an ordinary file.</summary>
    public const long MaxTextBytes = 1024 * 1024;
    /// <summary>Maximum wait for the initial Offer frame after handshake.</summary>
    public static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Maximum idle wait for each Data/Done frame during an accepted transfer.</summary>
    public static readonly TimeSpan FrameIdleTimeout = TimeSpan.FromSeconds(30);

    // "Text" came with 1.7. Older Holm skips the field it doesn't know and saves the text as a file in Downloads.
    sealed record OfferDto(string Sender, List<FileDto> Files, bool? Text = null);
    sealed record FileDto(string Name, long Size);

    public sealed class SenderCancelledException : OperationCanceledException
    {
        public SenderCancelledException() : base("The sender cancelled the transfer before it was accepted.") { }
    }

    /// <summary>Offers the files, and sends them if the receiver accepts.</summary>
    /// <returns>True when the receiver accepted and confirmed everything arrived; false when they declined.</returns>
    /// <param name="text">The one file is a beamed text (see <see cref="ShareOffer.IsText"/>).</param>
    public static async Task<bool> SendAsync(SecureChannel channel, string sender, IReadOnlyList<string> paths,
                                             IProgress<double>? progress, CancellationToken ct, bool text = false)
    {
        if (paths.Count == 0 || paths.Count > MaxFiles) throw new ArgumentException("Send between 1 and 1000 files.", nameof(paths));
        var files = paths.Select(p => new FileDto(Path.GetFileName(p), new FileInfo(p).Length)).ToList();
        long total = files.Sum(f => f.Size);
        if (total > MaxTotalBytes) throw new IOException("That's more than 100 GB.");

        if (text && (files.Count != 1 || total > MaxTextBytes)) throw new ArgumentException("A text is one small file.", nameof(paths));
        var offer = JsonSerializer.SerializeToUtf8Bytes(new OfferDto(Trim(sender, 64), files, text ? true : null),
                                                        new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        await channel.SendAsync(Prefix(Offer, offer), ct).ConfigureAwait(false);

        byte[] answer;
        try { answer = await channel.ReceiveAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The receiver may still be looking at the offer. Tell them why the connection is closing.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await channel.SendAsync(new[] { Cancelled }, timeout.Token).ConfigureAwait(false);
            }
            catch { }
            throw;
        }
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
        => await ReceiveAsync(channel, (offer, _) => decide(offer), folder, progress, ct).ConfigureAwait(false);

    public static Task<IReadOnlyList<string>?> ReceiveAsync(SecureChannel channel,
                                                            Func<ShareOffer, CancellationToken, Task<bool>> decide,
                                                            string folder, IProgress<double>? progress, CancellationToken ct)
        => ReceiveAsync(channel, decide, _ => folder, progress, ct);

    /// <summary>As above, with the folder chosen once the offer is known (a beamed text needn't land in Downloads).</summary>
    public static async Task<IReadOnlyList<string>?> ReceiveAsync(SecureChannel channel,
                                                                  Func<ShareOffer, CancellationToken, Task<bool>> decide,
                                                                  Func<ShareOffer, string> folderFor, IProgress<double>? progress, CancellationToken ct)
    {
        byte[] first;
        using (var offerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            offerTimeout.CancelAfter(OfferTimeout);
            try { first = await channel.ReceiveAsync(offerTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for the transfer offer.");
            }
        }
        if (first.Length < 2 || first[0] != Offer) throw new InvalidDataException("Expected an offer.");
        var offer = ParseOffer(first.AsSpan(1));
        string folder = folderFor(offer);

        // While the person decides, the sender may cancel: one read stays open for that. It is never cancelled
        // (cancelling a pending socket read isn't dependable on every system, and waiting for it left both sides
        // waiting for each other: the receiver for the read to stop, the sender for the answer). After a yes it
        // simply becomes the first read of the transfer.
        using var decisionLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<bool> decision = decide(offer, decisionLifetime.Token);
        Task<byte[]>? pending = channel.ReceiveAsync(ct);
        if (await Task.WhenAny(decision, pending).ConfigureAwait(false) == pending)
        {
            decisionLifetime.Cancel();
            try { await decision.ConfigureAwait(false); } catch { }
            var frame = await pending.ConfigureAwait(false); // a dropped connection throws here
            if (IsCancellation(frame)) throw new SenderCancelledException();
            throw new InvalidDataException("Unexpected message while waiting for a file decision.");
        }

        bool accept;
        try { accept = await decision.ConfigureAwait(false); }
        catch { Forget(pending); throw; }

        if (accept && !HasRoom(folder, offer.TotalBytes))
        {
            Forget(pending);
            await channel.SendAsync(new[] { Answer, (byte)0 }, ct).ConfigureAwait(false);
            throw new IOException("Not enough free disk space.");
        }
        try { await channel.SendAsync(new[] { Answer, (byte)(accept ? 1 : 0) }, ct).ConfigureAwait(false); }
        catch { Forget(pending); throw; }
        if (!accept) { Forget(pending); return null; }

        // The open read gets the first frame after the answer; the rest are read one by one with an idle timeout.
        async Task<byte[]> NextAsync()
        {
            if (pending != null)
            {
                var firstRead = pending;
                pending = null;
                try { return await firstRead.WaitAsync(FrameIdleTimeout, ct).ConfigureAwait(false); }
                catch (TimeoutException) { Forget(firstRead); throw; }
            }
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(FrameIdleTimeout);
            try { return await channel.ReceiveAsync(idle.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for transfer data.");
            }
        }

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
                        var frame = await NextAsync().ConfigureAwait(false);
                        if (IsCancellation(frame)) throw new SenderCancelledException();
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

            var done = await NextAsync().ConfigureAwait(false);
            if (IsCancellation(done)) throw new SenderCancelledException();
            if (done.Length != 1 || done[0] != Done) throw new InvalidDataException("Expected the end of the transfer.");
            await channel.SendAsync(new[] { Received }, ct).ConfigureAwait(false);
            progress?.Report(1);
            return saved;
        }
        catch
        {
            Forget(pending); // not read yet if the first file couldn't be created
            // Never leave a half-written file behind.
            if (current != null) { try { File.Delete(current); } catch { } }
            throw;
        }
    }

    static bool IsCancellation(byte[] frame) => frame.Length == 1 && frame[0] == Cancelled;

    /// <summary>A read nobody waits for any more: it ends when the connection closes, and its error is no news.</summary>
    static void Forget(Task<byte[]>? read) => read?.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);

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
            if (f.Size < 0 || f.Size > MaxTotalBytes || f.Size > MaxTotalBytes - total)
                throw new InvalidDataException("Offer too large.");
            total += f.Size;
        }
        var files = dto.Files.Select(f => new ShareFile(SafeFileName(f.Name), f.Size)).ToList();
        string sender = Trim(dto.Sender, 64);
        // Only one small file can be a text; anything else is saved as the files it is.
        bool text = dto.Text == true && files.Count == 1 && total <= MaxTextBytes;
        return new ShareOffer(sender.Length > 0 ? sender : "Someone", files, text);
    }

    static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A plain file name that can only land in the target folder: no folders, no "..", no characters
    /// Windows forbids, no device names, not too long, and no text-direction marks, which could make
    /// "photo\u202Egpj.exe" show up as "photoexe.jpg" in the question and in Explorer.
    /// </summary>
    public static string SafeFileName(string? name)
    {
        name ??= "";
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Select(c => char.IsControl(c) || IsDirectionMark(c) || InvalidOnWindows.Contains(c) ? '_' : c).ToArray());
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

    /// <summary>
    /// What Windows forbids in a file name, written out instead of asked from the system this runs on: Linux and
    /// macOS allow nearly everything, and a file received there may well travel on to a Windows PC.
    /// </summary>
    const string InvalidOnWindows = "\"<>|:*?\\/";

    static bool IsDirectionMark(char c) => c is '\u200E' or '\u200F' or '\u061C' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

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

    /// <summary>
    /// The "downloaded from the internet" mark, so Windows warns before opening programs or macros. Linux has none
    /// (there the stream would be a second file).
    /// </summary>
    internal static void MarkFromInternet(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch { } // not NTFS, or not allowed: the file is still fine
    }

    internal static bool HasRoom(string folder, long bytes)
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
