using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch;

/// <summary>
/// Shared security primitives for Holm: SSRF IP/URI guards, dangerous file-open extension blocking,
/// private Unix directory/file permissions (0700/0600), and strict NIST P-256 SPKI validation.
/// </summary>
public static class SecurityGuards
{
    public const string NistP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Internal test-only switch allowing loopback HTTP/IP targets when unit tests spin up local fake servers.
    /// Production code never sets this.
    /// </summary>
    internal static bool AllowLoopbackForTests { get; set; }

    internal static bool IsTestLoopbackAllowed() => AllowLoopbackForTests || FriendsService.AllowLoopbackServers;

    static readonly HashSet<string> DangerousOpenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows executables, libraries, scripts, shortcuts, installers, control-panel & shell items
        ".exe", ".dll", ".bat", ".cmd", ".com", ".pif", ".scr", ".msi", ".msp", ".msix", ".appx",
        ".ps1", ".psm1", ".psd1", ".ps2", ".ps1xml", ".vbs", ".vbe", ".vb", ".js", ".jse", ".wsf", ".wsh", ".ws", ".wsc",
        ".hta", ".lnk", ".url", ".reg", ".inf", ".cpl", ".msc", ".chm", ".gadget", ".scf",
        ".settingcontent-ms", ".library-ms", ".application", ".jnlp", ".sct", ".shb", ".shs",
        // Cross-platform / Unix / macOS / mobile executables, launchers, scripts & packages
        ".desktop", ".sh", ".bash", ".zsh", ".csh", ".ksh", ".appimage", ".jar",
        ".py", ".pyw", ".rb", ".pl", ".command", ".app", ".apk", ".dmg", ".pkg", ".deb", ".rpm",
        // Disk images that auto-mount and bypass MotW or host payloads
        ".iso", ".img", ".vhd", ".vhdx",
    };

    /// <summary>
    /// True if opening <paramref name="pathOrName"/> with the OS default handler (<c>ShellExecute</c>/<c>xdg-open</c>)
    /// could execute code, run a script, follow a shortcut/launcher, or mount a disk image.
    /// </summary>
    public static bool IsDangerousOpenExtension(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName)) return true;
        string trimmed = pathOrName.Trim().TrimEnd('.', ' ');
        if (trimmed.Length == 0) return true;
        string ext = Path.GetExtension(trimmed);
        return ext.Length > 0 && DangerousOpenExtensions.Contains(ext);
    }

    /// <summary>
    /// True only for globally routable public IPv4/IPv6 addresses. Rejects loopback, RFC1918 private,
    /// link-local, multicast, unspecified, CGNAT (100.64.0.0/10), IPv6 ULA (fc00::/7), IPv4-mapped
    /// private/non-public addresses, documentation, benchmarking, 6to4, Teredo, and reserved ranges.
    /// </summary>
    public static bool IsPublicIp(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            if (!address.TryWriteBytes(b, out int written) || written != 4) return false;

            if (b[0] == 0) return false;                                 // 0.0.0.0/8 unspecified / "this network"
            if (b[0] == 10) return false;                                // 10.0.0.0/8 RFC1918
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return false;        // 100.64.0.0/10 CGNAT (RFC6598)
            if (b[0] == 127) return false;                               // 127.0.0.0/8 loopback
            if (b[0] == 169 && b[1] == 254) return false;                // 169.254.0.0/16 link-local / cloud IMDS
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;   // 172.16.0.0/12 RFC1918
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;     // 192.0.0.0/24 IETF protocol assignments
            if (b[0] == 192 && b[1] == 0 && b[2] == 2) return false;     // 192.0.2.0/24 TEST-NET-1
            if (b[0] == 192 && b[1] == 168) return false;                // 192.168.0.0/16 RFC1918
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false; // 198.18.0.0/15 benchmarking
            if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false;  // 198.51.100.0/24 TEST-NET-2
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;   // 203.0.113.0/24 TEST-NET-3
            if (b[0] >= 224) return false;                               // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved/broadcast
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || address.IsIPv6Teredo)
                return false;

            Span<byte> b = stackalloc byte[16];
            if (!address.TryWriteBytes(b, out int written) || written != 16) return false;

            if ((b[0] & 0xFE) == 0xFC) return false;                     // fc00::/7 ULA
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return false;     // fe80::/10 link-local
            if (b[0] == 0xFF) return false;                              // ff00::/8 multicast
            // Only global unicast (2000::/3) is globally routable; excludes ::/128, ::1, 64:ff9b::/96, 100::/64, etc.
            if ((b[0] & 0xE0) != 0x20) return false;
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 documentation
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return false; // 2001::/32 Teredo
            if (b[0] == 0x20 && b[1] == 0x02) return false;              // 2002::/16 6to4 (may embed private IPv4)
            return true;
        }

        return false;
    }

    static bool IsAllowedConnectIp(IPAddress ip, bool allowLoopbackWhenTesting)
        => IsPublicIp(ip) || (allowLoopbackWhenTesting && IsTestLoopbackAllowed() && IPAddress.IsLoopback(ip));

    /// <summary>
    /// Validates that <paramref name="url"/> is an absolute HTTPS URL without userinfo, query, or fragment,
    /// targeting a public host/IP (or loopback HTTP when <paramref name="allowLoopbackWhenTesting"/> and the
    /// internal test hook are enabled).
    /// </summary>
    public static bool IsSafePublicHttpsUri(string? url, bool allowLoopbackWhenTesting = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        bool loopbackAllowed = allowLoopbackWhenTesting && IsTestLoopbackAllowed();
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!loopbackAllowed) return false;
            return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return IPAddress.TryParse(uri.Host, out var ip) && IsAllowedConnectIp(ip, allowLoopbackWhenTesting);

        if (uri.HostNameType != UriHostNameType.Dns) return false;
        string host = uri.Host.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return loopbackAllowed;
        if (!host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler"/> that resolves DNS in <c>ConnectCallback</c>, verifies every resolved
    /// IP with <see cref="IsPublicIp"/>, and connects directly to the validated IP (preventing SSRF and DNS rebinding).
    /// </summary>
    public static SocketsHttpHandler CreateSafeHttpHandler(bool allowRedirects = false, bool allowLoopbackWhenTesting = false)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = allowRedirects,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, ct) =>
            {
                string host = context.DnsEndPoint.Host;
                IPAddress[] addresses;
                if (IPAddress.TryParse(host, out var literal))
                    addresses = new[] { literal };
                else
                    addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

                if (addresses.Length == 0)
                    throw new HttpRequestException($"No addresses found for {host}.");

                foreach (var ip in addresses)
                {
                    if (!IsAllowedConnectIp(ip, allowLoopbackWhenTesting))
                        throw new HttpRequestException($"Blocked connection to non-public address for {host}.");
                }

                Exception? lastError = null;
                foreach (var ip in addresses)
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        socket.Dispose();
                        lastError = ex;
                    }
                }
                throw new HttpRequestException($"Couldn't connect to {host}.", lastError);
            },
        };
    }

    // ───────────── Private Unix directories & files (0700 / 0600) ─────────────

    static readonly UnixFileMode PrivateDirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    static readonly UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    static readonly UnixFileMode GroupOrOtherBits =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>Creates <paramref name="path"/> and enforces mode <c>0700</c> on Unix.</summary>
    public static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }
        if (Directory.Exists(path))
        {
            if (new DirectoryInfo(path).LinkTarget != null)
                throw new IOException("Refusing to use symlinked private directory.");
        }
        else
        {
            Directory.CreateDirectory(path, PrivateDirMode);
        }
        try { File.SetUnixFileMode(path, PrivateDirMode); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
    }

    /// <summary>Enforces mode <c>0600</c> on <paramref name="path"/> on Unix (no-op on Windows).</summary>
    public static void SetPrivatePermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(path, PrivateFileMode); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
    }

    /// <summary>Atomically writes UTF-8 text to <paramref name="path"/> with <c>0600</c> permissions on Unix.</summary>
    public static void WritePrivateFile(string path, string content)
        => WritePrivateFile(path, Encoding.UTF8.GetBytes(content));

    /// <summary>Atomically writes bytes to <paramref name="path"/> with <c>0600</c> permissions on Unix.</summary>
    public static void WritePrivateFile(string path, byte[] content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string temp = path + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + ".tmp";
        try
        {
            using (var stream = OpenNewPrivateFile(temp))
                stream.Write(content, 0, content.Length);
            SetPrivatePermissions(temp);
            File.Move(temp, path, overwrite: true);
            SetPrivatePermissions(path);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Creates a brand-new file with <see cref="FileMode.CreateNew"/> and <c>0600</c> permissions on Unix.
    /// </summary>
    public static FileStream OpenNewPrivateFile(string path, bool useAsync = false)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = useAsync ? FileOptions.Asynchronous : FileOptions.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFileMode;
        return new FileStream(path, options);
    }

    /// <summary>
    /// Creates a uniquely named file inside <paramref name="directory"/> using randomized naming,
    /// <see cref="FileMode.CreateNew"/>, and <c>0600</c> permissions on Unix.
    /// </summary>
    public static FileStream CreatePrivateTempFile(string directory, string extension, out string path, string? prefix = null, bool useAsync = false)
    {
        EnsurePrivateDirectory(directory);
        string ext = string.IsNullOrEmpty(extension) ? "" : extension.StartsWith('.') ? extension : "." + extension;
        string namePrefix = string.IsNullOrEmpty(prefix) ? "" : prefix;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            string random = Path.GetRandomFileName().Replace(".", "");
            string candidate = Path.Combine(directory, namePrefix + random + ext);
            try
            {
                var stream = OpenNewPrivateFile(candidate, useAsync);
                path = candidate;
                return stream;
            }
            catch (IOException) when (attempt < 15) { }
        }
        throw new IOException("Couldn't create a private temporary file.");
    }

    /// <summary>
    /// Returns a user-private temporary directory for <paramref name="subFolder"/>.
    /// On Unix, prefers <c>$XDG_RUNTIME_DIR/holm/&lt;subFolder&gt;</c> when verified non-symlink,
    /// mode <c>0700</c>, and owned by the current user; otherwise uses <c>Diag.Folder/tmp/&lt;subFolder&gt;</c> (<c>0700</c>).
    /// </summary>
    public static string GetPrivateTempDirectory(string subFolder)
    {
        if (OperatingSystem.IsWindows())
        {
            string winDir = Path.Combine(Path.GetTempPath(), "Holm", subFolder);
            Directory.CreateDirectory(winDir);
            return winDir;
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) && IsOwnedPrivateDir(xdg))
        {
            try
            {
                string root = Path.Combine(xdg, "holm");
                EnsurePrivateDirectory(root);
                string dir = Path.Combine(root, subFolder);
                EnsurePrivateDirectory(dir);
                return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        EnsurePrivateDirectory(Diag.Folder);
        string tmpRoot = Path.Combine(Diag.Folder, "tmp");
        EnsurePrivateDirectory(tmpRoot);
        string fallback = Path.Combine(tmpRoot, subFolder);
        EnsurePrivateDirectory(fallback);
        return fallback;
    }

    static bool IsOwnedPrivateDir(string path)
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            if (!Directory.Exists(path)) return false;
            var info = new DirectoryInfo(path);
            if (info.LinkTarget != null) return false;
            var mode = File.GetUnixFileMode(path);
            if ((mode & GroupOrOtherBits) != 0) return false;
            // POSIX chmod(2) succeeds only when the calling process owns the directory (or is root).
            File.SetUnixFileMode(path, mode);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ───────────── Strict NIST P-256 SPKI validation ─────────────

    /// <summary>
    /// Imports a SubjectPublicKeyInfo key and verifies it uses the exact NIST P-256 curve OID
    /// (<c>1.2.840.10045.3.1.7</c>) with no trailing bytes. Throws <see cref="CryptographicException"/> otherwise.
    /// </summary>
    public static ECDiffieHellman ImportP256PublicKey(ReadOnlySpan<byte> spki)
    {
        var ecdh = ECDiffieHellman.Create();
        try
        {
            ecdh.ImportSubjectPublicKeyInfo(spki, out int read);
            if (read != spki.Length || ecdh.KeySize != 256)
                throw new CryptographicException("Key is not NIST P-256.");
            var p = ecdh.ExportParameters(includePrivateParameters: false);
            if (!p.Curve.IsNamed || !string.Equals(p.Curve.Oid?.Value, NistP256Oid, StringComparison.Ordinal))
                throw new CryptographicException("Key curve is not NIST P-256.");
            return ecdh;
        }
        catch
        {
            ecdh.Dispose();
            throw;
        }
    }

    /// <summary>True if <paramref name="spki"/> is a valid NIST P-256 SubjectPublicKeyInfo public key.</summary>
    public static bool IsP256Spki(ReadOnlySpan<byte> spki)
    {
        try
        {
            using var key = ImportP256PublicKey(spki);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
