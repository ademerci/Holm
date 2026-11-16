using Holm.Platform;

namespace Holm.Platform.Linux;

public sealed class LinuxPlatformServices : IPlatformServices
{
    public INotchWindowPlatform NotchWindow { get; } = new LinuxNotchWindowPlatform();
    public IMediaPlatform Media { get; } = new LinuxMediaPlatform();
    public IAudioPlatform Audio { get; } = new LinuxAudioPlatform();
    public IPowerPlatform Power { get; } = new LinuxPowerPlatform();
    public IBluetoothPlatform Bluetooth { get; } = new LinuxBluetoothPlatform();
    public IAutostartPlatform Autostart { get; } = new LinuxAutostartPlatform();
    public IClipboardPlatform Clipboard { get; } = new LinuxClipboardPlatform();
    public IScreenPlatform Screen { get; } = new LinuxScreenPlatform();
    public IAppVolumePlatform AppVolumes { get; } = new LinuxAppVolumePlatform();
}
