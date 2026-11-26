using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Holm.Platform;
using Windows.Media.Control;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// The system media sessions (Spotify, browsers, VLC, ...) through GSMTC. Purely event driven: nothing runs until
/// Windows reports a change. Same session choice and app names as the WPF app's MediaService.
/// </summary>
public sealed class WindowsMediaPlatform : IMediaPlatform
{
    static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["308046B0AF4A39CB"] = "Firefox",
        ["spotify"] = "Spotify", ["spotifymusic"] = "Spotify", ["zunemusic"] = "Media Player", ["vlc"] = "VLC",
        ["opera"] = "Opera", ["brave"] = "Brave", ["discord"] = "Discord", ["applemusicwin"] = "Apple Music",
        ["tidal"] = "TIDAL", ["deezer"] = "Deezer", ["amazonmusic"] = "Amazon Music",
    };

    readonly List<GlobalSystemMediaTransportControlsSession> _hooked = new();
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    GlobalSystemMediaTransportControlsSessionMediaProperties? _properties;
    TimeSpan _sessionStart;
    volatile bool _sessionsDirty = true;

    public event Action<bool>? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => { _sessionsDirty = true; Changed?.Invoke(true); };
            _manager.CurrentSessionChanged += (_, _) => Changed?.Invoke(true);
            Changed?.Invoke(true);
        }
        catch (Exception ex) { Diag.Log("Media manager unavailable: " + ex.Message); }
    }

    void OnProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Changed?.Invoke(true);
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Changed?.Invoke(true);
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Changed?.Invoke(false);

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

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    public async Task<MediaState?> ReadAsync()
    {
        _properties = null;
        if (_manager == null) return null;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions(); } catch { sessions = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
        if (_sessionsDirty) { _sessionsDirty = false; Hook(sessions); }

        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager.GetCurrentSession(); } catch { }

        // Prefer whatever is actually playing; Windows' "current" session is sometimes a paused tab.
        var chosen = current != null && IsPlaying(current) ? current
                   : sessions.FirstOrDefault(IsPlaying) ?? current ?? sessions.FirstOrDefault();
        _session = chosen;
        if (chosen == null) return null;

        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await chosen.TryGetMediaPropertiesAsync(); } catch { }
        if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;
        _properties = props;

        var state = new MediaState
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
            state.IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var c = playback?.Controls;
            state.CanPlayPause = c == null || c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled;
            state.CanNext = c?.IsNextEnabled ?? false;
            state.CanPrevious = c?.IsPreviousEnabled ?? false;
            state.CanSeek = c?.IsPlaybackPositionEnabled ?? false;
            state.CanShuffle = c?.IsShuffleEnabled ?? false;
            state.CanRepeat = c?.IsRepeatEnabled ?? false;
            state.Shuffle = playback?.IsShuffleActive;
            state.Repeat = playback?.AutoRepeatMode is { } repeat ? repeat switch
            {
                global::Windows.Media.MediaPlaybackAutoRepeatMode.Track => 1,
                global::Windows.Media.MediaPlaybackAutoRepeatMode.List => 2,
                _ => 0,
            } : null;
            var rate = playback?.PlaybackRate ?? 1.0;
            state.Rate = rate > 0 ? rate : 1.0;
        }
        catch { }

        state.Timeline = ReadTimeline();
        return state;
    }

    public MediaTimeline? ReadTimeline()
    {
        try
        {
            var timeline = _session?.GetTimelineProperties();
            if (timeline == null) return null;
            _sessionStart = timeline.StartTime;
            return new MediaTimeline(timeline.Position - timeline.StartTime, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime);
        }
        catch { return null; }
    }

    const int MaxCoverBytes = 8 * 1024 * 1024;

    public async Task<byte[]?> ReadCoverAsync()
    {
        try
        {
            var thumbnail = _properties?.Thumbnail;
            if (thumbnail == null) return null;
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size > (ulong)MaxCoverBytes) return null;
            using var input = stream.AsStreamForRead();
            return await ReadBoundedAsync(input, MaxCoverBytes).ConfigureAwait(false);
        }
        catch { return null; }
    }

    static async Task<byte[]?> ReadBoundedAsync(Stream input, int maxBytes)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes) return null;
            copy.Write(buffer, 0, read);
        }
        return total == 0 ? null : copy.ToArray();
    }

    public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Pause() { try { if (_session != null) await _session.TryPauseAsync(); } catch { } }
    public async void Play() { try { if (_session != null) await _session.TryPlayAsync(); } catch { } }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    public async void SetShuffle(bool on)
    {
        try { if (_session != null) await _session.TryChangeShuffleActiveAsync(on); } catch { }
    }

    public async void SetRepeat(int mode)
    {
        var value = mode switch
        {
            1 => global::Windows.Media.MediaPlaybackAutoRepeatMode.Track,
            2 => global::Windows.Media.MediaPlaybackAutoRepeatMode.List,
            _ => global::Windows.Media.MediaPlaybackAutoRepeatMode.None,
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
}
