using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>A GIF ready to play: its frames and how long each one shows.</summary>
internal sealed class GifAnimation
{
    public GifAnimation(IReadOnlyList<Bitmap> frames, IReadOnlyList<TimeSpan> delays)
    {
        Frames = frames;
        Delays = delays;
    }

    public IReadOnlyList<Bitmap> Frames { get; }
    public IReadOnlyList<TimeSpan> Delays { get; }
    public double Width => Frames[0].PixelSize.Width;
    public double Height => Frames[0].PixelSize.Height;
    public TimeSpan Duration => Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b);
    /// <summary>The memory its frames take.</summary>
    public long Bytes => Frames.Sum(f => (long)f.PixelSize.Width * f.PixelSize.Height * 4);
}

/// <summary>
/// Checks a GIF's structure before the decoder gets it: every block is walked and measured here, in safe code,
/// so a broken or hostile file (sizes that don't fit, a cut-off end, thousands of frames, an absurd canvas) is cut
/// back to its good frames or turned away instead of reaching native code that could take Holm down with it.
/// </summary>
internal static class GifInspector
{
    public const int MaxSide = 2048;
    public const int MaxFrames = 1000;
    public const long MaxWork = 120_000_000;

    public sealed record Result(int Width, int Height, int Frames, byte[] Data);

    public static Result? Check(byte[] data)
    {
        try { return Walk(data); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException) { return null; }
    }

    static Result? Walk(byte[] data)
    {
        if (!GifDecoder.IsGif(data) || data.Length < 13) return null;
        int width = U16(data, 6), height = U16(data, 8);
        int at = 13;
        byte packed = data[10];
        if ((packed & 0x80) != 0) at += 3 << ((packed & 7) + 1);
        if (at > data.Length) return null;

        int frames = 0, goodEnd = -1, maxRight = 0, maxBottom = 0;
        bool clean = false;
        while (at < data.Length && frames < MaxFrames)
        {
            byte block = data[at];
            if (block == 0x3B) { clean = true; break; }
            if (block == 0x21)
            {
                if (at + 2 > data.Length || SkipSubBlocks(data, at + 2) is not int next) break;
                at = next;
                continue;
            }
            if (block != 0x2C || at + 10 > data.Length) break;
            int left = U16(data, at + 1), top = U16(data, at + 3), w = U16(data, at + 5), h = U16(data, at + 7);
            byte local = data[at + 9];
            if (w == 0 || h == 0 || w > MaxSide || h > MaxSide) break;
            at += 10;
            if ((local & 0x80) != 0) at += 3 << ((local & 7) + 1);
            if (at >= data.Length) break;
            int codeSize = data[at];
            if (codeSize is < 1 or > 11) break;
            if (SkipSubBlocks(data, at + 1) is not int end) break;
            at = end;
            frames++;
            goodEnd = at;
            maxRight = Math.Max(maxRight, Math.Min(MaxSide, left + w));
            maxBottom = Math.Max(maxBottom, Math.Min(MaxSide, top + h));
            if (width == 0 || height == 0) { width = maxRight; height = maxBottom; }
            if ((long)frames * Math.Max(1, width) * Math.Max(1, height) >= MaxWork) break;
        }
        if (frames == 0) return null;
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide) return null;
        if (clean && at == goodEnd && frames < MaxFrames) return new Result(width, height, frames, data);
        var trimmed = new byte[goodEnd + 1];
        Buffer.BlockCopy(data, 0, trimmed, 0, goodEnd);
        trimmed[goodEnd] = 0x3B;
        return new Result(width, height, frames, trimmed);
    }

    static int? SkipSubBlocks(byte[] data, int at)
    {
        while (true)
        {
            if (at >= data.Length) return null;
            int size = data[at];
            at += 1 + size;
            if (size == 0) return at;
        }
    }

    static int U16(byte[] data, int at) => data[at] | data[at + 1] << 8;
}

/// <summary>
/// Turns GIF files into frames with SkiaSharp (<see cref="SKCodec"/>), respecting frame disposal, delays and a
/// strict per-GIF memory cap.
/// </summary>
internal static class GifDecoder
{
    public const int MaxFrames = 120;
    public const long MaxBytes = 16L * 1024 * 1024;

    public static bool IsGif(ReadOnlySpan<byte> data)
        => data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';

    /// <summary>How long a frame shows; like browsers, too short means a tenth of a second.</summary>
    public static TimeSpan DelayMs(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds <= 10 ? 100 : milliseconds);

    public static int Step(int count, int max) => count <= max ? 1 : (count + max - 1) / max;

