using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinNotch;

internal sealed class MediaInfo
{
    public string Title = "";
    public string Artist = "";
    public string Album = "";
    public string App = "";
    /// <summary>The player's AppUserModelID ("Spotify.exe", "Chrome", "Publisher.App_hash!App").</summary>
    public string AppId = "";
    public bool IsPlaying;
    /// <summary>Null when the player doesn't report it.</summary>
    public bool? Shuffle;
    /// <summary>0 off, 1 repeat one, 2 repeat all; null when the player doesn't report it.</summary>
    public int? Repeat;
    public bool CanShuffle, CanRepeat;
    public ImageSource? Art;
    public Color Accent = Colors.White;
    /// <summary>Blurred cover used as the notch background, and how much black goes over it.</summary>
    public ImageSource? Backdrop;
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
}

/// <summary>
/// Follows the system media sessions (Spotify, browsers, VLC, ...). Purely event driven:
/// nothing runs until Windows reports a change.
/// </summary>
internal sealed class MediaService
{
    static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["308046B0AF4A39CB"] = "Firefox",
        ["spotify"] = "Spotify", ["spotifymusic"] = "Spotify", ["zunemusic"] = "Media Player", ["vlc"] = "VLC",
        ["opera"] = "Opera", ["brave"] = "Brave", ["discord"] = "Discord", ["applemusicwin"] = "Apple Music",
        ["tidal"] = "TIDAL", ["deezer"] = "Deezer", ["amazonmusic"] = "Amazon Music",
    };

    readonly Dispatcher _ui;
    readonly DispatcherTimer _debounce;
    readonly List<GlobalSystemMediaTransportControlsSession> _hooked = new();
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    TimeSpan _sessionStart;
    bool _sessionsDirty = true, _refreshing, _refreshAgain;
    string _trackKey = "";
    ImageSource? _art, _backdrop;
    Color _accent = Colors.White;
    double _shade = 0.5;

    readonly record struct Cover(ImageSource? Art, Color Accent, ImageSource? Backdrop, double Shade);

    public event Action<MediaInfo?, bool>? Changed;

    public MediaService(Dispatcher ui)
    {
        _ui = ui;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(); };
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => { _sessionsDirty = true; Poke(); };
            _manager.CurrentSessionChanged += (_, _) => Poke();
            Poke();
        }
        catch (Exception ex) { Diag.Log("Media manager unavailable: " + ex.Message); }
    }

    void Poke() => _ui.InvokeAsync(() => { _debounce.Stop(); _debounce.Start(); });

    void OnProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Poke();
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Poke();
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Poke();

    void Hook(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        foreach (var s in _hooked)
        {
            try
            {
                s.MediaPropertiesChanged -= OnProperties;
                s.PlaybackInfoChanged -= OnPlayback;
                s.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }
        _hooked.Clear();
        foreach (var s in sessions)
        {
            try
            {
                s.MediaPropertiesChanged += OnProperties;
                s.PlaybackInfoChanged += OnPlayback;
                s.TimelinePropertiesChanged += OnTimeline;
                _hooked.Add(s);
            }
            catch { }
        }
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
        catch (Exception ex) { Diag.Log("Media refresh failed: " + ex.Message); }
        finally { _refreshing = false; }
    }

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    async Task RefreshCoreAsync()
    {
        if (_manager == null) return;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions(); } catch { sessions = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
        if (_sessionsDirty) { Hook(sessions); _sessionsDirty = false; }

        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager.GetCurrentSession(); } catch { }

        // Prefer whatever is actually playing; Windows' "current" session is sometimes a paused tab.
        var chosen = current != null && IsPlaying(current) ? current
                   : sessions.FirstOrDefault(IsPlaying) ?? current ?? sessions.FirstOrDefault();
        _session = chosen;
        if (chosen == null) { Publish(null, false); return; }

        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await chosen.TryGetMediaPropertiesAsync(); } catch { }
        if (props == null || string.IsNullOrWhiteSpace(props.Title)) { Publish(null, false); return; }

        var info = new MediaInfo
        {
            Title = props.Title.Trim(),
            Artist = (string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist)?.Trim() ?? "",
            Album = props.AlbumTitle?.Trim() ?? "",
            App = FriendlyAppName(chosen.SourceAppUserModelId),
            AppId = chosen.SourceAppUserModelId ?? "",
        };

        try
        {
            var playback = chosen.GetPlaybackInfo();
            info.IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var c = playback?.Controls;
            info.CanPlayPause = c == null || c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled;
            info.CanNext = c?.IsNextEnabled ?? false;
            info.CanPrevious = c?.IsPreviousEnabled ?? false;
            info.CanSeek = c?.IsPlaybackPositionEnabled ?? false;
            info.CanShuffle = c?.IsShuffleEnabled ?? false;
            info.CanRepeat = c?.IsRepeatEnabled ?? false;
            info.Shuffle = playback?.IsShuffleActive;
            info.Repeat = playback?.AutoRepeatMode is { } repeat ? repeat switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => 1,
                Windows.Media.MediaPlaybackAutoRepeatMode.List => 2,
                _ => 0,
            } : null;
            var rate = playback?.PlaybackRate ?? 1.0;
            info.Rate = rate > 0 ? rate : 1.0;
        }
        catch { }

        try
        {
            var timeline = chosen.GetTimelineProperties();
            if (timeline != null)
            {
                _sessionStart = timeline.StartTime;
                info.Duration = timeline.EndTime - timeline.StartTime;
                info.Position = timeline.Position - timeline.StartTime;
                info.PositionStamp = timeline.LastUpdatedTime;
            }
        }
        catch { }

        var key = info.App + "|" + info.Title + "|" + info.Artist;
        bool trackChanged = key != _trackKey;
        if (trackChanged)
        {
            _trackKey = key;
            _art = _backdrop = null;
            _accent = Colors.White;
        }
        // Apps often send the title first and the cover a moment later, so keep trying until we have one.
        if (_art == null && props.Thumbnail != null)
        {
            var cover = await LoadArtAsync(props.Thumbnail);
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
        Changed?.Invoke(info, trackChanged);
    }

    public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    public async void SetShuffle(bool on)
    {
        try { if (_session != null) await _session.TryChangeShuffleActiveAsync(on); } catch { }
    }

    /// <summary>0 off, 1 repeat one, 2 repeat all.</summary>
    public async void SetRepeat(int mode)
    {
        var value = mode switch
        {
            1 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
            2 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
            _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
        };
        try { if (_session != null) await _session.TryChangeAutoRepeatModeAsync(value); } catch { }
    }

    public async void Seek(TimeSpan position)
    {
        try { if (_session != null) await _session.TryChangePlaybackPositionAsync((_sessionStart + position).Ticks); } catch { }
    }

    static string FriendlyAppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        string name = aumid;
        int bang = name.IndexOf('!');
        if (bang >= 0)
        {
            // Store apps: "Publisher.Package_hash!AppId"
            string appId = name[(bang + 1)..], package = name[..bang];
            int underscore = package.IndexOf('_');
            if (underscore > 0) package = package[..underscore];
            name = appId.Equals("App", StringComparison.OrdinalIgnoreCase) ? package : appId;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name[(dot + 1)..];
        }
        else
        {
            name = Path.GetFileName(name);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        }
        if (KnownApps.TryGetValue(name, out var known)) return known;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "";
    }

    static async Task<Cover> LoadArtAsync(IRandomAccessStreamReference thumbnail)
    {
        try
        {
            byte[] bytes;
            using (var stream = await thumbnail.OpenReadAsync())
            using (var input = stream.AsStreamForRead())
            using (var copy = new MemoryStream())
            {
                await input.CopyToAsync(copy);
                bytes = copy.ToArray();
            }
            if (bytes.Length == 0) return default;

            return await Task.Run(() =>
            {
                var (backdrop, shade) = BackdropFor(bytes);
                return new Cover(Decode(bytes, 160), AccentOf(Decode(bytes, 16)), backdrop, shade);
            });
        }
        catch { return default; }
    }

    /// <summary>The blurred backdrop plus enough black on top that white text stays readable, even on a white cover.</summary>
    internal static (ImageSource Backdrop, double Shade) BackdropFor(byte[] bytes)
    {
        var (image, luminance) = BackdropOf(bytes);
        return (image, Math.Clamp(1 - 0.22 / Math.Max(luminance, 0.01), 0.25, 0.8));
    }

    static BitmapSource Decode(byte[] bytes, int width, int height = 0)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = width;
        if (height > 0) bmp.DecodePixelHeight = height;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// A heavily blurred, slightly more saturated 24×24 copy of the cover. Stretched across the notch it
    /// gives the soft colour wash of the iPhone player. Blurring it once here is far cheaper than a
    /// BlurEffect, which would be recomputed on every animation frame.
    /// </summary>
    static (BitmapSource Image, double Luminance) BackdropOf(byte[] bytes)
    {
        const int n = 24;
        var bgra = new FormatConvertedBitmap(Decode(bytes, n, n), PixelFormats.Bgra32, null, 0);
        var px = new byte[n * n * 4];
        bgra.CopyPixels(px, n * 4, 0);

        var planes = new double[3][]; // B, G, R
        for (int c = 0; c < 3; c++)
        {
            planes[c] = new double[n * n];
            for (int i = 0; i < n * n; i++) planes[c][i] = px[i * 4 + c] / 255.0;
        }
        for (int pass = 0; pass < 3; pass++) // three box blurs ≈ one gaussian
            foreach (var plane in planes) { BoxBlur(plane, n, horizontal: true); BoxBlur(plane, n, horizontal: false); }

        double luminance = 0;
        for (int i = 0; i < n * n; i++)
        {
            double b = planes[0][i], g = planes[1][i], r = planes[2][i];
            double gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            luminance += gray;
            px[i * 4] = ToByte(gray + (b - gray) * 1.35);
            px[i * 4 + 1] = ToByte(gray + (g - gray) * 1.35);
            px[i * 4 + 2] = ToByte(gray + (r - gray) * 1.35);
            px[i * 4 + 3] = 255;
        }

        var image = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        image.Freeze();
        return (image, luminance / (n * n));
    }

    static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    static void BoxBlur(double[] plane, int n, bool horizontal, int radius = 2)
    {
        var line = new double[n];
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++) line[b] = plane[horizontal ? a * n + b : b * n + a];
            for (int b = 0; b < n; b++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++) sum += line[Math.Clamp(b + k, 0, n - 1)];
                plane[horizontal ? a * n + b : b * n + a] = sum / (2 * radius + 1);
            }
        }
    }

    /// <summary>A bright, saturated colour from the cover, used for the equalizer and the progress bar.</summary>
    static Color AccentOf(BitmapSource small)
    {
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double B = px[i], G = px[i + 1], R = px[i + 2];
            double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double sat = max <= 0 ? 0 : (max - min) / max;
            double weight = 0.02 + sat * sat * (max / 255.0);
            r += R * weight; g += G * weight; b += B * weight; total += weight;
        }
        r /= total; g /= total; b /= total;

        double vmax = Math.Max(r, Math.Max(g, b)), vmin = Math.Min(r, Math.Min(g, b));
        double s = vmax <= 0 ? 0 : (vmax - vmin) / vmax;
        if (s < 0.18) return Color.FromRgb(235, 235, 240);

        double hue = vmax == vmin ? 0
                   : vmax == r ? 60 * (((g - b) / (vmax - vmin)) % 6)
                   : vmax == g ? 60 * ((b - r) / (vmax - vmin) + 2)
                   : 60 * ((r - g) / (vmax - vmin) + 4);
        if (hue < 0) hue += 360;
        return FromHsv(hue, Math.Min(s * 1.15, 0.8), 1.0);
    }

    static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
