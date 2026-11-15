using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The computer's battery and the Bluetooth devices: chips at the bottom left of the open notch, and a line out of
/// the closed one when you plug in, run low or a device connects.
///
/// Bluetooth is read when the app starts and each time the notch has opened; nothing watches it in between yet.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly IBrush GreenBrush = Brush("#FF30D158"), RedBrush = Brush("#FFFF453A");

    IPowerPlatform _powerPlatform = null!;
    IBluetoothPlatform _bluetoothPlatform = null!;
    readonly WrapPanel _devicesPanel = new() { VerticalAlignment = VerticalAlignment.Center };
    PowerState _power;
    bool _powerKnown, _btKnown, _bluetoothScanInProgress;
    List<BluetoothDeviceState> _bt = new();

    void InitDevices(IPlatformServices platform)
    {
        _powerPlatform = platform.Power;
        _bluetoothPlatform = platform.Bluetooth;
        _powerPlatform.Changed += () => Dispatcher.UIThread.Post(ReadPower);
    }

    void StartDevices()
    {
        _powerPlatform.Start();
        ReadPower();
        ScanBluetooth();
    }

    void ReadPower()
    {
        var now = _powerPlatform.Read();
        var before = _power;
        bool known = _powerKnown;
        _power = now;
        _powerKnown = true;

        if (known && now.HasBattery)
        {
            if (now.OnAc && !before.OnAc)
                ShowPeek(null, "Charging", $"{now.Percent}%", GreenBrush, battery: now.Percent, charging: true);
            else if (!now.OnAc && now.Percent < before.Percent && now.Percent is 20 or 10 or 5)
                ShowPeek(null, "Low battery", $"{now.Percent}%", RedBrush, battery: now.Percent);
        }
        if (_state == NotchState.Expanded) RefreshDevices();
    }

    async void ScanBluetooth()
    {
        if (_bluetoothScanInProgress) return;
        _bluetoothScanInProgress = true;
        List<BluetoothDeviceState> found;
        try { found = await Task.Run(_bluetoothPlatform.ScanBatteries); }
        catch (Exception ex) { Diag.Log("Bluetooth scan failed: " + ex.Message); return; }
        finally { _bluetoothScanInProgress = false; }

        var previous = _bt.ToDictionary(d => d.Address, StringComparer.OrdinalIgnoreCase);
        _bt = found;

        if (_btKnown)
        {
            var arrived = found.FirstOrDefault(d => d.IsConnected && !(previous.TryGetValue(d.Address, out var p) && p.IsConnected));
            var low = found.FirstOrDefault(d => d.IsConnected && d.Battery <= 15
                                             && previous.TryGetValue(d.Address, out var p) && p.Battery > 15);
            if (arrived != null)
                ShowPeek(arrived.Glyph, arrived.Name, $"{arrived.Battery}%", null, battery: arrived.Battery);
            else if (low != null)
                ShowPeek(low.Glyph, low.Name, $"{low.Battery}%", RedBrush, battery: low.Battery);
        }
        _btKnown = true;
        if (_state == NotchState.Expanded) RefreshDevices();
    }

    void RefreshDevices()
    {
        int before = _devicesPanel.Children.Count;
        _devicesPanel.Children.Clear();
        if (_power.HasBattery)
            _devicesPanel.Children.Add(DeviceChip("", null, _power.Percent, _power.OnAc));
        foreach (var d in _bt.Where(d => d.IsConnected))
            _devicesPanel.Children.Add(DeviceChip(d.Glyph, d.Name, d.Battery, false));
        // Chips may wrap onto another line: the open notch gets taller or shorter.
        if (_devicesPanel.Children.Count != before && _state == NotchState.Expanded) ApplyState(animate: true);
    }

    static Control DeviceChip(string glyph, string? name, int level, bool charging)
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 16, 6), VerticalAlignment = VerticalAlignment.Center };
        chip.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        if (name != null)
            chip.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = Dim, MaxWidth = 100, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        chip.Children.Add(new BatteryGlyph { Level = level, Charging = charging, VerticalAlignment = VerticalAlignment.Center });
        chip.Children.Add(new TextBlock { Text = $"{level}%", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return chip;
    }
}
