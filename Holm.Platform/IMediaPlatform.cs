using System;
using System.Threading.Tasks;

namespace Holm.Platform;

/// <summary>What a player says about itself, as the system reports it. No pictures: the cover is read separately.</summary>
public sealed class MediaState
{
    public string Title = "";
    public string Artist = "";
    public string Album = "";
    /// <summary>The player's name as people know it ("Spotify", "Chrome").</summary>
    public string App = "";
    /// <summary>The system's own id for the player (Windows: its AppUserModelID; Linux: its MPRIS bus name).</summary>
    public string AppId = "";
    public bool IsPlaying;
    /// <summary>Null when the player doesn't report it.</summary>
    public bool? Shuffle;
    /// <summary>0 off, 1 repeat one, 2 repeat all; null when the player doesn't report it.</summary>
    public int? Repeat;
    public bool CanShuffle, CanRepeat, CanSeek, CanPrevious, CanNext, CanPlayPause;
    public double Rate = 1;
    public MediaTimeline? Timeline;
}

/// <summary>Where a player is in its track, and when it said so.</summary>
public readonly record struct MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset Stamp);

/// <summary>
/// The system's media sessions (Windows: GSMTC; Linux: MPRIS; macOS: MediaRemote). Event driven: a backend
/// must not poll. What reaches the notch, and when, is decided above this in the shared MediaService.
/// </summary>
public interface IMediaPlatform
{
    /// <summary>
    /// Something changed; raised on any thread. The argument is false when only a timeline moved, which players
    /// repeat every few seconds and which usually needs no redraw.
    /// </summary>
    event Action<bool>? Changed;

    Task StartAsync();

    /// <summary>Picks the session to show (whatever plays, else the system's current one) and reads it; null when there is none.</summary>
    Task<MediaState?> ReadAsync();

    /// <summary>The timeline of the session last read, without asking for anything else; null when it doesn't say.</summary>
    MediaTimeline? ReadTimeline();

    /// <summary>The encoded cover (JPEG, PNG, ...) of the track last read; null while the player hasn't sent one.</summary>
    Task<byte[]?> ReadCoverAsync();

    void PlayPause();
    void Play();
    void Pause();
    void Next();
    void Previous();
    void Seek(TimeSpan position);
    void SetShuffle(bool on);
    /// <summary>0 off, 1 repeat one, 2 repeat all.</summary>
    void SetRepeat(int mode);
}

/// <summary>For systems without a backend yet: nothing ever plays.</summary>
public sealed class NoMediaPlatform : IMediaPlatform
{
    public event Action<bool>? Changed { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
    public Task<MediaState?> ReadAsync() => Task.FromResult<MediaState?>(null);
    public MediaTimeline? ReadTimeline() => null;
    public Task<byte[]?> ReadCoverAsync() => Task.FromResult<byte[]?>(null);
    public void PlayPause() { }
    public void Play() { }
    public void Pause() { }
    public void Next() { }
    public void Previous() { }
    public void Seek(TimeSpan position) { }
    public void SetShuffle(bool on) { }
    public void SetRepeat(int mode) { }
}
