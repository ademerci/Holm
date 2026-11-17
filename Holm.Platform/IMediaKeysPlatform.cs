using System;

namespace Holm.Platform;

public enum MediaKey
{
    VolumeMute,
    VolumeDown,
    VolumeUp,
    NextTrack,
    PreviousTrack,
    StopMedia,
    PlayPause,
}

/// <summary>
/// Intercepts the volume and media keys when "Volume and media only in the notch" is on, so the system doesn't
/// show its own pop-up next to the notch's.
/// </summary>
public interface IMediaKeysPlatform
{
    bool IsSupported { get; }
    bool Start(Func<MediaKey, bool> takes, Action<MediaKey> press);
    void Stop();
}

/// <summary>Shared volume step math (2 % steps, matching Windows).</summary>
public static class MediaKeysMath
{
    public static float Step(float level, bool up)
    {
        int percent = (int)MathF.Round(Math.Clamp(level, 0, 1) * 100);
        percent = up ? (percent / 2 + 1) * 2 : (percent + 1) / 2 * 2 - 2;
        return Math.Clamp(percent, 0, 100) / 100f;
    }
}

public sealed class NoMediaKeysPlatform : IMediaKeysPlatform
{
    public bool IsSupported => false;
    public bool Start(Func<MediaKey, bool> takes, Action<MediaKey> press) => false;
    public void Stop() { }
}
