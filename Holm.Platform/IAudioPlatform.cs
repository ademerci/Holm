using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>
/// The system's sound output: its volume, and how loud what plays is (for the equalizer). Event driven: a
/// backend must not poll, and the meter only reads sound between <see cref="StartMeter"/> and <see cref="StopMeter"/>.
/// </summary>
public interface IAudioPlatform
{
    /// <summary>The volume (0 to 1) or mute changed, by anyone; raised on any thread.</summary>
    event Action<float, bool>? VolumeChanged;

    /// <summary>Another device became the default output; raised on any thread. Call <see cref="Start"/> again to follow it.</summary>
    event Action? DefaultDeviceChanged;

    /// <summary>Starts listening to the default output's volume. Call again when the default device changes.</summary>
    void Start();

    /// <summary>Stops listening entirely (app exit).</summary>
    void Stop();

    /// <summary>False when there is no output or it can't be read.</summary>
    bool TryGetVolume(out float level, out bool muted);

    void SetVolume(float level);
    void SetMute(bool mute);
    void ToggleMute();

    /// <summary>Starts reading how loud the output is. Only the level is taken: nothing is recorded, kept or sent.</summary>
    void StartMeter();
    void StopMeter();

    /// <summary>Whether the meter delivers levels right now; false while it starts or when the sound can't be read.</summary>
    bool MeterWorking { get; }

    /// <summary>The level (0 to 1) equalizer bar <paramref name="bar"/> of <paramref name="bars"/> shows; the last bar is the newest sound.</summary>
    float MeterBar(int bar, int bars);

    /// <summary>The speakers and headphones sound can go to, and which one it goes to now.</summary>
    IReadOnlyList<AudioOutput> Outputs(out string? current);

    /// <summary>Makes an output the default, as the system's sound settings do. False if that didn't work.</summary>
    bool SetDefaultOutput(string id);
}

/// <summary>A place sound can go: its id, the name people know it by, and an icon glyph for its kind.</summary>
public sealed record AudioOutput(string Id, string Name, string Glyph)
{
    public const string Speakers = "\uE7F5", Headphones = "\uE7F6", Headset = "\uE95B", Display = "\uE7F4";
}

/// <summary>For systems without a backend yet: no volume, no levels.</summary>
public sealed class NoAudioPlatform : IAudioPlatform
{
    public event Action<float, bool>? VolumeChanged { add { } remove { } }
    public event Action? DefaultDeviceChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public bool TryGetVolume(out float level, out bool muted) { level = 0; muted = false; return false; }
    public void SetVolume(float level) { }
    public void SetMute(bool mute) { }
    public void ToggleMute() { }
    public void StartMeter() { }
    public void StopMeter() { }
    public bool MeterWorking => false;
    public float MeterBar(int bar, int bars) => 0;
    public IReadOnlyList<AudioOutput> Outputs(out string? current) { current = null; return Array.Empty<AudioOutput>(); }
    public bool SetDefaultOutput(string id) => false;
}
