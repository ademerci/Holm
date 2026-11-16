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
    public IClipboardPlatform Clipboard { get; } = new WindowsClipboardPlatform();
    public IScreenPlatform Screen { get; } = new WindowsScreenPlatform();
    public IAppVolumePlatform AppVolumes { get; } = new WindowsAppVolumePlatform();
    public IShellPlatform Shell { get; } = new WindowsShellPlatform();
    public IConnectivityPlatform Connectivity { get; } = new WindowsConnectivityPlatform();
    public ICapsLockPlatform CapsLock { get; } = new WindowsCapsLockPlatform();
    public IHotKeyPlatform HotKey { get; } = new WindowsHotKeyPlatform();
    public IRemovableDrivesPlatform Drives { get; } = new WindowsRemovableDrivesPlatform();
    public IClickAwayPlatform ClickAway { get; } = new WindowsClickAwayPlatform();
    public IForegroundPlatform Foreground { get; } = new WindowsForegroundPlatform();
    public ISensorPlatform Sensors { get; } = new WindowsSensorPlatform();
    public INotificationsPlatform Notifications { get; } = new WindowsNotificationsPlatform();
    public IIdentityPlatform Identity { get; } = new WindowsIdentityPlatform();
}
