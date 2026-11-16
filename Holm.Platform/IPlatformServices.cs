namespace Holm.Platform;

/// <summary>Everything the shared app asks of the system it runs on. One implementation per OS.</summary>
public interface IPlatformServices
{
    INotchWindowPlatform NotchWindow { get; }
    IMediaPlatform Media { get; }
    IAudioPlatform Audio { get; }
    IPowerPlatform Power { get; }
    IBluetoothPlatform Bluetooth { get; }
    IAutostartPlatform Autostart { get; }
    IClipboardPlatform Clipboard { get; }
    IScreenPlatform Screen { get; }
    IAppVolumePlatform AppVolumes { get; }
    IShellPlatform Shell { get; }
    IConnectivityPlatform Connectivity { get; }
    ICapsLockPlatform CapsLock { get; }
    IHotKeyPlatform HotKey { get; }
    IRemovableDrivesPlatform Drives { get; }
    IClickAwayPlatform ClickAway { get; }
}

/// <summary>For systems without a backend yet: the app runs, with nothing from the system in it.</summary>
public sealed class NoPlatformServices : IPlatformServices
{
    public INotchWindowPlatform NotchWindow { get; } = new NoNotchWindowPlatform();
    public IMediaPlatform Media { get; } = new NoMediaPlatform();
    public IAudioPlatform Audio { get; } = new NoAudioPlatform();
    public IPowerPlatform Power { get; } = new NoPowerPlatform();
    public IBluetoothPlatform Bluetooth { get; } = new NoBluetoothPlatform();
    public IAutostartPlatform Autostart { get; } = new NoAutostartPlatform();
    public IClipboardPlatform Clipboard { get; } = new NoClipboardPlatform();
    public IScreenPlatform Screen { get; } = new NoScreenPlatform();
    public IAppVolumePlatform AppVolumes { get; } = new NoAppVolumePlatform();
    public IShellPlatform Shell { get; } = new NoShellPlatform();
    public IConnectivityPlatform Connectivity { get; } = new NoConnectivityPlatform();
    public ICapsLockPlatform CapsLock { get; } = new NoCapsLockPlatform();
    public IHotKeyPlatform HotKey { get; } = new NoHotKeyPlatform();
    public IRemovableDrivesPlatform Drives { get; } = new NoRemovableDrivesPlatform();
    public IClickAwayPlatform ClickAway { get; } = new NoClickAwayPlatform();
}
