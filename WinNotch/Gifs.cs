using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>A GIF ready to play: its frames (frozen, so any thread may use them) and how long each one shows.</summary>
internal sealed class GifAnimation
{
    public GifAnimation(IReadOnlyList<BitmapSource> frames, IReadOnlyList<TimeSpan> delays)
    {
        Frames = frames;
        Delays = delays;
    }

    public IReadOnlyList<BitmapSource> Frames { get; }
    public IReadOnlyList<TimeSpan> Delays { get; }
    public double Width => Frames[0].PixelWidth;
    public double Height => Frames[0].PixelHeight;
    public TimeSpan Duration => Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b);
}

/// <summary>
/// Turns GIF files into frames. WPF shows only the first frame of a GIF, so the frames are put together here the way a
/// browser does: each one drawn over the last at its place, with what the GIF says to do with the previous one.
/// </summary>
internal static class GifDecoder
{
    /// <summary>The most frames kept; longer GIFs keep every second (third, …) frame and show it as long.</summary>
    public const int MaxFrames = 120;
    /// <summary>The most memory the frames of one GIF may take, in bytes.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    public static bool IsGif(ReadOnlySpan<byte> data)
        => data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';

    /// <summary>How long a frame shows, from its delay in hundredths of a second; like browsers, too short means a tenth.</summary>
    public static TimeSpan Delay(int hundredths) => TimeSpan.FromMilliseconds(hundredths <= 1 ? 100 : hundredths * 10);

    /// <summary>Which of <paramref name="count"/> frames to keep so that at most <paramref name="max"/> remain, evenly.</summary>
    public static int Step(int count, int max) => count <= max ? 1 : (count + max - 1) / max;

