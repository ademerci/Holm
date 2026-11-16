using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch;

public enum FocumonPhase { Idle, Focus, Break }

/// <summary>
/// What the Focumon page says right now. <see cref="Anchor"/> is when a countdown ends or a count-up began, so the
/// notch can tick by itself between two messages (a browser slows pages down that nobody looks at).
/// </summary>
public sealed record FocumonStatus(string Monster, string? ImageUrl, FocumonPhase Phase, bool CountsDown, DateTime Anchor, DateTime Received)
{
    /// <summary>A page that hasn't been heard from for this long is gone (the browser was closed or put to sleep).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(150);

    public bool IsStale(DateTime nowUtc) => nowUtc - Received > StaleAfter;

    /// <summary>The time on the clock now: what is left of a countdown, or how long a count-up has run. Null when idle.</summary>
    public TimeSpan? Clock(DateTime nowUtc)
    {
        if (Phase == FocumonPhase.Idle) return null;
        var span = CountsDown ? Anchor - nowUtc : nowUtc - Anchor;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }
}

public static class Focumon
{
    public const string AppUrl = "https://www.focumon.com/app";
    const int MaxName = 40, MaxImageUrl = 300;
    static readonly TimeSpan MaxClock = TimeSpan.FromHours(24);

    /// <summary>
    /// One message of the browser extension: <c>{"v":1,"phase":"focus|break|idle|gone","down":true,"seconds":1500,
    /// "monster":"Name","image":"https://..."}</c>. Null when it isn't one (the browser or Holm is out of date, or it's rubbish).
    /// A "gone" message returns <paramref name="gone"/> = true and no status.
    /// </summary>
    public static FocumonStatus? TryParse(string json, DateTime nowUtc, out bool gone)
    {
        gone = false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1) return null;
            if (!root.TryGetProperty("phase", out var phaseText) || phaseText.ValueKind != JsonValueKind.String) return null;
            FocumonPhase phase;
            switch (phaseText.GetString())
            {
                case "focus": phase = FocumonPhase.Focus; break;
                case "break": phase = FocumonPhase.Break; break;
                case "idle": phase = FocumonPhase.Idle; break;
                case "gone": gone = true; return null;
                default: return null;
            }

            bool down = root.TryGetProperty("down", out var downValue) && downValue.ValueKind == JsonValueKind.True;
            DateTime anchor = nowUtc;
            if (phase != FocumonPhase.Idle)
            {
                if (!root.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number || !seconds.TryGetDouble(out double value)) return null;
                if (double.IsNaN(value) || value < 0 || value > MaxClock.TotalSeconds) return null;
                anchor = down ? nowUtc.AddSeconds(value) : nowUtc.AddSeconds(-value);
            }

            string monster = Clean(root.TryGetProperty("monster", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null);
            string? image = root.TryGetProperty("image", out var url) && url.ValueKind == JsonValueKind.String ? SafeImage(url.GetString()) : null;
            return new FocumonStatus(monster, image, phase, down, anchor, nowUtc);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>A name to show: no control characters, no line breaks, shortened.</summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var clean = new StringBuilder();
        foreach (char c in text)
            clean.Append(char.IsControl(c) ? ' ' : c);
        string result = string.Join(' ', clean.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return result.Length <= MaxName ? result : result[..MaxName].TrimEnd();
    }

    /// <summary>
    /// The picture of the monster is only fetched from Focumon's own monster assets
    /// (<c>/assets/focumon/mini/…</c> or <c>/assets/focumon/battle/…</c>), never logos or page screenshots.
    /// </summary>
    internal static string? SafeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxImageUrl) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        bool ours = uri.Host.Equals("focumon.com", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith(".focumon.com", StringComparison.OrdinalIgnoreCase);
        if (!ours) return null;
        string path = uri.AbsolutePath;
        bool monster = path.StartsWith("/assets/focumon/mini/", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/assets/focumon/battle/", StringComparison.OrdinalIgnoreCase);
        return monster ? uri.AbsoluteUri : null;
    }

    /// <summary>"24:13", or "1:02:03" from an hour on.</summary>
    public static string Format(TimeSpan clock)
    {
        long seconds = (long)Math.Floor(clock.TotalSeconds);
        if (seconds < 0) seconds = 0;
        long hours = seconds / 3600, minutes = seconds / 60 % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds % 60:00}");
    }
}

/// <summary>
/// Where the Focumon browser extension tells Holm what the page shows: a tiny web server on this PC only (127.0.0.1),
/// taking one kind of message. A web page can't reach it on purpose: a browser tells the server which page a request
/// comes from, and only the extension is let in. It never answers with data and never touches anything else.
/// </summary>
public sealed class FocumonLink : IDisposable
{
    /// <summary>The port the extension talks to.</summary>
    public const int DefaultPort = 47631;
    public const string RoutePath = "/focumon";
    public const string Header = "X-Holm-Focumon";
    const int MaxHead = 8 * 1024, MaxBody = 4 * 1024, MaxClients = 8;
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _slots = new(MaxClients);

    public int Port { get; }

