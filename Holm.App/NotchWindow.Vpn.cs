using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A small lock while a VPN is connected, in the idle notch and beside the batteries, and a word when it comes or
/// goes (the WPF app's VpnWatcher, with its rules in VpnNames linked). Every system says when addresses change
/// (.NET's NetworkChange), so nothing is polled. Linux and macOS name tunnels their own way: tun, wg, ppp, and on
/// macOS a utun that holds an IPv4 address (macOS keeps a few utun of its own, with only IPv6 link-local addresses).
/// </summary>
public sealed partial class NotchWindow
{
    const string LockGlyph = "";

    readonly TextBlock _idleLock = new() { Text = LockGlyph, FontFamily = IconFont, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly DispatcherTimer _vpnSettle = new() { Interval = TimeSpan.FromSeconds(1.5) };
    string? _vpn;
    bool _vpnWatching;

    void InitVpn()
    {
        // The right end of the idle notch: the reminder or the shelf, then the lock.
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in new Control[] { _idleReminder, _idleShelf })
        {
            _idleView.Children.Remove(part);
            right.Children.Add(part);
        }
        right.Children.Add(_idleLock);
        _idleView.Children.Add(right);
        // Connecting changes the addresses several times in a row; look once it has settled.
        _vpnSettle.Tick += (_, _) => { _vpnSettle.Stop(); CheckVpn(announce: true); };
    }

    void StartVpn()
    {
        if (_settings.Vpn && !_vpnWatching)
        {
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
            _vpnWatching = true;
        }
        else if (!_settings.Vpn && _vpnWatching)
        {
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            _vpnWatching = false;
        }
        CheckVpn(announce: false);
    }

    void OnAddressChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { _vpnSettle.Stop(); _vpnSettle.Start(); });

    void CheckVpn(bool announce)
    {
        string? now = _settings.Vpn ? ScanVpn() : null;
        bool changed = now != _vpn;
        _vpn = now;
        _idleLock.IsVisible = now != null;
        RefreshDevices();
        if (!changed || !announce) return;
        Diag.Log(now == null ? "VPN disconnected" : $"VPN connected ({now})");
        if (now != null) ShowPeek(LockGlyph, "VPN connected", now == "VPN" ? "" : now, null, glyphBrush: Green);
        else ShowPeek(LockGlyph, "VPN disconnected", "", null);
    }

    static string? ScanVpn()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Select(Describe).ToList();
            return VpnNames.Find(adapters) ?? (OperatingSystem.IsWindows() ? null : VpnNames.UnixTunnel(adapters));
        }
        catch (NetworkInformationException ex)
        {
            Diag.Log("Network adapters unavailable: " + ex.Message);
            return null;
        }
    }

    static NetAdapter Describe(NetworkInterface n)
    {
        bool up = n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown; // tunnels often say Unknown
        IReadOnlyList<IPAddress>? addresses = null;
        if (up)
        {
            try { addresses = n.GetIPProperties().UnicastAddresses.Select(a => a.Address).ToArray(); }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException) { }
        }
        return new NetAdapter(n.Name, n.Description, n.NetworkInterfaceType, up, addresses);
    }

    void SetVpn(bool on)
    {
        _settings.Vpn = on;
        _settings.Save();
        StartVpn();
    }
}
