using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Holm.Platform;
using SkiaSharp;
using WinNotch;

namespace Holm.App;

/// <summary>What the notch shows of the player: the system's report plus the pictures made from its cover.</summary>
public sealed class MediaInfo
{
    public string Title = "";
    public string Artist = "";
    public string Album = "";
    public string App = "";
    public string AppId = "";
    public bool IsPlaying;
    /// <summary>Null when the player doesn't report it.</summary>
    public bool? Shuffle;
    /// <summary>0 off, 1 repeat one, 2 repeat all; null when the player doesn't report it.</summary>
    public int? Repeat;
    public bool CanShuffle, CanRepeat;
    public Bitmap? Art;
    public Color Accent = Colors.White;
    /// <summary>Blurred cover used as the notch background, and how much black goes over it.</summary>
    public Bitmap? Backdrop;
    public double BackdropShade = 0.5;
    public TimeSpan Position;
    public TimeSpan Duration;
    public DateTimeOffset PositionStamp;
    public double Rate = 1;
    public bool CanSeek, CanPrevious, CanNext, CanPlayPause;

    /// <summary>Apps only report the position when it jumps, so extrapolate it while playing.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var pos = Position;
            if (IsPlaying && PositionStamp != default)
                pos += TimeSpan.FromTicks((long)((DateTimeOffset.Now - PositionStamp).Ticks * Rate));
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
            return pos;
        }
    }

    /// <summary>Same length, and the position is where this one says it is now (give or take half a second).</summary>
    public bool SameTimeline(MediaInfo other)
        => Duration == other.Duration && (CurrentPosition - other.CurrentPosition).Duration() < TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether <paramref name="other"/> would look exactly like this one in the notch. Players repeat
    /// themselves a lot: Spotify re-sends its timeline every few seconds, also while paused.
    /// </summary>
    public bool SameAs(MediaInfo other)
        => Title == other.Title && Artist == other.Artist && Album == other.Album && App == other.App && AppId == other.AppId
        && IsPlaying == other.IsPlaying && Shuffle == other.Shuffle && Repeat == other.Repeat && Rate == other.Rate
        && CanShuffle == other.CanShuffle && CanRepeat == other.CanRepeat && CanSeek == other.CanSeek
        && CanPrevious == other.CanPrevious && CanNext == other.CanNext && CanPlayPause == other.CanPlayPause
        && ReferenceEquals(Art, other.Art) && ReferenceEquals(Backdrop, other.Backdrop)
        && Accent == other.Accent && BackdropShade == other.BackdropShade
        && SameTimeline(other);
}

/// <summary>
/// Follows the system's players through <see cref="IMediaPlatform"/>, on every OS the same way: changes are
/// gathered for a moment, read once, and only what really looks different reaches the notch. The cover is decoded
/// and blurred once per track, off the UI thread.
/// </summary>
public sealed class MediaService
{
    readonly IMediaPlatform _platform;
    readonly DispatcherTimer _debounce;
    bool _refreshing, _refreshAgain, _published;
    // 1 when something besides a timeline changed since the last refresh. Set from the system's threads.
    int _fullRefresh = 1;
    MediaInfo? _shown;
    string _trackKey = "";
    Bitmap? _art, _backdrop;
    Color _accent = Colors.White;
    double _shade = 0.5;

    readonly record struct Cover(Bitmap? Art, Color Accent, Bitmap? Backdrop, double Shade);

    /// <summary>The player to show (null: none), and whether it is another track than before. On the UI thread.</summary>
    public event Action<MediaInfo?, bool>? Changed;

