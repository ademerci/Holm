using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinNotch;

/// <summary>
/// A network adapter, as far as the VPN check cares. <paramref name="Addresses"/> is null when unknown;
/// <paramref name="CarriesInternet"/> says the internet is reached through it (a full tunnel, an exit node).
/// </summary>
internal readonly record struct NetAdapter(string Name, string Description, NetworkInterfaceType Type, bool Up,
    IReadOnlyList<IPAddress>? Addresses = null, bool CarriesInternet = false);

/// <summary>Which adapter is a VPN and what to call it: the rules of VpnWatcher, without its Windows plumbing.</summary>
internal static class VpnNames
{
    /// <summary>Words in an adapter's name or description, and the name to show for it. The first match wins.</summary>
    static readonly (string Word, string Label)[] Known =
    {
        ("nordlynx", "NordVPN"), ("nordvpn", "NordVPN"), ("protonvpn", "Proton VPN"), ("proton vpn", "Proton VPN"),
        ("mullvad", "Mullvad"), ("expressvpn", "ExpressVPN"), ("surfshark", "Surfshark"), ("cyberghost", "CyberGhost"),
        ("windscribe", "Windscribe"), ("private internet access", "PIA"), ("cloudflare warp", "Cloudflare WARP"),
        ("anyconnect", "Cisco AnyConnect"), ("cisco", "Cisco VPN"), ("pangp", "GlobalProtect"), ("globalprotect", "GlobalProtect"),
        ("forticlient", "FortiClient"), ("fortinet", "FortiClient"), ("pulse secure", "Pulse Secure"), ("ivanti", "Ivanti"),
        ("sonicwall", "SonicWall"), ("check point", "Check Point"), ("zscaler", "Zscaler"),
        ("wireguard", "WireGuard"), ("openvpn", "OpenVPN"), ("tap-windows", "OpenVPN"), ("wintun", "VPN"), ("vpn", "VPN"),
    };

    /// <summary>Virtual adapters that aren't VPNs, even when they're tunnels.</summary>
    static readonly string[] NotVpn =
    {
        "hyper-v", "vethernet", "virtualbox", "vmware", "loopback", "teredo", "isatap", "6to4", "ip-https",
        "bluetooth", "wsl", "docker", "npcap", "kernel debug",
    };

    /// <summary>The name of the VPN among these adapters, or null if none is connected.</summary>
    public static string? Find(IEnumerable<NetAdapter> adapters)
    {
        string? tailnet = null; // Tailscale for the tailnet only: shown unless a VPN that carries everything is on too
        foreach (var adapter in adapters)
        {
            if (!adapter.Up) continue;
            // Windows' own VPN connections (IKEv2, SSTP, L2TP, PPTP) carry the name you gave them.
            if (adapter.Type == NetworkInterfaceType.Ppp) return adapter.Name.Trim() is { Length: > 0 } name ? name : "VPN";
            string text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            if (NotVpn.Any(text.Contains)) continue;
            if (text.Contains("tailscale"))
            {
                // Its adapter stays up while Tailscale runs; it's connected while it holds a tailnet address.
                if (adapter.Addresses is { } addresses && !addresses.Any(IsTailnetAddress)) continue;
                if (adapter.CarriesInternet) return "Tailscale · exit node";
                tailnet = "Tailscale";
                continue;
            }
            foreach (var (word, label) in Known)
                if (text.Contains(word)) return label;
            if (adapter.Type == NetworkInterfaceType.Tunnel) return "VPN";
        }
        return tailnet;
    }

    /// <summary>Tailscale's addresses: 100.64.0.0/10 (shared address space) and fd7a:115c:a1e0::/48.</summary>
    public static bool IsTailnetAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] b = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => b[0] == 100 && (b[1] & 0xC0) == 64,
            AddressFamily.InterNetworkV6 => b[0] == 0xFD && b[1] == 0x7A && b[2] == 0x11 && b[3] == 0x5C && b[4] == 0xA1 && b[5] == 0xE0,
            _ => false,
        };
    }
}
