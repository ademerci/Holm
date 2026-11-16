using Holm.Platform;

namespace Holm.Platform.MacOS;

public sealed class MacPlatformServices : IPlatformServices
{
    public INotchWindowPlatform NotchWindow { get; } = new MacNotchWindowPlatform();
    public IMediaPlatform Media { get; } = new MacMediaPlatform();
    public IAudioPlatform Audio { get; } = new MacAudioPlatform();
    public IPowerPlatform Power { get; } = new MacPowerPlatform();
    public IBluetoothPlatform Bluetooth { get; } = new MacBluetoothPlatform();
    public IAutostartPlatform Autostart { get; } = new MacAutostartPlatform();
}
