using System;
using System.Collections.Generic;
using System.Linq;
using Holm.Platform;
using Microsoft.Win32;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// The battery through GetSystemPowerStatus (the WPF app's PowerInfo, linked from WinNotch/Native.cs). Windows
/// tells us when the power source or the battery state changes; nothing is polled.
/// </summary>
public sealed class WindowsPowerPlatform : IPowerPlatform
{
    bool _started;

    public event Action? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange) Changed?.Invoke();
    }

    public PowerState Read()
    {
        var info = PowerInfo.Read();
        return new PowerState(info.HasBattery, info.Percent, info.OnAc, info.Charging);
    }
}

/// <summary>Bluetooth batteries as Windows' Settings app shows them (the WPF app's BluetoothBattery, linked).</summary>
public sealed class WindowsBluetoothPlatform : IBluetoothPlatform
{
    public List<BluetoothDeviceState> ScanBatteries()
        => BluetoothBattery.Scan().Select(d => new BluetoothDeviceState(d.Address, d.Name, d.Glyph, d.Battery, d.IsConnected)).ToList();
}
