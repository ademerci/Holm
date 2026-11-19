namespace Holm.Platform;

/// <summary>
/// The few sounds Holm makes, as the WPF app makes them: the alarm when a timer or a reminder is due, and the chime when
/// someone sends you files. Never blocks; a system without sound stays silent.
/// </summary>
public interface ISoundPlatform
{
    /// <summary>The system's alarm sound (Windows: Media\Alarm01.wav, else the exclamation sound; Linux: the sound theme's alarm).</summary>
    void PlayAlarm();

    /// <summary>A short WAV the app carries; the system's information sound when it can't be played.</summary>
    void PlayWav(byte[] wav);
}

public sealed class NoSoundPlatform : ISoundPlatform
{
    public void PlayAlarm() { }
    public void PlayWav(byte[] wav) { }
}
