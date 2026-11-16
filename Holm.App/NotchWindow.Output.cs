using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Two buttons next to the volume in the open notch, as in the WPF app: Bluetooth (MainWindow.Bluetooth.cs) switches
/// Bluetooth on and off and connects or disconnects paired devices; the output button (MainWindow.xaml.cs) lists the
/// speakers and headphones and makes the one you pick the default.
/// </summary>
public sealed partial class NotchWindow
{
    const string BluetoothGlyph = "";
    NotchButton _bluetoothBtn = null!, _outputBtn = null!;
    readonly TextBlock _bluetoothIcon = new() { Text = BluetoothGlyph, FontFamily = IconFont, FontSize = 14, Foreground = Dim };
    readonly TextBlock _outputIcon = new() { Text = AudioOutput.Speakers, FontFamily = IconFont, FontSize = 14, Foreground = Dim };
    bool? _radioOn;
    bool _bluetoothListing;

    void BuildOutputButtons(Panel sound)
    {
        _bluetoothBtn = new NotchButton(_bluetoothIcon) { Width = 30, Height = 30, Margin = new Thickness(0, 0, 2, 0), IsVisible = false };
        _bluetoothBtn.Click += () => _ = ShowBluetoothMenuAsync();
        _outputBtn = new NotchButton(_outputIcon) { Width = 30, Height = 30, Margin = new Thickness(0, 0, 2, 0), IsVisible = false };
        _outputBtn.Click += ShowOutputMenu;
        sound.Children.Insert(0, _outputBtn);
        sound.Children.Insert(0, _bluetoothBtn);
    }

    /// <summary>When the notch opens: is there Bluetooth, is it on, and is there more than one place for sound.</summary>
    async void RefreshOutputButtons()
    {
        RefreshOutput();
        try { _radioOn = await _bluetoothPlatform.RadioAsync(); } catch { _radioOn = null; }
        ShowBluetoothState();
    }

    void ShowBluetoothState()
    {
        _bluetoothBtn.IsVisible = _radioOn != null;
        if (_radioOn == null) return;
        _bluetoothIcon.Foreground = _radioOn == true ? Dim : Faint;
        ToolTip.SetTip(_bluetoothBtn, _radioOn == true ? "Bluetooth" : "Bluetooth is off");
    }

    void RefreshOutput()
    {
        var outputs = _audio.Outputs(out string? current);
        var now = outputs.FirstOrDefault(o => o.Id == current);
        _outputBtn.IsVisible = outputs.Count > 1;
        _outputIcon.Text = now?.Glyph ?? AudioOutput.Speakers;
        ToolTip.SetTip(_outputBtn, now?.Name);
    }

    /// <summary>Lists the speakers and headphones; picking one makes it the default, like the sound settings do.</summary>
    void ShowOutputMenu()
    {
        var outputs = _audio.Outputs(out string? current);
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        foreach (var output in outputs)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = output.Glyph, FontFamily = IconFont, FontSize = 13, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            header.Children.Add(new TextBlock { Text = output.Name, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = output.Id == current };
            var chosen = output;
            item.Click += (_, _) =>
            {
                if (chosen.Id == current) return;
                Diag.Log("Output device picked");
                _audio.SetDefaultOutput(chosen.Id);
                RefreshOutput();
            };
            menu.Items.Add(item);
        }
        OpenButtonMenu(menu, _outputBtn);
    }

    /// <summary>The switch, then the paired devices with a tick at the connected ones; picking one connects or disconnects it.</summary>
    async Task ShowBluetoothMenuAsync()
    {
        if (_bluetoothListing || _radioOn == null) return;
        bool on = _radioOn == true;
        var paired = new List<PairedDevice>();
        if (on)
        {
            _bluetoothListing = true;
            try { paired = await Task.Run(_bluetoothPlatform.Paired); }
            catch (Exception ex) { Diag.Log("Bluetooth devices unavailable: " + ex.Message); }
            finally { _bluetoothListing = false; }
            if (!_hovered) return; // the notch closed while we looked
        }
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        var power = new MenuItem { Header = "Bluetooth", ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        power.Click += async (_, _) =>
        {
            Diag.Log(on ? "Bluetooth: off" : "Bluetooth: on");
            if (!await _bluetoothPlatform.SetRadioAsync(!on))
                ShowPeek(BluetoothGlyph, on ? "Can't turn Bluetooth off" : "Can't turn Bluetooth on", "", null);
            _radioOn = await _bluetoothPlatform.RadioAsync();
            ShowBluetoothState();
        };
        menu.Items.Add(power);
        if (on)
        {
            menu.Items.Add(new Separator());
            if (paired.Count == 0) menu.Items.Add(new MenuItem { Header = "No paired devices", IsEnabled = false });
            foreach (var device in paired.OrderByDescending(p => p.Connected)) menu.Items.Add(BluetoothItem(device));
        }
        if (_bluetoothPlatform.SettingsLink is { } link)
        {
            menu.Items.Add(new Separator());
            var settings = new MenuItem { Header = "Bluetooth settings" };
            settings.Click += (_, _) => { try { _shell.Open(link); } catch (Exception ex) { Diag.Log("Bluetooth settings: " + ex.Message); } };
            menu.Items.Add(settings);
        }
        OpenButtonMenu(menu, _bluetoothBtn);
    }

    MenuItem BluetoothItem(PairedDevice device)
    {
        // Taller than the menu's other rows, so the devices don't sit packed together.
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        header.Children.Add(new TextBlock { Text = device.Glyph, FontFamily = IconFont, FontSize = 13, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(new TextBlock { Text = device.Name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (device.Connected && device.Battery >= 0)
            header.Children.Add(new TextBlock { Text = $"{device.Battery}%", Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = device.Connected, IsEnabled = device.CanConnect };
        ToolTip.SetTip(item, device.CanConnect ? (device.Connected ? "Disconnect" : "Connect") : device.Connected ? "Connected" : "Connects by itself when it's on and nearby");
        item.Click += async (_, _) =>
        {
            bool connect = !device.Connected;
            Diag.Log(connect ? "Bluetooth device: connect" : "Bluetooth device: disconnect");
            bool asked = false;
            try { asked = await Task.Run(() => _bluetoothPlatform.SetConnected(device.Id, connect)); }
            catch (Exception ex) { Diag.Log("Bluetooth device unreachable: " + ex.Message); }
            if (!asked) ShowPeek(device.Glyph, (connect ? "Can't connect " : "Can't disconnect ") + device.Name, "", null);
            else RefreshDevices(); // the batteries follow
        };
        return item;
    }

    /// <summary>A button's menu: the notch stays open while it is.</summary>
    void OpenButtonMenu(ContextMenu menu, Control button)
    {
        if (menu.Items.Count == 0) return;
        _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_hovered && !_island.IsPointerOver && !_typing) SetExpanded(false);
        };
        menu.Open(button);
    }
}
