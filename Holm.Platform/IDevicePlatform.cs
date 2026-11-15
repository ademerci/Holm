using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>The computer's own battery: whether there is one, how full, and whether it is plugged in.</summary>
public readonly record struct PowerState(bool HasBattery, int Percent, bool OnAc, bool Charging);

/// <summary>
/// The computer's battery (Windows: GetSystemPowerStatus; Linux: UPower; macOS: IOKit). Event driven: a backend
/// reports plugging in and unplugging and must not poll.
/// </summary>
public interface IPowerPlatform
{
    /// <summary>Plugged in, unplugged or the battery state changed; raised on any thread.</summary>
    event Action? Changed;

    void Start();
    void Stop();

    /// <summary>The state right now; cheap enough for the UI thread.</summary>
    PowerState Read();
}

/// <summary>A Bluetooth device and the battery level the system knows for it.</summary>
/// <param name="Glyph">The icon for the kind of device (headphones, mouse, keyboard, ...), as a glyph of the icon font.</param>
/// <param name="Battery">0 to 100.</param>
public sealed record BluetoothDeviceState(string Address, string Name, string Glyph, int Battery, bool IsConnected);

/// <summary>Bluetooth devices and their batteries (Windows: the device registry; Linux: BlueZ; macOS: IOBluetooth).</summary>
public interface IBluetoothPlatform
{
    /// <summary>
    /// The devices whose battery level the system knows. May take a moment: call it off the UI thread. It reads
    /// what the system already has and never talks to the radio.
    /// </summary>
    List<BluetoothDeviceState> ScanBatteries();
}

/// <summary>For systems without a backend yet: no battery.</summary>
public sealed class NoPowerPlatform : IPowerPlatform
{
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public PowerState Read() => default;
}

/// <summary>For systems without a backend yet: no devices.</summary>
public sealed class NoBluetoothPlatform : IBluetoothPlatform
{
    public List<BluetoothDeviceState> ScanBatteries() => new();
}
