using System.Net.NetworkInformation;

namespace WinNotch.Tests;

public class VpnWatcherTests
{
    static NetAdapter Up(string name, string description, NetworkInterfaceType type = NetworkInterfaceType.Ethernet)
        => new(name, description, type, true);

    // What a PC without a VPN typically has (this one, for instance).
    static readonly NetAdapter[] Ordinary =
    {
        Up("WLAN", "Intel(R) Wi-Fi 7 BE201 320MHz", NetworkInterfaceType.Wireless80211),
        Up("Loopback Pseudo-Interface 1", "Software Loopback Interface 1", NetworkInterfaceType.Loopback),
        Up("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter"),
        Up("vEthernet (WSL (Hyper-V firewall))", "Hyper-V Virtual Ethernet Adapter #2"),
        Up("Teredo Tunneling Pseudo-Interface", "Teredo Tunneling Pseudo-Interface", NetworkInterfaceType.Tunnel),
        Up("Bluetooth Network Connection", "Bluetooth Device (Personal Area Network)"),
        Up("Tailscale", "Tailscale Tunnel", (NetworkInterfaceType)53),
        new("Ethernet 3", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet, false), // installed, not connected
    };

    [Fact]
    public void No_VPN_on_an_ordinary_PC() => Assert.Null(VpnWatcher.Find(Ordinary));

    [Theory]
    [InlineData("NordLynx", "NordLynx Tunnel", "NordVPN")]
    [InlineData("ProtonVPN", "ProtonVPN Tunnel", "Proton VPN")]
    [InlineData("wg-home", "WireGuard Tunnel", "WireGuard")]
    [InlineData("Ethernet 3", "TAP-Windows Adapter V9", "OpenVPN")]
    [InlineData("Ethernet 4", "Cisco AnyConnect Secure Mobility Client Virtual Miniport Adapter for Windows x64", "Cisco AnyConnect")]
    [InlineData("Ethernet 5", "PANGP Virtual Ethernet Adapter Secure", "GlobalProtect")]
    [InlineData("Mullvad", "Mullvad Tunnel", "Mullvad")]
    [InlineData("Ethernet 6", "Some Company VPN Adapter", "VPN")]
    public void The_usual_VPN_apps_are_recognised(string name, string description, string expected)
        => Assert.Equal(expected, VpnWatcher.Find(Ordinary.Append(Up(name, description))));

    [Fact]
    public void Windows_own_VPN_connections_show_the_name_you_gave_them()
        => Assert.Equal("Work VPN", VpnWatcher.Find(Ordinary.Append(Up("Work VPN", "Work VPN", NetworkInterfaceType.Ppp))));

    [Fact]
    public void An_unknown_tunnel_counts_as_a_VPN()
        => Assert.Equal("VPN", VpnWatcher.Find(Ordinary.Append(Up("Tunnel 2", "Some tunnel", NetworkInterfaceType.Tunnel))));

    [Fact]
    public void A_disconnected_VPN_adapter_is_not_a_VPN()
        => Assert.Null(VpnWatcher.Find(Ordinary.Append(new NetAdapter("NordLynx", "NordLynx Tunnel", NetworkInterfaceType.Ethernet, false))));
}
