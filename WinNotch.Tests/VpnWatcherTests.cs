using System.Net;
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
        new("Tailscale", "Tailscale Tunnel", (NetworkInterfaceType)53, true, new[] { IPAddress.Parse("fe80::1eb9:e153:2cff:7fa6") }), // running, not in a tailnet
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

    static NetAdapter Tailscale(bool exitNode, params string[] addresses)
        => new("Tailscale", "Tailscale Tunnel", (NetworkInterfaceType)53, true, addresses.Select(IPAddress.Parse).ToArray(), exitNode);

    static IEnumerable<NetAdapter> WithoutTailscale => Ordinary.Where(a => a.Name != "Tailscale");

    [Fact]
    public void Tailscale_connected_to_a_tailnet_counts()
        => Assert.Equal("Tailscale", VpnWatcher.Find(WithoutTailscale.Append(Tailscale(false, "fe80::1", "fd7a:115c:a1e0::dc38:d725", "100.70.215.36"))));

    [Fact]
    public void Tailscale_with_an_exit_node_says_so()
        => Assert.Equal("Tailscale · exit node", VpnWatcher.Find(WithoutTailscale.Append(Tailscale(true, "100.70.215.36"))));

    [Fact]
    public void Tailscale_without_a_tailnet_address_does_not_count()
        => Assert.Null(VpnWatcher.Find(WithoutTailscale.Append(Tailscale(false, "fe80::1", "169.254.12.3"))));

    [Fact]
    public void A_full_VPN_wins_over_Tailscale_for_the_tailnet_only()
        => Assert.Equal("NordVPN", VpnWatcher.Find(WithoutTailscale.Append(Tailscale(false, "100.70.215.36")).Append(Up("NordLynx", "NordLynx Tunnel"))));

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.0.1", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("fd7a:115c:a1e0::1", true)]
    [InlineData("fd7a:115c:a1e1::1", false)]
    public void Knows_tailnet_addresses(string address, bool expected)
        => Assert.Equal(expected, VpnWatcher.IsTailnetAddress(IPAddress.Parse(address)));

    [Fact]
    public void A_disconnected_VPN_adapter_is_not_a_VPN()
        => Assert.Null(VpnWatcher.Find(Ordinary.Append(new NetAdapter("NordLynx", "NordLynx Tunnel", NetworkInterfaceType.Ethernet, false))));
}