    /// <param name="maxSide">The frames are made smaller so the longer side is at most this many pixels.</param>
    /// <returns>The animation, or null if the data isn't a GIF that can be read.</returns>
    public static GifAnimation? Decode(byte[] data, int maxSide = 320)
    {
        if (!IsGif(data)) return null;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            int width = Query<ushort>(decoder.Metadata, "/logscrdesc/Width") is ushort w and > 0 ? w : decoder.Frames[0].PixelWidth;
            int height = Query<ushort>(decoder.Metadata, "/logscrdesc/Height") is ushort h and > 0 ? h : decoder.Frames[0].PixelHeight;
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            double scale = Math.Min(1.0, maxSide / (double)Math.Max(width, height));
            int outW = Math.Max(1, (int)Math.Round(width * scale)), outH = Math.Max(1, (int)Math.Round(height * scale));
            int count = decoder.Frames.Count;
            int budget = (int)Math.Max(1, MaxBytes / ((long)outW * outH * 4));
            int step = Step(count, Math.Min(MaxFrames, budget));

            int stride = width * 4;
            var canvas = new byte[stride * height];
            byte[]? saved = null;
            var frames = new List<BitmapSource>();
            var delays = new List<TimeSpan>();
            var pending = TimeSpan.Zero;
            (int X, int Y, int W, int H, int Disposal) previous = (0, 0, 0, 0, 0);

            for (int i = 0; i < count; i++)
            {
                var frame = decoder.Frames[i];
                var meta = frame.Metadata as BitmapMetadata;
                int left = Query<ushort>(meta, "/imgdesc/Left") ?? 0;
                int top = Query<ushort>(meta, "/imgdesc/Top") ?? 0;
                int disposal = Query<byte>(meta, "/grctlext/Disposal") ?? 0;
                var delay = Delay(Query<ushort>(meta, "/grctlext/Delay") ?? 10);

                // What the frame before asked for once it was shown: clear its place, or put back what was there.
                if (previous.Disposal == 2) Clear(canvas, stride, width, height, previous.X, previous.Y, previous.W, previous.H);
                else if (previous.Disposal == 3 && saved != null) Buffer.BlockCopy(saved, 0, canvas, 0, canvas.Length);
                if (disposal == 3) saved = (byte[])canvas.Clone();

                var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int fw = pixels.PixelWidth, fh = pixels.PixelHeight;
                var buffer = new byte[fw * fh * 4];
                pixels.CopyPixels(buffer, fw * 4, 0);
                Draw(canvas, stride, width, height, buffer, fw, fh, left, top);
                previous = (left, top, fw, fh, disposal);

                pending += delay;
                if (i % step != 0 && i != count - 1) continue;
                frames.Add(Freeze(canvas, width, height, stride, scale));
                delays.Add(pending);
                pending = TimeSpan.Zero;
            }
            if (frames.Count > 1 && pending > TimeSpan.Zero) delays[^1] += pending;
            return new GifAnimation(frames, delays);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                   or IOException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            Diag.Log("GIF unreadable: " + ex.Message);
            return null;
        }
    }

    static T? Query<T>(BitmapMetadata? meta, string query) where T : struct
    {
        try { return meta?.GetQuery(query) is T value ? value : null; }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>Draws a frame's pixels over the canvas; GIF pixels are either seen or not, so the seen ones replace.</summary>
    internal static void Draw(byte[] canvas, int stride, int width, int height, byte[] pixels, int fw, int fh, int left, int top)
    {
        for (int y = 0; y < fh; y++)
        {
            int cy = top + y;
            if (cy < 0 || cy >= height) continue;
            for (int x = 0; x < fw; x++)
            {
                int cx = left + x;
                if (cx < 0 || cx >= width) continue;
                int src = (y * fw + x) * 4;
                if (pixels[src + 3] == 0) continue;
                Buffer.BlockCopy(pixels, src, canvas, cy * stride + cx * 4, 4);
            }
        }
    }

    internal static void Clear(byte[] canvas, int stride, int width, int height, int left, int top, int w, int h)
    {
        for (int y = Math.Max(0, top); y < Math.Min(height, top + h); y++)
        {
            int from = Math.Max(0, left), to = Math.Min(width, left + w);
            if (to > from) Array.Clear(canvas, y * stride + from * 4, (to - from) * 4);
        }
    }

    static BitmapSource Freeze(byte[] canvas, int width, int height, int stride, double scale)
    {
        // Bgra32 with fully seen or fully clear pixels is the same as premultiplied, which WPF draws fastest.
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, canvas, stride);
        if (scale < 1)
        {
            var smaller = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            image = new CachedBitmap(smaller, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        image.Freeze();
        return image;
    }
}

/// <summary>
/// GIFs from links: fetched once (https only, at most <see cref="MaxDownload"/>, and only if it really is a GIF), kept
/// in a cache folder next to the settings and decoded in the background. Decoded GIFs stay in memory for a while.
/// </summary>
internal static class GifCache
{
    public const int MaxDownload = 10 * 1024 * 1024;
    const int KeepFiles = 150, KeepDecoded = 24;

    static readonly HttpClient Http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10), AllowAutoRedirect = true, MaxAutomaticRedirections = 3 })
    {
        Timeout = TimeSpan.FromSeconds(25),
        DefaultRequestHeaders = { { "User-Agent", "Holm-Chat/1" } },
    };

    static readonly Dictionary<string, Task<GifAnimation?>> Loading = new();
    static readonly LinkedList<string> Order = new();
    static readonly object Gate = new();

    /// <summary>For snapshots and tests: nothing goes to the disk or the network.</summary>
    internal static bool Offline;

    static string Folder => Path.Combine(Diag.Folder, "gifs");

    static string FileFor(string url) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ".gif");

    /// <summary>The GIF behind a link, decoded with its longer side at most <paramref name="maxSide"/>; null if it can't be had.</summary>
    public static Task<GifAnimation?> LoadAsync(string url, int maxSide = 320)
    {
        string key = maxSide + " " + url;
        lock (Gate)
        {
            if (Loading.TryGetValue(key, out var task) && !(task.IsCompleted && task.Result == null))
            {
                Order.Remove(key);
                Order.AddFirst(key);
                return task;
            }
            task = Task.Run(() => FetchAndDecodeAsync(url, maxSide));
            Loading[key] = task;
            Order.Remove(key); // tried before and failed
            Order.AddFirst(key);
            while (Order.Count > KeepDecoded)
            {
                Loading.Remove(Order.Last!.Value);
                Order.RemoveLast();
            }
            return task;
        }
    }

    static async Task<GifAnimation?> FetchAndDecodeAsync(string url, int maxSide)
    {
        byte[]? data = await BytesAsync(url).ConfigureAwait(false);
        return data == null ? null : GifDecoder.Decode(data, maxSide);
    }

    static async Task<byte[]?> BytesAsync(string url)
    {
        if (ChatGif.Clean(url) is not { } clean || Offline) return null;
        string file = FileFor(clean);
        try
        {
            if (File.Exists(file))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
                return await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        try
        {
            using var response = await Http.GetAsync(clean, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) return null;
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
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
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(file, data);
            var old = new DirectoryInfo(Folder).GetFiles("*.gif").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepFiles);
            foreach (var f in old) f.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Log("GIF not cached: " + ex.Message); }
    }
}

