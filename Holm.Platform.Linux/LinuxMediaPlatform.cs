using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Holm.Platform;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// The players on the session bus (MPRIS: Spotify, Firefox, Chromium, VLC, mpv, ...). Players announce every
/// change with a signal; between signals nothing is asked. The position is read together with everything else
/// and the shared MediaService counts it forward while the track plays, as on Windows.
/// </summary>
public sealed class LinuxMediaPlatform : IMediaPlatform
{
    const string NamePrefix = "org.mpris.MediaPlayer2.";
    const string ObjectPath = "/org/mpris/MediaPlayer2";
    const string Root = "org.mpris.MediaPlayer2", Player = "org.mpris.MediaPlayer2.Player";
    /// <summary>A cover larger than this isn't one.</summary>
    const int MaxCoverBytes = 8 * 1024 * 1024;
    static readonly TimeSpan CoverRetryDelay = TimeSpan.FromSeconds(3);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = MaxCoverBytes };

    readonly Bus _bus;
    readonly TimeProvider _time;
    readonly object _gate = new();
    readonly Dictionary<string, string> _identities = new();
    bool _started;
    string[]? _players;
    // The player last read, and what is needed to steer it and to fetch its cover.
    string? _chosen;
    string _trackId = "", _artUrl = "";
    MediaTimeline? _timeline;
    string? _coverUrl;
    byte[]? _cover;
    long _coverAttempt;

    public event Action<bool>? Changed;

    public LinuxMediaPlatform() : this(Bus.Session) { }
    internal LinuxMediaPlatform(Bus bus, TimeProvider? time = null)
        => (_bus, _time) = (bus, time ?? TimeProvider.System);

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        // A player came or went.
        // Filtered here rather than by an arg0namespace rule: Tmds.DBus writes that key as "arg0Namespace", which
        // dbus-daemon rejects as invalid, and the whole watch was lost.
        await _bus.WatchAsync(new MatchRule
        {
            Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged",
        }, signal =>
        {
            if (signal.Arg0 != Root && !signal.Arg0.StartsWith(Root + ".", StringComparison.Ordinal)) return;
            lock (_gate) _players = null;
            Changed?.Invoke(true);
        }).ConfigureAwait(false);
        // A player says something about itself changed (track, playing or paused, shuffle, ...).
        await _bus.WatchAsync(new MatchRule
        {
            Type = MessageType.Signal, Path = ObjectPath, Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged",
        }, signal => { if (signal.Arg0 == Player) Changed?.Invoke(true); }).ConfigureAwait(false);
        // A jump within the track. Rare enough to simply read the player again.
        await _bus.WatchAsync(new MatchRule
        {
            Type = MessageType.Signal, Path = ObjectPath, Interface = Player, Member = "Seeked",
        }, _ => Changed?.Invoke(true)).ConfigureAwait(false);
        Changed?.Invoke(true);
    }

    public async Task<MediaState?> ReadAsync()
    {
        string[]? players;
        lock (_gate) players = _players;
        if (players == null)
        {
            players = await ListPlayersAsync().ConfigureAwait(false);
            lock (_gate) _players = players;
        }

        // What plays wins; among several, the one already shown. With nothing playing, the one shown stays, and
        // failing that the first that has a track.
        (string Name, Dictionary<string, VariantValue> Props)? best = null;
        int bestRank = 0;
        foreach (var name in players)
        {
            var props = await _bus.GetAllAsync(name, ObjectPath, Player).ConfigureAwait(false);
            if (props.Count == 0) continue;
            string status = Str(props, "PlaybackStatus");
            bool hasTrack = TitleOf(props).Length > 0;
            if (status == "Stopped" && !hasTrack) continue;
            int rank = (status == "Playing" ? 4 : hasTrack ? 2 : 1) + (name == _chosen ? 1 : 0);
            if (rank > bestRank) (best, bestRank) = ((name, props), rank);
        }

        if (best is not { } found)
        {
            lock (_gate) { _chosen = null; _timeline = null; _trackId = _artUrl = ""; }
            return null;
        }

        var p = found.Props;
        var metadata = Metadata(p);
        var state = new MediaState
        {
            Title = TitleOf(p),
            Artist = metadata.TryGetValue("xesam:artist", out var artists) ? JoinStrings(artists) : "",
            Album = Str(metadata, "xesam:album"),
            App = await IdentityAsync(found.Name).ConfigureAwait(false),
            AppId = found.Name,
            IsPlaying = Str(p, "PlaybackStatus") == "Playing",
            Shuffle = p.TryGetValue("Shuffle", out var shuffle) && shuffle.Type == VariantValueType.Bool ? shuffle.GetBool() : null,
            Repeat = Str(p, "LoopStatus") switch { "None" => 0, "Track" => 1, "Playlist" => 2, _ => null },
            CanSeek = Flag(p, "CanSeek"),
            CanPrevious = Flag(p, "CanGoPrevious"),
            CanNext = Flag(p, "CanGoNext"),
            CanPlayPause = Flag(p, "CanPlay") || Flag(p, "CanPause"),
            Rate = p.TryGetValue("Rate", out var rate) && Number(rate) is > 0 and var r ? r : 1,
        };
        bool canControl = !p.ContainsKey("CanControl") || Flag(p, "CanControl");
        state.CanShuffle = canControl && state.Shuffle != null;
        state.CanRepeat = canControl && state.Repeat != null;

        // Microseconds, in whichever number type the player picked.
        double length = metadata.TryGetValue("mpris:length", out var l) ? Number(l) ?? 0 : 0;
        double position = p.TryGetValue("Position", out var pos) ? Number(pos) ?? 0 : 0;
        if (length > 0)
            state.Timeline = new MediaTimeline(TimeSpan.FromMilliseconds(position / 1000), TimeSpan.FromMilliseconds(length / 1000), DateTimeOffset.Now);

        lock (_gate)
        {
            _chosen = found.Name;
            _timeline = state.Timeline;
            _trackId = metadata.TryGetValue("mpris:trackid", out var id) ? Text(id) : "";
            _artUrl = Str(metadata, "mpris:artUrl");
        }
        return state;
    }

    public MediaTimeline? ReadTimeline()
    {
        lock (_gate) return _timeline;
    }

    public Task<byte[]?> ReadCoverAsync()
    {
        string url;
        lock (_gate) url = _artUrl;
        return ReadCoverAsync(url);
    }

    internal async Task<byte[]?> ReadCoverAsync(string url)
    {
        if (url.Length == 0) return null;
        lock (_gate)
        {
            if (url == _coverUrl && (_cover != null || _time.GetElapsedTime(_coverAttempt) < CoverRetryDelay))
                return _cover;
        }
        byte[]? bytes = await LoadCoverAsync(url).ConfigureAwait(false);
        lock (_gate) (_coverUrl, _cover, _coverAttempt) = (url, bytes, _time.GetTimestamp());
        return bytes;
    }

    /// <summary>
    /// The picture a player points to: a file (most players), an address on the web (Spotify hands out its
    /// CDN's) or the picture itself as a data URL.
    /// </summary>
    internal static async Task<byte[]?> LoadCoverAsync(string url)
    {
        try
        {
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = url.IndexOf(',');
                if (comma < 0 || !url.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
                return Convert.FromBase64String(url[(comma + 1)..]);
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.IsFile)
            {
                var file = new FileInfo(uri.LocalPath);
                return file.Exists && file.Length <= MaxCoverBytes ? await File.ReadAllBytesAsync(file.FullName).ConfigureAwait(false) : null;
            }
            if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                return await Http.GetByteArrayAsync(uri).ConfigureAwait(false);
        }
        catch { }
        return null;
    }

    public void PlayPause() => Call("PlayPause");
    public void Play() => Call("Play");
    public void Pause() => Call("Pause");
    public void Next() => Call("Next");
    public void Previous() => Call("Previous");

    public void Seek(TimeSpan position)
    {
        string? player; string track;
        lock (_gate) (player, track) = (_chosen, _trackId);
        // SetPosition wants the track it is meant for, so a seek that arrives late can't land in the next song.
        if (player == null || !track.StartsWith('/')) return;
        long microseconds = (long)(position.TotalMilliseconds * 1000);
        _ = _bus.CallAsync(player, ObjectPath, Player, "SetPosition", "ox", w => { w.ObjectPath(track); w.Int64(microseconds); });
    }

    public void SetShuffle(bool on)
    {
        if (Chosen() is { } player) _ = _bus.SetAsync(player, ObjectPath, Player, "Shuffle", VariantValue.Bool(on));
    }

    public void SetRepeat(int mode)
    {
        if (Chosen() is { } player)
            _ = _bus.SetAsync(player, ObjectPath, Player, "LoopStatus", VariantValue.String(mode switch { 1 => "Track", 2 => "Playlist", _ => "None" }));
    }

    void Call(string method)
    {
        if (Chosen() is { } player) _ = _bus.CallAsync(player, ObjectPath, Player, method);
    }

    string? Chosen()
    {
        lock (_gate) return _chosen;
    }

    async Task<string[]> ListPlayersAsync()
    {
        try
        {
            if (await _bus.ConnectAsync().ConfigureAwait(false) is not { } connection) return Array.Empty<string>();
            var names = await connection.ListServicesAsync().ConfigureAwait(false);
            return names.Where(n => n.StartsWith(NamePrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>The player's name as people know it; it never changes while the player runs.</summary>
    async Task<string> IdentityAsync(string name)
    {
        lock (_gate) if (_identities.TryGetValue(name, out var known)) return known;
        string identity = await _bus.GetAsync(name, ObjectPath, Root, "Identity").ConfigureAwait(false) is { } v ? Text(v) : "";
        if (identity.Length == 0) identity = FallbackName(name);
        lock (_gate)
        {
            if (_identities.Count > 32) _identities.Clear();
            _identities[name] = identity;
        }
        return identity;
    }

    /// <summary>"org.mpris.MediaPlayer2.firefox.instance_1_23" is "Firefox".</summary>
    internal static string FallbackName(string busName)
    {
        string rest = busName.StartsWith(NamePrefix, StringComparison.Ordinal) ? busName[NamePrefix.Length..] : busName;
        int dot = rest.IndexOf('.');
        if (dot > 0) rest = rest[..dot];
        return rest.Length == 0 ? "" : char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    static Dictionary<string, VariantValue> Metadata(Dictionary<string, VariantValue> props)
    {
        try
        {
            if (props.TryGetValue("Metadata", out var m) && m.Type == VariantValueType.Dictionary)
                return m.GetDictionary<string, VariantValue>();
        }
        catch { }
        return new();
    }

    static string TitleOf(Dictionary<string, VariantValue> props) => Str(Metadata(props), "xesam:title");

    static string Str(Dictionary<string, VariantValue> values, string key) => values.TryGetValue(key, out var v) ? Text(v) : "";

    static bool Flag(Dictionary<string, VariantValue> values, string key)
        => values.TryGetValue(key, out var v) && v.Type == VariantValueType.Bool && v.GetBool();

    internal static string Text(VariantValue value) => value.Type switch
    {
        VariantValueType.String => value.GetString(),
        VariantValueType.ObjectPath => value.GetObjectPathAsString(),
        _ => "",
    };

    /// <summary>"Artist" is a list in MPRIS; a few players send a plain string.</summary>
    internal static string JoinStrings(VariantValue value)
    {
        try
        {
            if (value.Type == VariantValueType.Array && value.ItemType == VariantValueType.String)
                return string.Join(", ", value.GetArray<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch { }
        return Text(value);
    }

    internal static double? Number(VariantValue value) => value.Type switch
    {
        VariantValueType.Int64 => value.GetInt64(),
        VariantValueType.UInt64 => value.GetUInt64(),
        VariantValueType.Int32 => value.GetInt32(),
        VariantValueType.UInt32 => value.GetUInt32(),
        VariantValueType.Double => value.GetDouble(),
        VariantValueType.Byte => value.GetByte(),
        VariantValueType.Int16 => value.GetInt16(),
        VariantValueType.UInt16 => value.GetUInt16(),
        _ => null,
    };
}