    public static GifAnimation? Decode(byte[] data, int maxSide = 320, int maxFrames = MaxFrames)
    {
        if (GifInspector.Check(data) is not { } checkedGif) return null;
        try
        {
            using var skData = SKData.CreateCopy(checkedGif.Data);
            using var codec = SKCodec.Create(skData);
            if (codec == null) return null;

            int width = checkedGif.Width, height = checkedGif.Height;
            var frameInfos = codec.FrameInfo;
            int rawCount = Math.Max(codec.FrameCount, frameInfos.Length);
            if (rawCount == 0) rawCount = 1; // single-frame GIFs can report FrameCount == 0 with FrameInfo empty
            int count = Math.Min(rawCount, checkedGif.Frames);

            maxSide = Math.Clamp(maxSide, 1, 1024);
            double scale = Math.Min(1.0, maxSide / (double)Math.Max(width, height));
            int outW = Math.Max(1, (int)Math.Round(width * scale)), outH = Math.Max(1, (int)Math.Round(height * scale));
            maxFrames = Math.Clamp(maxFrames, 1, MaxFrames);
            if (maxFrames == 1) count = 1;
            int budget = (int)Math.Max(1, MaxBytes / ((long)outW * outH * 4));
            int step = Step(count, Math.Min(maxFrames, budget));

            int stride = width * 4;
            var canvas = new byte[stride * height];
            var frames = new List<Bitmap>();
            var delays = new List<TimeSpan>();
            var pending = TimeSpan.Zero;
            var imgInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            // Skia puts each frame together over the one it says it builds on (its RequiredFrame), and does what the
            // frame before asked for once it was shown. That is usually the frame just before; after a "restore previous"
            // frame it is an earlier one, which is kept here until then.
            var needed = new HashSet<int>();
            for (int i = 1; i < count && i < frameInfos.Length; i++)
                if (frameInfos[i].RequiredFrame >= 0 && frameInfos[i].RequiredFrame != i - 1) needed.Add(frameInfos[i].RequiredFrame);
            var kept = new Dictionary<int, byte[]>();

            var handle = GCHandle.Alloc(canvas, GCHandleType.Pinned);
            try
            {
                IntPtr canvasPtr = handle.AddrOfPinnedObject();
                for (int i = 0; i < count; i++)
                {
                    var fi = i < frameInfos.Length ? frameInfos[i] : default;
                    int required = i < frameInfos.Length ? fi.RequiredFrame : -1;
                    SKCodecOptions options;
                    if (i == 0 || required < 0)
                    {
                        Array.Clear(canvas);
                        options = new SKCodecOptions(i);
                    }
                    else if (required == i - 1) options = new SKCodecOptions(i, i - 1);
                    else
                    {
                        if (!kept.TryGetValue(required, out var before)) break;
                        Buffer.BlockCopy(before, 0, canvas, 0, canvas.Length);
                        options = new SKCodecOptions(i, required);
                    }
                    var res = codec.GetPixels(imgInfo, canvasPtr, options);
                    if (res is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) break;
                    if (needed.Contains(i)) kept[i] = (byte[])canvas.Clone();

                    pending += DelayMs(fi.Duration);
                    if (i % step != 0 && i != count - 1) continue;
                    frames.Add(Freeze(canvas, width, height, outW, outH));
                    delays.Add(pending);
                    pending = TimeSpan.Zero;
                }
            }
            finally { handle.Free(); }

            if (frames.Count == 0) return null;
            if (frames.Count > 1 && pending > TimeSpan.Zero) delays[^1] += pending;
            return new GifAnimation(frames, delays);
        }
        catch (Exception ex) when (ex is not (StackOverflowException or AccessViolationException))
        {
            Diag.Log("GIF unreadable: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    static WriteableBitmap Freeze(byte[] canvas, int width, int height, int outW, int outH)
    {
        byte[] pixels = outW == width && outH == height ? canvas : Shrink(canvas, width, height, outW, outH);
        var bitmap = new WriteableBitmap(new PixelSize(outW, outH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        for (int y = 0; y < outH; y++)
            Marshal.Copy(pixels, y * outW * 4, frame.Address + y * frame.RowBytes, outW * 4);
        return bitmap;
    }

    /// <summary>Averages each block of canvas pixels into one; premultiplied, so clear pixels add nothing.</summary>
    internal static byte[] Shrink(byte[] canvas, int width, int height, int outW, int outH)
    {
        var result = new byte[outW * outH * 4];
        Span<int> sum = stackalloc int[4];
        for (int oy = 0; oy < outH; oy++)
        {
            int y0 = oy * height / outH, y1 = Math.Max(y0 + 1, (oy + 1) * height / outH);
            for (int ox = 0; ox < outW; ox++)
            {
                int x0 = ox * width / outW, x1 = Math.Max(x0 + 1, (ox + 1) * width / outW);
                sum.Clear();
                for (int y = y0; y < y1; y++)
                    for (int i = (y * width + x0) * 4, end = (y * width + x1) * 4; i < end; i += 4)
                    {
                        sum[0] += canvas[i]; sum[1] += canvas[i + 1]; sum[2] += canvas[i + 2]; sum[3] += canvas[i + 3];
                    }
                int n = (y1 - y0) * (x1 - x0), o = (oy * outW + ox) * 4;
                for (int c = 0; c < 4; c++) result[o + c] = (byte)((sum[c] + n / 2) / n);
            }
        }
        return result;
    }
}

/// <summary>
/// GIFs from links: fetched once (https only, at most <see cref="MaxDownload"/>, and only if it really is a GIF), kept
/// in a cache folder next to the settings and decoded in the background. Decoded GIFs stay in memory for a while.
/// </summary>
internal static class GifCache
{
    public const int MaxDownload = 10 * 1024 * 1024;
    const int KeepFiles = 150, KeepDecoded = 24, MaxRedirects = 3;
    const long KeepBytes = 96L * 1024 * 1024;
    static readonly SemaphoreSlim Decoding = new(2);

    static readonly HttpClient Http = new(SecurityGuards.CreateSafeHttpHandler(allowRedirects: false))
    {
        Timeout = TimeSpan.FromSeconds(25),
        DefaultRequestHeaders = { { "User-Agent", "Holm-Chat/1" } },
    };

    static readonly Dictionary<string, Task<GifAnimation?>> Loading = new();
    static readonly LinkedList<string> Order = new();
    static readonly object Gate = new();

    internal static bool Offline { get; set; }

    static string Folder => Path.Combine(Diag.Folder, "gifs");

    static string FileFor(string url) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ".gif");

    public static Task<GifAnimation?> LoadAsync(string url, int maxSide = 320, int maxFrames = GifDecoder.MaxFrames)
    {
        string key = maxSide + " " + maxFrames + " " + url;
        lock (Gate)
        {
            if (Loading.TryGetValue(key, out var task) && !(task.IsCompleted && task.Result == null))
            {
                Order.Remove(key);
                Order.AddFirst(key);
                return task;
            }
            task = Task.Run(() => FetchAndDecodeAsync(url, maxSide, maxFrames));
            Loading[key] = task;
            Order.Remove(key);
            Order.AddFirst(key);
            Trim();
            task.ContinueWith(_ => { lock (Gate) Trim(); }, TaskScheduler.Default);
            return task;
        }
    }

    static void Trim()
    {
        while (Order.Count > 1 && (Order.Count > KeepDecoded || KeptBytes() > KeepBytes))
        {
            Loading.Remove(Order.Last!.Value);
            Order.RemoveLast();
        }
    }

    public static void ReleaseDecoded()
    {
        lock (Gate)
        {
            foreach (string key in Loading.Where(p => p.Value.IsCompleted).Select(p => p.Key).ToList())
            {
                Loading.Remove(key);
                Order.Remove(key);
            }
        }
    }

    static long KeptBytes()
        => Loading.Values.Where(t => t.IsCompletedSuccessfully && t.Result != null).Sum(t => t.Result!.Bytes);

    static async Task<GifAnimation?> FetchAndDecodeAsync(string url, int maxSide, int maxFrames)
    {
        try
        {
            byte[]? data = await BytesAsync(url).ConfigureAwait(false);
            if (data == null) return null;
            await Decoding.WaitAsync().ConfigureAwait(false);
            try
            {
                var gif = GifDecoder.Decode(data, maxSide, maxFrames);
                if (gif == null) Forget(url);
                return gif;
            }
            finally { Decoding.Release(); }
        }
        catch (Exception ex) when (ex is not (StackOverflowException or AccessViolationException))
        {
            Diag.Log("GIF not loaded: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    static void Forget(string url)
    {
        if (ChatGif.Clean(url) is not { } clean || AppSettings.ReadOnly || Offline) return;
        try { File.Delete(FileFor(clean)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static async Task<byte[]?> BytesAsync(string url)
    {
        if (ChatGif.Clean(url) is not { } clean || Offline) return null;
        string file = FileFor(clean);
        try
        {
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length <= MaxDownload)
                {
                    byte[] cached = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                    if (GifDecoder.IsGif(cached))
                    {
                        if (!AppSettings.ReadOnly) File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
                        return cached;
                    }
                }
                Forget(clean);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        try
        {
            HttpResponseMessage? response = null;
            try
            {
                string current = clean;
                for (int hop = 0; hop <= MaxRedirects; hop++)
                {
                    if (ChatGif.Clean(current) is not { } hopUrl) return null;
                    response = await Http.GetAsync(hopUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                    {
                        var nextUri = location.IsAbsoluteUri ? location : new Uri(new Uri(hopUrl), location);
                        response.Dispose();
                        response = null;
                        if (hop == MaxRedirects) return null;
                        if (ChatGif.Clean(nextUri.AbsoluteUri) is not { } nextClean) return null;
                        current = nextClean;
                        continue;
                    }
                    break;
                }
                if (response == null || !response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) return null;
                if (ChatGif.Clean(response.RequestMessage?.RequestUri?.AbsoluteUri) == null) return null;
                if (response.Content.Headers.ContentLength > MaxDownload) return null;
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (memory.Length > MaxDownload) return null;
                }
                byte[] data = memory.ToArray();
                if (!GifDecoder.IsGif(data)) return null;
                Save(file, data);
                return data;
            }
            finally { response?.Dispose(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException or UriFormatException)
        {
            Diag.Log("GIF not loaded: " + ex.Message);
            return null;
        }
    }

    static void Save(string file, byte[] data)
    {
        if (AppSettings.ReadOnly) return;
        try
        {
            SecurityGuards.EnsurePrivateDirectory(Folder);
            SecurityGuards.WritePrivateFile(file, data);
            var old = new DirectoryInfo(Folder).GetFiles("*.gif").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepFiles);
            foreach (var f in old) f.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Diag.Log("GIF not cached: " + ex.Message); }
    }
}

/// <summary>Plays a <see cref="GifAnimation"/> on an image, and stops it when out of sight or the window isn't active.</summary>
internal static class GifPlayer
{
    static readonly TimeSpan MinFrameDelay = TimeSpan.FromMilliseconds(40); // 25 fps cap

    public static void Play(Image image, GifAnimation gif, bool animate = true, Control? hover = null)
    {
        if (gif.Frames.Count == 0) return;
        image.Source = gif.Frames[0];
        if (!animate || gif.Frames.Count < 2 || gif.Frames.Count != gif.Delays.Count || gif.Duration <= TimeSpan.Zero) return;

        int index = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Render);
        Window? window = TopLevel.GetTopLevel(image) as Window;
        // On screen only once it's in a window: a bubble rebuilt while its GIF was loading still has a parent but is
        // never shown, and a clock started for it would tick forever.
        bool attached = window != null;

        void Update()
        {
            bool want = attached && image.IsEffectivelyVisible
                     && (hover == null || hover.IsPointerOver)
                     && window?.IsActive != false;
            if (want == timer.IsEnabled) return;
            if (want)
            {
                var delay = gif.Delays[index];
                timer.Interval = delay < MinFrameDelay ? MinFrameDelay : delay;
                timer.Start();
            }
            else timer.Stop();
        }

        timer.Tick += (_, _) =>
        {
            index = (index + 1) % gif.Frames.Count;
            image.Source = gif.Frames[index];
            var delay = gif.Delays[index];
            timer.Interval = delay < MinFrameDelay ? MinFrameDelay : delay;
        };

        void Focus(object? sender, EventArgs e) => Update();
        image.AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            window = TopLevel.GetTopLevel(image) as Window;
            if (window != null) { window.Activated += Focus; window.Deactivated += Focus; }
            Update();
        };
        image.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (window != null) { window.Activated -= Focus; window.Deactivated -= Focus; }
            window = null;
            timer.Stop();
        };
        image.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) Update();
        };
        if (hover != null)
        {
            hover.PointerEntered += (_, _) => Update();
            hover.PointerExited += (_, _) => Update();
        }
        if (window != null) { window.Activated += Focus; window.Deactivated += Focus; }
        Update();
    }

    /// <summary>An animated timer for a notification thumbnail brush; returns a handle that stops it.</summary>
    public static Action PlayBrush(ImageBrush brush, GifAnimation gif)
    {
        if (gif.Frames.Count < 2 || gif.Frames.Count != gif.Delays.Count || gif.Duration <= TimeSpan.Zero) return () => { };
        int index = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = gif.Delays[0] < MinFrameDelay ? MinFrameDelay : gif.Delays[0],
        };
        timer.Tick += (_, _) =>
        {
            index = (index + 1) % gif.Frames.Count;
            brush.Source = gif.Frames[index];
            var delay = gif.Delays[index];
            timer.Interval = delay < MinFrameDelay ? MinFrameDelay : delay;
        };
        timer.Start();
        return () => timer.Stop();
    }

    public static Control View(string url, double width, double height, double radius, bool animate = true, int maxSide = 320, bool fitHeight = false,
                               bool hoverOnly = false, int maxFrames = GifDecoder.MaxFrames)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, Width = width, Height = height };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.LowQuality);
        var label = new TextBlock
        {
            Text = "GIF", FontSize = Math.Max(9, Math.Min(13, height / 5)), FontWeight = FontWeight.Bold,
            Foreground = HubTheme.Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(label);
        grid.Children.Add(image);
        var host = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = HubTheme.CardStrong, ClipToBounds = true,
            Child = grid,
        };
        _ = LoadInto(image, label, url, animate, maxSide, animate ? maxFrames : 1, hoverOnly ? host : null, gif =>
        {
            if (!fitHeight) return;
            double fitted = Math.Clamp(width * gif.Height / Math.Max(1, gif.Width), width / 3, width * 1.4);
            host.Height = grid.Height = image.Height = fitted;
        });
        return host;
    }

    static async Task LoadInto(Image image, TextBlock label, string url, bool animate, int maxSide, int maxFrames, Control? hover, Action<GifAnimation> loaded)
    {
        try
        {
            var gif = await GifCache.LoadAsync(url, maxSide, maxFrames);
            if (gif == null || gif.Frames.Count == 0)
            {
                label.Text = "GIF unavailable";
                label.FontSize = 11;
                return;
            }
            label.IsVisible = false;
            loaded(gif);
            Play(image, gif, animate, hover);
        }
        catch (Exception ex) when (ex is not (StackOverflowException or AccessViolationException))
        {
            Diag.Log("GIF not shown: " + ex.GetType().Name + ": " + ex.Message);
            image.Source = null;
            label.IsVisible = true;
            label.Text = "GIF unavailable";
        }
    }
}

internal sealed record GifResult(string Preview, string Url, double Width, double Height);

/// <summary>
/// Searching GIFs on GIPHY. It needs an API key: one built into Holm (<see cref="BuiltInKey"/>) or one you add in
/// the GIF picker. Without a key, GIF links can still be pasted and sent.
/// </summary>
internal static class GifSearch
{
    public static readonly string BuiltInKey = typeof(GifSearch).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GiphyKey")?.Value?.Trim() ?? "";

    static readonly HttpClient Http = new(SecurityGuards.CreateSafeHttpHandler(allowRedirects: false)) { Timeout = TimeSpan.FromSeconds(12), DefaultRequestHeaders = { { "User-Agent", "Holm-Chat/1" } } };

    public static string? Key(AppSettings settings)
        => !string.IsNullOrWhiteSpace(settings.GifSearchKey) ? settings.GifSearchKey.Trim() : BuiltInKey.Length > 0 ? BuiltInKey : null;

    public static async Task<List<GifResult>> SearchAsync(string key, string? words, CancellationToken ct)
    {
        string q = (words ?? "").Trim();
        string url = q.Length == 0
            ? $"https://api.giphy.com/v1/gifs/trending?api_key={Uri.EscapeDataString(key)}&limit=24&rating=pg-13"
            : $"https://api.giphy.com/v1/gifs/search?api_key={Uri.EscapeDataString(key)}&q={Uri.EscapeDataString(q)}&limit=24&rating=pg-13";
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if ((int)response.StatusCode is 401 or 403) throw new IOException("GIPHY didn't accept the key");
            if (!response.IsSuccessStatusCode) throw new IOException($"GIPHY answered {(int)response.StatusCode}");
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex) { throw new IOException("GIPHY can't be reached", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new IOException("GIPHY took too long", ex); }
    }

    internal static List<GifResult> Parse(string json)
    {
        var results = new List<GifResult>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return results;
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("images", out var images)) continue;
                string? send = Url(images, "fixed_height") ?? Url(images, "downsized");
                string? preview = Url(images, "fixed_height_small") ?? send;
                if (ChatGif.Clean(send) is not { } clean || ChatGif.Clean(preview) is not { } small) continue;
                double w = Number(images, "fixed_height_small", "width"), h = Number(images, "fixed_height_small", "height");
                results.Add(new GifResult(small, clean, w > 0 ? w : 100, h > 0 ? h : 100));
            }
        }
        catch (JsonException) { }
        return results;
    }

    static string? Url(JsonElement images, string name)
        => images.TryGetProperty(name, out var image) && image.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;

    static double Number(JsonElement images, string name, string field)
        => images.TryGetProperty(name, out var image) && image.TryGetProperty(field, out var value)
           && double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : 0;
}