/// <summary>Plays a <see cref="GifAnimation"/> on an image or a brush, and stops it when that leaves the screen.</summary>
internal static class GifPlayer
{
    /// <summary>A GIF plays smoothly at this rate; more would only cost CPU.</summary>
    const int FrameRate = 25;

    public static ObjectAnimationUsingKeyFrames Animation(GifAnimation gif)
    {
        var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = gif.Duration };
        var at = TimeSpan.Zero;
        for (int i = 0; i < gif.Frames.Count; i++)
        {
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(gif.Frames[i], KeyTime.FromTimeSpan(at)));
            at += gif.Delays[i];
        }
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        animation.Freeze();
        return animation;
    }

    /// <summary>Shows the GIF on the image, moving while the image is on screen (and <paramref name="animate"/>).</summary>
    public static void Play(Image image, GifAnimation gif, bool animate = true)
    {
        image.Source = gif.Frames[0];
        if (!animate || gif.Frames.Count < 2) return;
        var animation = Animation(gif);
        void Start() => image.BeginAnimation(Image.SourceProperty, animation);
        // An endless clock keeps ticking until a garbage collection even when its image is gone: stop it.
        image.Loaded += (_, _) => Start();
        image.Unloaded += (_, _) => image.BeginAnimation(Image.SourceProperty, null);
        if (image.IsLoaded) Start();
    }

    /// <summary>An image that fetches the GIF behind the link and plays it; a soft placeholder until then.</summary>
    /// <param name="fitHeight">Once the GIF is in, the height follows its shape (between a third and 1.4 times the width).</param>
    public static FrameworkElement View(string url, double width, double height, double radius, bool animate = true, int maxSide = 320, bool fitHeight = false)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, Width = width, Height = height };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
        var label = new TextBlock
        {
            Text = "GIF", FontSize = Math.Max(9, Math.Min(13, height / 5)), FontWeight = FontWeights.Bold,
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
        void Clip() => host.Clip = new RectangleGeometry(new Rect(0, 0, width, host.Height), radius, radius);
        host.Loaded += (_, _) => Clip();
        _ = LoadInto(image, label, url, animate, maxSide, gif =>
        {
            if (!fitHeight) return;
            double fitted = Math.Clamp(width * gif.Height / Math.Max(1, gif.Width), width / 3, width * 1.4);
            host.Height = grid.Height = image.Height = fitted;
            Clip();
        });
        return host;
    }

    static async Task LoadInto(Image image, TextBlock label, string url, bool animate, int maxSide, Action<GifAnimation> loaded)
    {
        var gif = await GifCache.LoadAsync(url, maxSide);
        if (gif == null)
        {
            label.Text = "GIF unavailable";
            label.FontSize = 11;
            return;
        }
        label.Visibility = Visibility.Collapsed;
        loaded(gif);
        Play(image, gif, animate);
    }
}

/// <summary>One GIF in search results: a small one to show in the picker and the one to send.</summary>
internal sealed record GifResult(string Preview, string Url, double Width, double Height);

/// <summary>
/// Searching GIFs on GIPHY. It needs an API key: one built into Holm (<see cref="BuiltInKey"/>, empty in the open
/// source code) or one you add in the GIF picker. Without a key, GIF links can still be pasted and sent.
/// </summary>
internal static class GifSearch
{
    /// <summary>A GIPHY API key the release is built with; empty means everyone adds their own.</summary>
    public const string BuiltInKey = "";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12), DefaultRequestHeaders = { { "User-Agent", "Holm-Chat/1" } } };

    public static string? Key(AppSettings settings)
        => !string.IsNullOrWhiteSpace(settings.GifSearchKey) ? settings.GifSearchKey.Trim() : BuiltInKey.Length > 0 ? BuiltInKey : null;

    /// <summary>GIFs for the words, or what's trending when there are none.</summary>
    /// <exception cref="IOException">GIPHY couldn't be reached or refused the key.</exception>
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

    /// <summary>The GIFs in a GIPHY answer: the small still-moving preview and the 200-pixel-high GIF to send.</summary>
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
