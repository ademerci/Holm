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
    public IClipboardPlatform Clipboard { get; } = new MacClipboardPlatform();
    public IScreenPlatform Screen { get; } = new MacScreenPlatform();
    public IAppVolumePlatform AppVolumes { get; } = new NoAppVolumePlatform();
    public IShellPlatform Shell { get; } = new MacShellPlatform();
    public IConnectivityPlatform Connectivity { get; } = new MacConnectivityPlatform();
}
