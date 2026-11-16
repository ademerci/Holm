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
    public IShellPlatform Shell { get; } = new LinuxShellPlatform();
    public IConnectivityPlatform Connectivity { get; } = new LinuxConnectivityPlatform();
    public ICapsLockPlatform CapsLock { get; } = new LinuxCapsLockPlatform();
    public IHotKeyPlatform HotKey { get; } = new LinuxHotKeyPlatform();
    public IRemovableDrivesPlatform Drives { get; } = new LinuxRemovableDrivesPlatform();
    public IClickAwayPlatform ClickAway { get; } = new LinuxClickAwayPlatform();
    public IForegroundPlatform Foreground { get; } = new LinuxForegroundPlatform();
    public ISensorPlatform Sensors { get; } = new LinuxSensorPlatform();
    public INotificationsPlatform Notifications { get; } = new LinuxNotificationsPlatform();
    public IIdentityPlatform Identity { get; } = new UnixIdentityPlatform(WinNotch.Diag.Folder);
    public IDictationPlatform Dictation { get; } = new LinuxDictationPlatform();
    public IMediaKeysPlatform MediaKeys { get; } = new NoMediaKeysPlatform();
    public ILocationPlatform Location { get; } = new NoLocationPlatform();
}
