using System;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// Volume and levels through CoreAudio. The work is done by the WPF app's own VolumeService and AudioMeter
/// (linked from WinNotch/, not copied): this only puts them behind <see cref="IAudioPlatform"/>.
/// </summary>
public sealed class WindowsAudioPlatform : IAudioPlatform
{
    readonly VolumeService _volume = new();
    readonly AudioMeter _meter = new();

    public event Action<float, bool>? VolumeChanged
    {
        add => _volume.Changed += value;
        remove => _volume.Changed -= value;
    }

    public event Action? DefaultDeviceChanged
    {
        add => _volume.DefaultDeviceChanged += value;
        remove => _volume.DefaultDeviceChanged -= value;
    }

    public void Start() => _volume.Bind();

    public void Stop()
    {
        _meter.Stop();
        _volume.Shutdown();
    }

    public bool TryGetVolume(out float level, out bool muted) => _volume.TryGet(out level, out muted);
    public void SetVolume(float level) => _volume.SetLevel(level);
    public void SetMute(bool mute) => _volume.SetMute(mute);
    public void ToggleMute() => _volume.ToggleMute();

    public void StartMeter() => _meter.Start();
    public void StopMeter() => _meter.Stop();
    public bool MeterWorking => _meter.Working;
    public float MeterBar(int bar, int bars) => _meter.Levels.Bar(bar, bars);
}