    public MediaService(IMediaPlatform platform)
    {
        _platform = platform;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(); };
        _platform.Changed += Poke;
    }

    public Task StartAsync() => _platform.StartAsync();

    void Poke(bool full)
    {
        if (full) Interlocked.Exchange(ref _fullRefresh, 1);
        Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
    }

    async Task RefreshAsync()
    {
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshCoreAsync();
            } while (_refreshAgain);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _fullRefresh, 1); // don't trust what we have
            Diag.Log("Media refresh failed: " + ex.Message);
        }
        finally { _refreshing = false; }
    }

    async Task RefreshCoreAsync()
    {
        // Only a timeline moved (Spotify sends one every few seconds, also while paused). When the position is
        // where the notch already puts it, there's nothing to ask the player and nothing to redraw.
        bool full = Interlocked.Exchange(ref _fullRefresh, 0) == 1;
        if (!full && _shown is { } shown && _platform.ReadTimeline() is { } moved)
        {
            var timeline = new MediaInfo
            {
                IsPlaying = shown.IsPlaying, Rate = shown.Rate,
                Duration = moved.Duration, Position = moved.Position, PositionStamp = moved.Stamp,
            };
            if (timeline.SameTimeline(shown)) return;
        }

        var state = await _platform.ReadAsync();
        if (state == null) { Publish(null, false); return; }

        var info = new MediaInfo
        {
            Title = state.Title, Artist = state.Artist, Album = state.Album, App = state.App, AppId = state.AppId,
            IsPlaying = state.IsPlaying, Shuffle = state.Shuffle, Repeat = state.Repeat, Rate = state.Rate,
            CanShuffle = state.CanShuffle, CanRepeat = state.CanRepeat, CanSeek = state.CanSeek,
            CanPrevious = state.CanPrevious, CanNext = state.CanNext, CanPlayPause = state.CanPlayPause,
        };
        if (state.Timeline is { } t) (info.Duration, info.Position, info.PositionStamp) = (t.Duration, t.Position, t.Stamp);

        var key = info.App + "|" + info.Title + "|" + info.Artist;
        bool trackChanged = key != _trackKey;
        if (trackChanged)
        {
            _trackKey = key;
            _art = _backdrop = null;
            _accent = Colors.White;
        }
        // Apps often send the title first and the cover a moment later, so keep trying until we have one.
        if (_art == null && await _platform.ReadCoverAsync() is { } bytes)
        {
            var cover = await Task.Run(() => LoadArt(bytes));
            if (key == _trackKey && cover.Art != null)
                (_art, _accent, _backdrop, _shade) = (cover.Art, cover.Accent, cover.Backdrop, cover.Shade);
        }
        info.Art = _art;
        info.Accent = _accent;
        info.Backdrop = _backdrop;
        info.BackdropShade = _shade;
        Publish(info, trackChanged);
    }

    void Publish(MediaInfo? info, bool trackChanged)
    {
        if (info == null) _trackKey = "";
        // Nothing the notch doesn't show already: no redraw, no new brushes.
        if (_published && !trackChanged && (info == null ? _shown == null : _shown != null && _shown.SameAs(info))) return;
        _published = true;
        _shown = info;
        Changed?.Invoke(info, trackChanged);
    }

    public void PlayPause() => _platform.PlayPause();
    public void Next() => _platform.Next();
    public void Previous() => _platform.Previous();
    public void Seek(TimeSpan position) => _platform.Seek(position);
    public void SetShuffle(bool on) => _platform.SetShuffle(on);
    public void SetRepeat(int mode) => _platform.SetRepeat(mode);

    /// <summary>The cover for the notch (160 px wide), its accent colour and its blurred backdrop.</summary>
    static Cover LoadArt(byte[] bytes)
    {
        try
        {
            using var source = SKBitmap.Decode(bytes);
            if (source == null || source.Width <= 0 || source.Height <= 0) return default;

            var backdropPixels = Pixels(source, CoverMath.BackdropSize, CoverMath.BackdropSize);
            double luminance = CoverMath.Backdrop(backdropPixels);
            var backdrop = FromPixels(backdropPixels, CoverMath.BackdropSize, CoverMath.BackdropSize);

            int smallHeight = Math.Max(1, (int)Math.Round(16.0 * source.Height / source.Width));
            var (r, g, b) = CoverMath.Accent(Pixels(source, 16, smallHeight));

            Bitmap art;
            using (var stream = new MemoryStream(bytes)) art = Bitmap.DecodeToWidth(stream, 160);
            return new Cover(art, Color.FromRgb(r, g, b), backdrop, CoverMath.ShadeFor(luminance));
        }
        catch { return default; }
    }

    /// <summary>The picture scaled to this size, as BGRA bytes.</summary>
    static byte[] Pixels(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scaled = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                           ?? throw new InvalidOperationException("The cover could not be scaled.");
        var px = new byte[width * height * 4];
        Marshal.Copy(scaled.GetPixels(), px, 0, px.Length);
        return px;
    }

    static WriteableBitmap FromPixels(byte[] px, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        for (int y = 0; y < height; y++)
            Marshal.Copy(px, y * width * 4, frame.Address + y * frame.RowBytes, width * 4);
        return bitmap;
    }
}
