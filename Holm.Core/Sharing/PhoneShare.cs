using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

/// <summary>A file offered to the phone.</summary>
public sealed record PhoneFile(string Path, string Name, long Length);

/// <summary>
/// Files to a phone without an app: a small web server on this network, for the files you dropped and nothing else.
/// Its address carries 128 random bits, so only someone who scans the code (or gets the link) finds it; it serves
/// one page and the files, read-only, and stops when you're done. A phone on the same Wi-Fi opens it in its browser.
/// </summary>
public sealed class PhoneShare : IDisposable
{
    const int MaxHeader = 16 * 1024, MaxClients = 16;
    static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(15);

    static readonly Dictionary<string, string> Viewable = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".heic"] = "image/heic", [".mp4"] = "video/mp4", [".m4v"] = "video/x-m4v",
        [".mov"] = "video/quicktime", [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".pdf"] = "application/pdf",
        [".txt"] = "text/plain; charset=utf-8",
    };

    readonly TcpListener _listener;
    readonly string _token;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _slots = new(MaxClients);
    readonly bool[] _delivered;

    /// <summary>What the QR code says: http://192.168.1.23:52345/&lt;token&gt;/</summary>
    public string Url { get; }
    public IReadOnlyList<PhoneFile> Files { get; }
    public bool AllDelivered { get { lock (_delivered) return _delivered.All(d => d); } }

    /// <summary>The phone opened the page. Raised on a worker thread.</summary>
    public event Action? Visited;
    /// <summary>File index and bytes sent so far, every 256 KB. Raised on a worker thread.</summary>
    public event Action<int, long>? Sending;
    /// <summary>A file reached the phone whole. Raised on a worker thread.</summary>
    public event Action<int>? Delivered;

    PhoneShare(TcpListener listener, IPAddress address, IReadOnlyList<PhoneFile> files)
    {
        _listener = listener;
        _token = Base64Url(RandomNumberGenerator.GetBytes(16));
        Files = files;
        _delivered = new bool[files.Count];
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        Url = $"http://{host}:{port}/{_token}/";
    }

    /// <summary>Offers the files on <paramref name="address"/>, on a port Windows picks.</summary>
    /// <exception cref="IOException">A file can't be read.</exception>
    /// <exception cref="SocketException">No port could be opened.</exception>
    public static PhoneShare Start(IReadOnlyList<string> paths, IPAddress address)
    {
        if (paths.Count == 0) throw new ArgumentException("Nothing to offer.", nameof(paths));
        var files = paths.Select(p => new FileInfo(p)).Select(f => new PhoneFile(f.FullName, f.Name, f.Length)).ToList();
        var listener = new TcpListener(address, 0);
        listener.Start(MaxClients);
        var share = new PhoneShare(listener, address, files);
        _ = Task.Run(share.AcceptAsync);
        return share;
    }

    async Task AcceptAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { return; }
            catch (SocketException) when (!ct.IsCancellationRequested) { continue; }
            catch (SocketException) { return; }
            if (!_slots.Wait(0)) { client.Dispose(); continue; } // more at once than any phone needs
            _ = Task.Run(async () =>
            {
                try { await ServeAsync(client, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                catch (Exception ex) { Diag.Log("Phone link: " + ex.Message); }
                finally
                {
                    client.Dispose();
                    try { _slots.Release(); } catch (ObjectDisposedException) { }
                }
            });
        }
    }

    async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        var stream = client.GetStream();
        string? head;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(HeaderTimeout);
            head = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(false);
        }
        if (head == null) return;

        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            await WriteAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8", "Bad request"u8.ToArray(), false, ct).ConfigureAwait(false);
            return;
        }
        string method = parts[0];
        bool headOnly = method == "HEAD";
        if (method != "GET" && !headOnly)
        {
            await WriteAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Not allowed"u8.ToArray(), false, ct).ConfigureAwait(false);
            return;
        }

        var route = Route(parts[1], _token, Files.Count);
        switch (route.Kind)
        {
            case RouteKind.Page:
                Visited?.Invoke();
                await WriteAsync(stream, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page(Files, _token)), headOnly, ct).ConfigureAwait(false);
                break;
            case RouteKind.Download:
            case RouteKind.View:
                string? range = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))?[6..].Trim();
                await SendFileAsync(stream, route.File, route.Kind == RouteKind.View, range, headOnly, ct).ConfigureAwait(false);
                break;
            default:
                await WriteAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not found"u8.ToArray(), headOnly, ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>The request line and headers, up to the empty line. Null if the client sent too much or went quiet.</summary>
    static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeader];
        int filled = 0;
        try
        {
            while (filled < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
                if (read == 0) return null;
                int searchFrom = Math.Max(0, filled - 3);
                filled += read;
                int end = buffer.AsSpan(searchFrom, filled - searchFrom).IndexOf("\r\n\r\n"u8);
                if (end >= 0) return Encoding.Latin1.GetString(buffer, 0, searchFrom + end);
            }
        }
        catch (OperationCanceledException) { }
        return null;
    }

    async Task SendFileAsync(NetworkStream stream, int index, bool inline, string? range, bool headOnly, CancellationToken ct)
    {
        var file = Files[index];
        FileStream source;
        try { source = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WriteAsync(stream, 410, "Gone", "text/plain; charset=utf-8", "This file is no longer there"u8.ToArray(), headOnly, ct).ConfigureAwait(false);
            return;
        }
        await using (source)
        {
            long length = source.Length;
            long from = 0, to = length - 1;
            bool partial = false;
            if (range != null)
            {
                if (!TryParseRange(range, length, out from, out to))
                {
                    await WriteHeadersAsync(stream, 416, "Range Not Satisfiable", new[] { ("Content-Range", $"bytes */{length}"), ("Content-Length", "0") }, ct).ConfigureAwait(false);
                    return;
                }
                partial = true;
            }

            string type = inline && Viewable.TryGetValue(Path.GetExtension(file.Name), out var known) ? known : "application/octet-stream";
            var headers = new List<(string, string)>
            {
                ("Content-Type", type),
                ("Content-Length", (to - from + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Content-Disposition", Disposition(file.Name, inline)),
                ("Accept-Ranges", "bytes"),
            };
            if (partial) headers.Add(("Content-Range", $"bytes {from}-{to}/{length}"));
            await WriteHeadersAsync(stream, partial ? 206 : 200, partial ? "Partial Content" : "OK", headers, ct).ConfigureAwait(false);
            if (headOnly || length == 0)
            {
                if (!headOnly && length == 0) MarkDelivered(index);
                return;
            }

            source.Position = from;
            var buffer = new byte[64 * 1024];
            long left = to - from + 1, sent = 0, reported = 0;
            while (left > 0)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct).ConfigureAwait(false);
                if (read == 0) break;
                await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                left -= read;
                sent += read;
                if (sent - reported >= 256 * 1024)
                {
                    reported = sent;
                    Sending?.Invoke(index, from + sent);
                }
            }
            if (left == 0 && from == 0 && to == length - 1) MarkDelivered(index);
        }
    }

    void MarkDelivered(int index)
    {
        lock (_delivered) _delivered[index] = true;
        Delivered?.Invoke(index);
    }

    static Task WriteAsync(NetworkStream stream, int status, string reason, string type, byte[] body, bool headOnly, CancellationToken ct)
    {
        var headers = new[]
        {
            ("Content-Type", type),
            ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'"),
            ("Referrer-Policy", "no-referrer"),
        };
        return WriteThenBodyAsync(stream, status, reason, headers, headOnly ? Array.Empty<byte>() : body, ct);
    }

    static async Task WriteThenBodyAsync(NetworkStream stream, int status, string reason, IEnumerable<(string, string)> headers, byte[] body, CancellationToken ct)
    {
        await WriteHeadersAsync(stream, status, reason, headers, ct).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    static async Task WriteHeadersAsync(NetworkStream stream, int status, string reason, IEnumerable<(string Name, string Value)> headers, CancellationToken ct)
    {
        var text = new StringBuilder($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers) text.Append(name).Append(": ").Append(value).Append("\r\n");
        text.Append("Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(text.ToString()), ct).ConfigureAwait(false);
    }

    public enum RouteKind { NotFound, Page, Download, View }
    public readonly record struct RouteResult(RouteKind Kind, int File = -1);

    /// <summary>"/token/" is the page, "/token/f/2" downloads the third file, "/token/v/2" shows it. Anything else: not found.</summary>
    internal static RouteResult Route(string target, string token, int fileCount)
    {
        int query = target.IndexOfAny(new[] { '?', '#' });
        if (query >= 0) target = target[..query];
        var segments = target.Split('/');
        // "/token" → ["", "token"]; "/token/" → ["", "token", ""]; "/token/f/2" → ["", "token", "f", "2"]
        if (segments.Length < 2 || segments[0].Length != 0 || !SameToken(segments[1], token)) return new(RouteKind.NotFound);
        if (segments.Length == 2 || segments.Length == 3 && segments[2].Length == 0) return new(RouteKind.Page);
        if (segments.Length == 4 && segments[2] is "f" or "v"
            && int.TryParse(segments[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
            && index < fileCount)
            return new(segments[2] == "f" ? RouteKind.Download : RouteKind.View, index);
        return new(RouteKind.NotFound);
    }

    static bool SameToken(string given, string token)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(token));

    /// <summary>"bytes=0-99", "bytes=100-", "bytes=-500"; one range only.</summary>
    internal static bool TryParseRange(string header, long length, out long from, out long to)
    {
        from = 0;
        to = length - 1;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || length == 0) return false;
        string spec = header[6..].Trim();
        if (spec.Contains(',')) return false;
        int dash = spec.IndexOf('-');
        if (dash < 0) return false;
        string a = spec[..dash].Trim(), b = spec[(dash + 1)..].Trim();
        var number = System.Globalization.NumberStyles.None;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (a.Length == 0)
        {
            if (!long.TryParse(b, number, culture, out long suffix) || suffix == 0) return false;
            from = Math.Max(0, length - suffix);
            return true;
        }
        if (!long.TryParse(a, number, culture, out from) || from >= length) return false;
        if (b.Length == 0) return true;
        if (!long.TryParse(b, number, culture, out to) || to < from) return false;
        to = Math.Min(to, length - 1);
        return true;
    }

    /// <summary>attachment; filename="Fotos_.zip"; filename*=UTF-8''Fotos%C3%A4.zip</summary>
    internal static string Disposition(string name, bool inline)
    {
        string ascii = new string(name.Select(c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
        return $"{(inline ? "inline" : "attachment")}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    /// <summary>The page the phone sees: the files, each to download (and to open, when the phone can show it).</summary>
    internal static string Page(IReadOnlyList<PhoneFile> files, string token)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\">")
            .Append("<title>Holm</title><style>")
            .Append(":root{color-scheme:dark}body{margin:0;padding:28px 16px;background:#000;color:#fff;")
            .Append("font:16px -apple-system,system-ui,'Segoe UI',Roboto,sans-serif}h1{font-size:24px;margin:0 0 4px}")
            .Append("p{color:#a6a6a6;margin:0 0 22px}ul{list-style:none;margin:0;padding:0}")
            .Append("li{display:flex;align-items:center;gap:10px;background:#1c1c1e;border-radius:16px;padding:14px 14px 14px 16px;margin-bottom:10px}")
            .Append(".n{flex:1;min-width:0;overflow-wrap:anywhere}.s{display:block;color:#a6a6a6;font-size:13px;margin-top:2px}")
            .Append("a{background:#0a84ff;color:#fff;text-decoration:none;border-radius:18px;padding:8px 14px;font-weight:600;font-size:14px;white-space:nowrap}")
            .Append("a.o{background:#3a3a3c}.note{margin:22px 0 0;font-size:13px}")
            .Append("</style></head><body><h1>Files from Holm</h1><p>")
            .Append(files.Count == 1 ? "1 file" : $"{files.Count} files").Append(" · ").Append(Size(files.Sum(f => f.Length)))
            .Append("</p><ul>");
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            html.Append("<li><span class=\"n\">").Append(WebUtility.HtmlEncode(file.Name))
                .Append("<span class=\"s\">").Append(Size(file.Length)).Append("</span></span>");
            if (Viewable.ContainsKey(Path.GetExtension(file.Name)))
                html.Append("<a class=\"o\" href=\"/").Append(token).Append("/v/").Append(i).Append("\">Open</a>");
            html.Append("<a href=\"/").Append(token).Append("/f/").Append(i).Append("\" download>Download</a></li>");
        }
        html.Append("</ul><p class=\"note\">This page only works on the same Wi-Fi as the PC, and only until you're done there.</p></body></html>");
        return html.ToString();
    }

    internal static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        _cts.Dispose();
    }
}

