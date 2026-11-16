using Holm.Platform;

namespace Holm.Platform.Windows;

public sealed class WindowsPlatformServices : IPlatformServices
{
    public INotchWindowPlatform NotchWindow { get; } = new WindowsNotchWindowPlatform();
    public IMediaPlatform Media { get; } = new WindowsMediaPlatform();
    public IAudioPlatform Audio { get; } = new WindowsAudioPlatform();
    public IPowerPlatform Power { get; } = new WindowsPowerPlatform();
    public IBluetoothPlatform Bluetooth { get; } = new WindowsBluetoothPlatform();
    public IAutostartPlatform Autostart { get; } = new WindowsAutostartPlatform();
}
