using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Holm.Platform.Linux;

/// <summary>
/// Bounded, non-symlink, regular-file checks and reads for untrusted local paths (MPRIS artUrl, notification icons).
/// Uses Linux <c>statx</c> with <c>AT_SYMLINK_NOFOLLOW</c> before opening so FIFOs, sockets, symlinks, and device nodes
/// such as <c>/dev/zero</c> are rejected without blocking or traversing links.
/// </summary>
internal static class LinuxFileChecks
{
    public static bool IsRegularFile(string path, long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0')) return false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            if (info.LinkTarget != null || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                return false;
            if (OperatingSystem.IsLinux() && !IsLinuxRegularFile(path))
                return false;
            return info.Length >= 0 && info.Length <= maxBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public static async Task<byte[]?> ReadBoundedFileAsync(string path, int maxBytes, TimeSpan timeout)
    {
        if (!IsRegularFile(path, maxBytes)) return null;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            if (!stream.CanSeek || stream.Length <= 0 || stream.Length > maxBytes) return null;
            using var copy = new MemoryStream((int)stream.Length);
            var buffer = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes) return null;
                copy.Write(buffer, 0, read);
            }
            return total == 0 ? null : copy.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    public static byte[]? ReadBoundedFile(string path, int maxBytes)
    {
        if (!IsRegularFile(path, maxBytes)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!stream.CanSeek || stream.Length <= 0 || stream.Length > maxBytes) return null;
            using var copy = new MemoryStream((int)stream.Length);
            var buffer = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes) return null;
                copy.Write(buffer, 0, read);
            }
            return total == 0 ? null : copy.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }

    static unsafe bool IsLinuxRegularFile(string path)
    {
        const int AT_FDCWD = -100;
        const int AT_SYMLINK_NOFOLLOW = 0x100;
        const uint STATX_TYPE = 0x0001;
        const ushort S_IFMT = 0xF000;
        const ushort S_IFREG = 0x8000;
        const int ENOSYS = 38;
        try
        {
            byte* buf = stackalloc byte[256];
            int rc = statx(AT_FDCWD, path, AT_SYMLINK_NOFOLLOW, STATX_TYPE, buf);
            if (rc == 0)
            {
                uint mask = *(uint*)buf;
                ushort mode = *(ushort*)(buf + 28);
                return (mask & STATX_TYPE) != 0 && (mode & S_IFMT) == S_IFREG;
            }
            return Marshal.GetLastPInvokeError() == ENOSYS;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [DllImport("libc", SetLastError = true)]
    static extern unsafe int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, uint mask, byte* statxbuf);
}