/// <summary>The address a phone on the same Wi-Fi reaches this PC at.</summary>
public static class LanAddress
{
    public readonly record struct Candidate(string Name, string Description, NetworkInterfaceType Type, bool Up, IPAddress Address, bool HasGateway);

    static readonly string[] Virtual =
    {
        "hyper-v", "vethernet", "virtualbox", "vmware", "wsl", "docker", "vpn", "tap-windows", "wintun", "wireguard",
        "tailscale", "zerotier", "bluetooth", "loopback", "npcap",
    };

    static readonly NetworkInterfaceType[] Physical =
    {
        NetworkInterfaceType.Wireless80211, NetworkInterfaceType.Ethernet, NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx, NetworkInterfaceType.Ethernet3Megabit,
    };

    /// <summary>A Wi-Fi or Ethernet adapter's IPv4 address on a network with a router, Wi-Fi first. Null if there is none.</summary>
    public static IPAddress? Pick(IEnumerable<Candidate> candidates)
        => candidates
            .Where(c => c.Up && c.HasGateway && Array.IndexOf(Physical, c.Type) >= 0
                        && c.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(c.Address)
                        && !c.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)
                        && !Virtual.Any(v => (c.Name + " " + c.Description).Contains(v, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Type == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .Select(c => c.Address)
            .FirstOrDefault();

    public static IPAddress? Find()
    {
        var candidates = new List<Candidate>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties properties;
                try { properties = nic.GetIPProperties(); }
                catch (NetworkInformationException) { continue; }
                bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var unicast in properties.UnicastAddresses)
                    candidates.Add(new Candidate(nic.Name, nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus == OperationalStatus.Up, unicast.Address, gateway));
            }
        }
        catch (NetworkInformationException ex) { Diag.Log("Network adapters unavailable: " + ex.Message); }
        return Pick(candidates);
    }
}