    /// <summary>A page's message arrived. Raised on a worker thread.</summary>
    public event Action<FocumonStatus>? Updated;
    /// <summary>The page was closed. Raised on a worker thread.</summary>
    public event Action? Gone;

    FocumonLink(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Starts listening; null if the port is taken (a second Holm, or another program).</summary>
    public static FocumonLink? TryStart(int port = DefaultPort)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            listener.Start(MaxClients);
            var link = new FocumonLink(listener);
            _ = Task.Run(link.AcceptAsync);
            return link;
        }
        catch (SocketException ex)
        {
            Diag.Log("Focumon link: " + ex.Message);
            listener?.Stop();
            return null;
        }
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
            if (!_slots.Wait(0)) { client.Dispose(); continue; }
            _ = Task.Run(async () =>
            {
                try { await ServeAsync(client, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                catch (Exception ex) { Diag.Log("Focumon link: " + ex.Message); }
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
        var stream = client.GetStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Patience);

        var (head, rest) = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(false);
        if (head == null) { await ReplyAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }

        var request = Request.Parse(head);
        switch (Judge(request, Port))
        {
            case Verdict.Allowed: break;
            case Verdict.TooBig: await ReplyAsync(stream, 413, "Payload Too Large", ct).ConfigureAwait(false); return;
            case Verdict.Malformed: await ReplyAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return;
            case Verdict.WrongMethod: await ReplyAsync(stream, 405, "Method Not Allowed", ct).ConfigureAwait(false); return;
            default: await ReplyAsync(stream, 403, "Forbidden", ct).ConfigureAwait(false); return;
        }

        var body = new byte[request.ContentLength];
        int have = Math.Min(rest.Length, body.Length);
        Array.Copy(rest, body, have);
        while (have < body.Length)
        {
            int read = await stream.ReadAsync(body.AsMemory(have), timeout.Token).ConfigureAwait(false);
            if (read == 0) { await ReplyAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }
            have += read;
        }

        var status = Focumon.TryParse(Encoding.UTF8.GetString(body), DateTime.UtcNow, out bool gone);
        if (status != null) Updated?.Invoke(status);
        else if (gone) Gone?.Invoke();
        else { await ReplyAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }
        await ReplyAsync(stream, 204, "No Content", ct).ConfigureAwait(false);
    }

    internal enum Verdict { Allowed, Forbidden, WrongMethod, Malformed, TooBig }

    internal readonly record struct Request(string Method, string Target, string? Host, string? Origin, bool HasMark, int ContentLength, bool Valid)
    {
        public static Request Parse(string head)
        {
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                return new Request("", "", null, null, false, 0, false);
            string? host = null, origin = null;
            bool mark = false;
            int length = -1;
            foreach (var line in lines.AsSpan(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) host = value;
                else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) origin = value;
                else if (name.Equals(Header, StringComparison.OrdinalIgnoreCase)) mark = value == "1";
                else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    length = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -2;
            }
            return new Request(parts[0], parts[1], host, origin, mark, length, true);
        }
    }

    /// <summary>
    /// Who may talk to us: a POST to /focumon, to our own address (not a name someone pointed at 127.0.0.1),
    /// from a browser extension (never from a page), carrying our header (which a page can't add without asking first).
    /// </summary>
    internal static Verdict Judge(Request request, int port)
    {
        if (!request.Valid) return Verdict.Malformed;
        if (request.Method != "POST") return Verdict.WrongMethod;
        if (request.Target != RoutePath) return Verdict.Forbidden;
        if (request.Host != $"127.0.0.1:{port}" && request.Host != $"localhost:{port}") return Verdict.Forbidden;
        if (request.Origin == null || !(request.Origin.StartsWith("chrome-extension://", StringComparison.Ordinal)
                                       || request.Origin.StartsWith("moz-extension://", StringComparison.Ordinal))) return Verdict.Forbidden;
        if (!request.HasMark) return Verdict.Forbidden;
        if (request.ContentLength == -2 || request.ContentLength < 0) return Verdict.Malformed;
        return request.ContentLength > MaxBody ? Verdict.TooBig : Verdict.Allowed;
    }

    /// <summary>The headers, and the first bytes of the body if they came along.</summary>
    static async Task<(string? Head, byte[] Leftover)> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHead + MaxBody];
        int filled = 0;
        try
        {
            while (filled < MaxHead)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(filled, MaxHead - filled), ct).ConfigureAwait(false);
                if (read == 0) break;
                int searchFrom = Math.Max(0, filled - 3);
                filled += read;
                int end = buffer.AsSpan(searchFrom, filled - searchFrom).IndexOf("\r\n\r\n"u8);
                if (end >= 0)
                {
                    int headEnd = searchFrom + end;
                    return (Encoding.Latin1.GetString(buffer, 0, headEnd), buffer[(headEnd + 4)..filled]);
                }
            }
        }
        catch (OperationCanceledException) { }
        return (null, Array.Empty<byte>());
    }

    static Task ReplyAsync(NetworkStream stream, int status, string reason, CancellationToken ct)
        => stream.WriteAsync(Encoding.Latin1.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"), ct).AsTask();

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        _cts.Dispose();
    }
}
