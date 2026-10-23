using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Windows.Devices.Radios;

namespace WinNotch;

/// <summary>
/// Bluetooth: a button next to the volume in the open notch switches Bluetooth on and off and connects or
/// disconnects your paired headphones and speakers, without a trip to the Settings app.
/// </summary>
public partial class MainWindow
{
    static readonly Brush FaintBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    Radio? _radio; // null on a PC without Bluetooth: then there's no button
    DateTime _radioLookedAt = DateTime.MinValue;
    bool _radioLookup, _bluetoothListing;
    ContextMenu? _bluetoothMenu;

    /// <summary>Finds the radio once (again a minute later if there was none, say a dongle plugged in) and shows its state.</summary>
    async void RefreshBluetooth()
    {
        if (_radio == null && !_radioLookup && DateTime.UtcNow - _radioLookedAt > TimeSpan.FromMinutes(1))
        {
            _radioLookup = true;
            var radio = await BluetoothControl.RadioAsync();
            _radioLookup = false;
            _radioLookedAt = DateTime.UtcNow;
            if (radio != null)
            {
                _radio = radio;
                radio.StateChanged += (_, _) => Dispatcher.InvokeAsync(ShowBluetoothState);
            }
        }
        ShowBluetoothState();
    }

    void ShowBluetoothState()
    {
        BluetoothBtn.Visibility = _radio != null ? Visibility.Visible : Visibility.Collapsed;
        if (_radio == null) return;
        bool on = _radio.State == RadioState.On;
        BluetoothIcon.Foreground = on ? DimBrush : FaintBrush;
        BluetoothBtn.ToolTip = on ? "Bluetooth" : "Bluetooth is off";
    }

    /// <summary>The switch, then the paired devices with a tick at the connected ones; picking one connects or disconnects it.</summary>
    async void Bluetooth_Click(object sender, RoutedEventArgs e)
    {
        var radio = _radio;
        if (radio == null || _bluetoothListing) return;
        bool on = radio.State == RadioState.On;
        var paired = new List<BtEntry>();
        if (on)
        {
            _bluetoothListing = true;
            try { paired = await Shell.RunAsync(BluetoothControl.Paired); }
            catch (Exception ex) { Diag.Log("Bluetooth devices unavailable: " + ex.Message); }
            finally { _bluetoothListing = false; }
            if (!_expanded) return; // the notch closed while we looked
        }

        var menu = new ContextMenu { PlacementTarget = BluetoothBtn, Placement = PlacementMode.Bottom };
        var power = new MenuItem { Header = "Bluetooth", IsCheckable = true, IsChecked = on, IsEnabled = radio.State is RadioState.On or RadioState.Off };
        power.Click += async (_, _) =>
        {
            Diag.Log(on ? "Bluetooth: off" : "Bluetooth: on");
            if (!await BluetoothControl.SetRadioAsync(radio, !on))
                ShowPeek("\uE702", on ? "Can't turn Bluetooth off" : "Can't turn Bluetooth on", "", null, false, null);
            ShowBluetoothState();
        };
        menu.Items.Add(power);
        if (on)
        {
            menu.Items.Add(new Separator());
            if (paired.Count == 0) menu.Items.Add(new MenuItem { Header = "No paired devices", IsEnabled = false });
            foreach (var entry in paired.OrderByDescending(p => p.Device.Connected == true)) // by name within each
                menu.Items.Add(BluetoothItem(entry));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(SettingsItem("Add a device…", "ms-settings-connectabledevices:devicediscovery"));
        menu.Items.Add(SettingsItem("Bluetooth settings", "ms-settings:bluetooth"));
        menu.Closed += Menu_Closed;
        _bluetoothMenu = menu;
        menu.IsOpen = true;
    }

    MenuItem BluetoothItem(BtEntry entry)
    {
        var device = entry.Device;
        bool connected = device.Connected == true;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = device.Glyph, FontFamily = IconFont, FontSize = 13, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(new TextBlock { Text = device.Name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (connected && device.Battery >= 0)
            header.Children.Add(new TextBlock { Text = $"{device.Battery}%", Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });

        // Only sound devices can be told to connect; the others do it on their own when they're switched on.
        var item = new MenuItem
        {
            Header = header, IsChecked = connected, IsEnabled = entry.CanConnect,
            ToolTip = entry.CanConnect ? (connected ? "Disconnect" : "Connect")
                    : connected ? "Connected" : "Connects by itself when it's on and nearby",
        };
        ToolTipService.SetShowOnDisabled(item, true);
        item.Click += async (_, _) =>
        {
            bool connect = !connected;
            Diag.Log(connect ? "Bluetooth device: connect" : "Bluetooth device: disconnect");
            bool asked = false;
            try { asked = await Shell.RunAsync(() => BluetoothControl.SetConnected(device, connect)); }
            catch (Exception ex) { Diag.Log("Bluetooth device unreachable: " + ex.Message); }
            if (!asked)
            {
                ShowPeek(device.Glyph, (connect ? "Can't connect " : "Can't disconnect ") + device.Name, "", null, false, null);
                return;
            }
            // Windows takes a few seconds to connect; the batteries and the "connected" pop-out follow from the scans.
            _btSoon.Stop();
            _btSoon.Start();
            _btLate.Stop();
            _btLate.Start();
        };
        return item;
    }

    static MenuItem SettingsItem(string header, string link)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            try { Shell.Open(link); }
            catch (Exception ex)
            {
                Diag.Log($"Couldn't open {link}: {ex.Message}");
                try { Shell.Open("ms-settings:bluetooth"); } catch { }
            }
        };
        return item;
    }
}
