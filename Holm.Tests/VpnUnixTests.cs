using System.Net;
using System.Net.NetworkInformation;
using WinNotch;

namespace Holm.Tests;

/// <summary>Tunnels as Linux and macOS name them.</summary>
public class VpnUnixTests
{
    static NetAdapter Up(string name, params string[] addresses)
        => new(name, name, NetworkInterfaceType.Unknown, true, addresses.Select(IPAddress.Parse).ToArray());

    [Fact]
    public void Tunnels_with_an_IPv4_address_are_VPNs()
    {
        Assert.Equal("WireGuard", VpnNames.UnixTunnel(new[] { Up("eth0", "192.168.1.5"), Up("wg0", "10.0.0.2") }));
        Assert.Equal("VPN", VpnNames.UnixTunnel(new[] { Up("tun0", "10.8.0.2") }));
        Assert.Equal("VPN", VpnNames.UnixTunnel(new[] { Up("utun4", "10.8.0.2", "fe80::1") }));
    }

    [Fact]
    public void macOS_own_tunnels_with_only_link_local_addresses_are_not()
        => Assert.Null(VpnNames.UnixTunnel(new[] { Up("utun0", "fe80::1"), Up("utun1", "fe80::2"), Up("en0", "192.168.1.5") }));
}
