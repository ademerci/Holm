using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace WinNotch;

/// <summary>Where a speed test is.</summary>
internal enum SpeedPhase { Ping, Download, Upload, Done }

/// <summary>
/// What a speed test has measured so far; null while a value isn't known yet. <paramref name="PublicIp"/> is the
/// address the test server saw, <paramref name="Server"/> the Cloudflare location that answered ("ZRH").
/// </summary>
internal sealed record SpeedProgress(SpeedPhase Phase, double? PingMs, double? DownMbps, double? UpMbps,
    string? PublicIp = null, string? Server = null);

/// <summary>The link to the router or the network: Wi-Fi with its name and signal, or a cable.</summary>
internal sealed record NetworkLink(bool IsWifi, string? Ssid, int? SignalBars, ulong LinkBitsPerSecond, Guid? AdapterId = null);

/// <summary>
/// How fast the internet is, measured against Cloudflare's public speed test (speed.cloudflare.com, no key, no
/// account): the ping first, then download and upload with a few connections at once for about 7 seconds each.
/// Only when you start it; nothing about you is sent besides the requests themselves, and the bytes are zeros.
/// A phase stops early after <see cref="MaxBytesPerPhase"/>, so a test uses at most ~600 MB on a very fast line.
/// </summary>
internal static class SpeedTest
{
    const string Server = "https://speed.cloudflare.com/";
    public static readonly TimeSpan PhaseTime = TimeSpan.FromSeconds(7);
    /// <summary>The first moments of a phase, while the connections speed up, don't count towards the result.</summary>
    public static readonly TimeSpan Warmup = TimeSpan.FromSeconds(1.5);
    public const long MaxBytesPerPhase = 300_000_000;
    const int Streams = 4, Pings = 8, DownChunk = 25_000_000, UpChunk = 8_000_000;

    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxConnectionsPerServer = Streams,
        AutomaticDecompression = DecompressionMethods.None,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    static SpeedTest() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Holm (+https://github.com/ademerci/Holm)");

    /// <summary>Ping, download, upload; <paramref name="progress"/> hears about each value as it changes.</summary>
    public static async Task<SpeedProgress> RunAsync(IProgress<SpeedProgress>? progress, CancellationToken cancel)
    {
        progress?.Report(new(SpeedPhase.Ping, null, null, null));
        var (ping, ip, server) = await PingAsync(cancel);
        progress?.Report(new(SpeedPhase.Download, ping, 0, null, ip, server));
        double down = await MeasureAsync(DownloadOnceAsync, live => progress?.Report(new(SpeedPhase.Download, ping, live, null, ip, server)), cancel);
        progress?.Report(new(SpeedPhase.Upload, ping, down, 0, ip, server));
        double up = await MeasureAsync(UploadOnceAsync, live => progress?.Report(new(SpeedPhase.Upload, ping, down, live, ip, server)), cancel);
        var done = new SpeedProgress(SpeedPhase.Done, ping, down, up, ip, server);
        progress?.Report(done);
        return done;
    }

    /// <summary>
    /// The median time for an empty answer, in ms. The first request opens the connection and doesn't count. The
    /// answers also say which address they came from (cf-meta-ip) and which Cloudflare location sent them (CF-RAY).
    /// </summary>
    static async Task<(double Ms, string? PublicIp, string? Server)> PingAsync(CancellationToken cancel)
    {
        var times = new List<double>();
        string? ip = null, server = null;
        for (int i = 0; i <= Pings; i++)
        {
            var clock = Stopwatch.StartNew();
            using var response = await Http.GetAsync(Server + "__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancel);
            clock.Stop();
            response.EnsureSuccessStatusCode();
            if (i > 0) times.Add(clock.Elapsed.TotalMilliseconds);
            if (response.Headers.TryGetValues("cf-meta-ip", out var ips)) ip = ips.FirstOrDefault() is { } a && IPAddress.TryParse(a, out _) ? a : ip;
            if (response.Headers.TryGetValues("cf-ray", out var rays)) server = ServerOf(rays.FirstOrDefault()) ?? server;
        }
        return (Median(times), ip, server);
    }

    /// <summary>"a5a57e1c9a8759fd-GVA" → "GVA": the airport code of the Cloudflare location that answered.</summary>
    public static string? ServerOf(string? ray)
    {
        int dash = ray?.LastIndexOf('-') ?? -1;
        if (ray == null || dash < 0 || dash == ray.Length - 1) return null;
        string code = ray[(dash + 1)..].Trim();
        return code.Length is >= 3 and <= 4 && code.All(char.IsAsciiLetterUpper) ? code : null;
    }

    static async Task DownloadOnceAsync(Counter counter, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(Server + "__down?bytes=" + DownChunk, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0) counter.Add(read);
    }

    static async Task UploadOnceAsync(Counter counter, CancellationToken cancel)
    {
        using var content = new ZeroContent(UpChunk, counter);
        using var response = await Http.PostAsync(Server + "__up", content, cancel);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Runs <paramref name="once"/> on a few connections over and over until the phase is up, and returns Mbps after
    /// the warm-up. <paramref name="live"/> gets the speed so far about four times a second.
    /// </summary>
    static async Task<double> MeasureAsync(Func<Counter, CancellationToken, Task> once, Action<double> live, CancellationToken cancel)
    {
        var counter = new Counter();
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        phase.CancelAfter(PhaseTime);
        var clock = Stopwatch.StartNew();
        var all = Task.WhenAll(Enumerable.Range(0, Streams).Select(_ => LoopAsync(once, counter, phase.Token)));
        long warmBytes = -1;
        TimeSpan warmTime = TimeSpan.Zero;
        while (!phase.IsCancellationRequested && !all.IsCompleted)
        {
            await Task.WhenAny(all, Task.Delay(250, phase.Token)).ConfigureAwait(false);
            if (warmBytes < 0 && clock.Elapsed >= Warmup) { warmBytes = counter.Bytes; warmTime = clock.Elapsed; }
            if (counter.Bytes >= MaxBytesPerPhase) phase.Cancel();
            live(Rate(counter.Bytes, clock.Elapsed, warmBytes, warmTime));
        }
        long bytes = counter.Bytes;
        TimeSpan time = clock.Elapsed;
        phase.Cancel();
        await all.ConfigureAwait(false); // the loops never throw
        cancel.ThrowIfCancellationRequested();
        if (bytes == 0) throw counter.Error ?? new HttpRequestException("No data came through.");
        return Rate(bytes, time, warmBytes, warmTime);
    }

    static async Task LoopAsync(Func<Counter, CancellationToken, Task> once, Counter counter, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try { await once(counter, cancel).ConfigureAwait(false); }
            catch (Exception) when (cancel.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                counter.Error ??= ex; // this connection gives up; the others may go on
                return;
            }
        }
    }

    /// <summary>Mbps since the warm-up, or since the start while it's still warming up (or never got past it).</summary>
    public static double Rate(long bytes, TimeSpan time, long warmBytes, TimeSpan warmTime)
        => warmBytes >= 0 && time - warmTime >= TimeSpan.FromSeconds(1)
            ? Mbps(bytes - warmBytes, time - warmTime)
            : Mbps(bytes, time);

    public static double Mbps(long bytes, TimeSpan time)
        => time <= TimeSpan.Zero || bytes <= 0 ? 0 : bytes * 8 / time.TotalSeconds / 1_000_000;

    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>"940", "87.4", "3.21": three significant digits, never fewer than a whole number.</summary>
    public static string FormatMbps(double mbps) => mbps switch
    {
        >= 100 => Math.Round(mbps).ToString("0", CultureInfo.InvariantCulture),
        >= 10 => mbps.ToString("0.0", CultureInfo.InvariantCulture),
        _ => mbps.ToString("0.00", CultureInfo.InvariantCulture),
    };

    public static string FormatPing(double ms) => ms < 10 ? ms.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(ms).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>"Wi-Fi · Home · 4 of 5 bars · link 866 Mbps", "Ethernet · link 1 Gbps", or "" when nothing is known.</summary>
    public static string DescribeLink(NetworkLink? link)
    {
        if (link == null) return "";
        var parts = new List<string> { link.IsWifi ? "Wi-Fi" : "Ethernet" };
        if (link.IsWifi && !string.IsNullOrWhiteSpace(link.Ssid)) parts.Add(link.Ssid.Trim());
        if (link.IsWifi && link.SignalBars is { } bars) parts.Add($"{bars} of 5 bars");
        if (link.LinkBitsPerSecond > 0) parts.Add("link " + FormatLinkSpeed(link.LinkBitsPerSecond));
        return string.Join(" · ", parts);
    }

    /// <summary>"866 Mbps", "1 Gbps": 866.7 shows as 866, as in Windows.</summary>
    public static string FormatLinkSpeed(ulong bitsPerSecond)
    {
        double mbps = bitsPerSecond / 1_000_000.0;
        return mbps >= 1000 && mbps % 1000 == 0
            ? (mbps / 1000).ToString("0", CultureInfo.InvariantCulture) + " Gbps"
            : Math.Floor(mbps).ToString("0", CultureInfo.InvariantCulture) + " Mbps";
    }

    /// <summary>
    /// The connection to the internet, as Windows reports it. The Wi-Fi name may be missing when Windows' location
    /// permission is off for desktop apps (Windows 11 24H2 and later).
    /// </summary>
    public static NetworkLink? CurrentLink()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) return null;
            ulong bits = profile.NetworkAdapter?.InboundMaxBitsPerSecond ?? 0;
            Guid? adapter = profile.NetworkAdapter?.NetworkAdapterId;
            if (!profile.IsWlanConnectionProfile) return new NetworkLink(false, null, null, bits, adapter);
            string? ssid = null;
            try { ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
            int? signal = profile.GetSignalBars() is { } b ? b : null;
            return new NetworkLink(true, ssid, signal, bits, adapter);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidCastException)
        {
            Diag.Log("Network link: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The bytes a phase has moved so far, across its connections.</summary>
    sealed class Counter
    {
        long _bytes;
        public long Bytes => Interlocked.Read(ref _bytes);
        public void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);
        public Exception? Error;
    }

    /// <summary>An upload body of zeros that counts what has gone out.</summary>
    sealed class ZeroContent : HttpContent
    {
        static readonly byte[] Zeros = new byte[64 * 1024];
        readonly long _length;
        readonly Counter _counter;

        public ZeroContent(long length, Counter counter)
        {
            _length = length;
            _counter = counter;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancel)
        {
            for (long left = _length; left > 0;)
            {
                int n = (int)Math.Min(left, Zeros.Length);
                await stream.WriteAsync(Zeros.AsMemory(0, n), cancel).ConfigureAwait(false);
                _counter.Add(n);
                left -= n;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
