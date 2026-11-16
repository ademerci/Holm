using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>An app in the mixer: all its sound streams under one slider.</summary>
/// <param name="Key">What makes streams one app (its program, in lower case); pass it back to change the app.</param>
public sealed record AppVolumeEntry(string Key, string Name, float Volume, bool Muted, bool Playing);

/// <summary>An app's icon: a picture file the app can open, or the pixels themselves.</summary>
public sealed record AppIcon(string? File, ScreenImage? Pixels);

/// <summary>
/// The volume of each app (Windows: Core Audio sessions; Linux: PulseAudio/PipeWire sink inputs). Follows the
/// system's events only between <see cref="Start"/> and <see cref="Stop"/>, while the mixer page is open.
/// macOS lets no app change another app's volume: there <see cref="Supported"/> is false.
/// </summary>
public interface IAppVolumePlatform
{
    bool Supported { get; }

    /// <summary>Apps came, went, or changed volume elsewhere; raised on any thread.</summary>
    event Action? Changed;

    void Start();
    void Stop();

    /// <summary>The apps with sound, playing ones first, at most six. Call off the UI thread.</summary>
    List<AppVolumeEntry> Read();

    void SetVolume(string key, float level);
    void SetMute(string key, bool mute);

    /// <summary>The app's icon, or null; may read files: call off the UI thread.</summary>
    AppIcon? IconOf(string key);
}

/// <summary>For systems that can't set an app's volume.</summary>
public sealed class NoAppVolumePlatform : IAppVolumePlatform
{
    public bool Supported => false;
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public List<AppVolumeEntry> Read() => new();
    public void SetVolume(string key, float level) { }
    public void SetMute(string key, bool mute) { }
    public AppIcon? IconOf(string key) => null;
}
