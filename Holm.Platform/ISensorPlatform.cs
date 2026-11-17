using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>An app using the microphone or the camera right now.</summary>
/// <param name="Key">The app as the call list and the logos know it ("Discord", "chrome").</param>
/// <param name="Since">When it started, UTC.</param>
public sealed record SensorActivity(bool Camera, string Name, string Key, DateTime Since);

/// <summary>
/// Which apps use the microphone or the camera (the privacy dots and calls), and the microphone's mute for every
/// app. Event driven: <see cref="Changed"/> comes when an app starts or stops, or the mute changes.
/// </summary>
public interface ISensorPlatform
{
    /// <summary>Raised on any thread.</summary>
    event Action? Changed;

    void Start();
    void Stop();

    /// <summary>Camera first, then by when they started.</summary>
    IReadOnlyList<SensorActivity> InUse { get; }

    /// <summary>False when the default microphone has no mute (or there is none).</summary>
    bool TryGetMicrophoneMute(out bool muted);
    void SetMicrophoneMute(bool mute);
}

public sealed class NoSensorPlatform : ISensorPlatform
{
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public IReadOnlyList<SensorActivity> InUse => Array.Empty<SensorActivity>();
    public bool TryGetMicrophoneMute(out bool muted) { muted = false; return false; }
    public void SetMicrophoneMute(bool mute) { }
}
