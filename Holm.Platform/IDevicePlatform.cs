using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>The computer's own battery: whether there is one, how full, and whether it is plugged in.</summary>
public readonly record struct PowerState(bool HasBattery, int Percent, bool OnAc, bool Charging);

/// <summary>
/// The computer's battery (Windows: GetSystemPowerStatus; Linux: UPower). Event driven: a backend
/// reports plugging in and unplugging and must not poll.
/// </summary>
public interface IPowerPlatform
{
    /// <summary>Plugged in, unplugged or the battery state changed; raised on any thread.</summary>
    event Action? Changed;

    /// <summary>The computer goes to sleep (true) or has woken up (false); raised on any thread.</summary>
    event Action<bool>? Sleeping;

    void Start();
    void Stop();

    /// <summary>The state right now; cheap enough for the UI thread.</summary>
    PowerState Read();
}

/// <summary>A Bluetooth device and the battery level the system knows for it.</summary>
/// <param name="Glyph">The icon for the kind of device (headphones, mouse, keyboard, ...), as a glyph of the icon font.</param>
/// <param name="Battery">0 to 100.</param>
public sealed record BluetoothDeviceState(string Address, string Name, string Glyph, int Battery, bool IsConnected);

/// <summary>Bluetooth devices and their batteries (Windows: the device registry; Linux: BlueZ).</summary>
public interface IBluetoothPlatform
{
    /// <summary>
    /// The devices whose battery level the system knows. May take a moment: call it off the UI thread. It reads
    /// what the system already has and never talks to the radio.
    /// </summary>
    List<BluetoothDeviceState> ScanBatteries();

    /// <summary>Whether Bluetooth is on; null when the computer has no Bluetooth (then there's no button).</summary>
    System.Threading.Tasks.Task<bool?> RadioAsync();

    /// <summary>Switches Bluetooth on or off, like the switch in the system's settings. False if that wasn't allowed.</summary>
    System.Threading.Tasks.Task<bool> SetRadioAsync(bool on);

    /// <summary>The paired devices. Run it off the UI thread.</summary>
    List<PairedDevice> Paired();

    /// <summary>Asks a paired device to connect or disconnect. False if the system didn't take the request.</summary>
    bool SetConnected(string id, bool connect);

    /// <summary>The system's Bluetooth settings, to open from the menu; null where there is no such link.</summary>
    string? SettingsLink { get; }
}

/// <summary>A paired Bluetooth device; <paramref name="CanConnect"/>: Holm can ask it to connect (sound devices on Windows).</summary>
public sealed record PairedDevice(string Id, string Name, string Glyph, bool Connected, int Battery, bool CanConnect);

/// <summary>For systems without a backend yet: no battery.</summary>
public sealed class NoPowerPlatform : IPowerPlatform
{
    public event Action? Changed { add { } remove { } }
    public event Action<bool>? Sleeping { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public PowerState Read() => default;
}

/// <summary>For systems without a backend yet: no devices.</summary>
public sealed class NoBluetoothPlatform : IBluetoothPlatform
{
    public List<BluetoothDeviceState> ScanBatteries() => new();
    public System.Threading.Tasks.Task<bool?> RadioAsync() => System.Threading.Tasks.Task.FromResult<bool?>(null);
    public System.Threading.Tasks.Task<bool> SetRadioAsync(bool on) => System.Threading.Tasks.Task.FromResult(false);
    public List<PairedDevice> Paired() => new();
    public bool SetConnected(string id, bool connect) => false;
    public string? SettingsLink => null;
}

/// <summary>
/// The icons for kinds of Bluetooth device, as glyphs of the icon font: the same ones the WPF app picks
/// (WinNotch/BluetoothBattery.cs), so a backend only has to say which kind a device is.
/// </summary>
public static class DeviceGlyphs
{
    public const string Headphones = "", Mouse = "", Keyboard = "", Controller = "",
        Pen = "", Phone = "", Computer = "", Other = "";
}
