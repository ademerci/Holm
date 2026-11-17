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
    public event Action<bool>? Sleeping;

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
        // The WPF app's WM_POWERBROADCAST: status changes, and PBT_APMSUSPEND / PBT_APMRESUMEAUTOMATIC.
        if (e.Mode == PowerModes.StatusChange) Changed?.Invoke();
        else if (e.Mode == PowerModes.Suspend) Sleeping?.Invoke(true);
        else if (e.Mode == PowerModes.Resume) Sleeping?.Invoke(false);
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
    global::Windows.Devices.Radios.Radio? _radio;
    Dictionary<string, BtDevice> _paired = new();

    public List<BluetoothDeviceState> ScanBatteries()
        => BluetoothBattery.Scan().Select(d => new BluetoothDeviceState(d.Address, d.Name, d.Glyph, d.Battery, d.IsConnected)).ToList();

    /// <summary>The radio through the WPF app's BluetoothControl (linked).</summary>
    public async System.Threading.Tasks.Task<bool?> RadioAsync()
    {
        _radio ??= await BluetoothControl.RadioAsync();
        return _radio == null ? null : _radio.State == global::Windows.Devices.Radios.RadioState.On;
    }

    public async System.Threading.Tasks.Task<bool> SetRadioAsync(bool on)
        => _radio != null && await BluetoothControl.SetRadioAsync(_radio, on);

    public List<PairedDevice> Paired()
    {
        var entries = BluetoothControl.Paired();
        _paired = entries.GroupBy(e => e.Device.Address).ToDictionary(g => g.Key, g => g.First().Device);
        return entries.Select(e => new PairedDevice(e.Device.Address, e.Device.Name, e.Device.Glyph, e.Device.Connected == true, e.Device.Battery, e.CanConnect)).ToList();
    }

    public bool SetConnected(string id, bool connect) => _paired.TryGetValue(id, out var device) && BluetoothControl.SetConnected(device, connect);

    public string? SettingsLink => "ms-settings:bluetooth";
}

/// <summary>
/// The Run key, through the WPF app's Autostart (linked). Next to an installed Holm the port shares its entry and
/// settings, so it only shows the switch; with a data folder of its own (HOLM_DATA) it may change it.
/// </summary>
public sealed class WindowsAutostartPlatform : IAutostartPlatform
{
    public bool CanChange => !AppSettings.ReadOnly;

    public bool IsEnabled
    {
        get { try { return Autostart.IsEnabled; } catch { return false; } }
    }

    public void Set(bool enabled)
    {
        if (!CanChange) return;
        try { Autostart.Set(enabled); } catch { }
    }
}
