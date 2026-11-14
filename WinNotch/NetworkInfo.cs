using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinNotch;

/// <summary>The settings of the adapter that reaches the internet: its addresses, gateway and DNS servers.</summary>
internal sealed record NetworkDetails(string? Adapter, string? Mac, IReadOnlyList<IPAddress> Addresses,
    IReadOnlyList<IPAddress> Gateways, IReadOnlyList<IPAddress> Dns, bool? Dhcp, string? DnsSuffix);

/// <summary>
/// What Windows knows about the connection, read on this PC only: nothing is sent anywhere to find it out.
/// </summary>
internal static class NetworkInfo
{
    /// <summary>The adapter Windows uses for the internet (by its id, from <see cref="SpeedTest.CurrentLink"/>), else the first with a gateway.</summary>
    public static NetworkDetails? Read(Guid? adapterId)
    {
        try
        {
            var up = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();
            var nic = adapterId is { } id ? up.FirstOrDefault(n => Guid.TryParse(n.Id, out var g) && g == id) : null;
            nic ??= up.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => !IsUnspecified(g.Address)));
            if (nic == null) return null;
            var ip = nic.GetIPProperties();
            var addresses = ip.UnicastAddresses
                .Where(a => !a.Address.IsIPv6LinkLocal && !IPAddress.IsLoopback(a.Address))
                .OrderBy(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 ? 1 : 0)
                .ThenBy(a => a.SuffixOrigin == SuffixOrigin.Random ? 1 : 0) // IPv6 privacy addresses last
                .Select(a => a.Address)
                .ToArray();
            bool? dhcp = null;
            try { dhcp = ip.GetIPv4Properties()?.IsDhcpEnabled; }
            catch (NetworkInformationException) { }
            return new NetworkDetails(
                nic.Description,
                FormatMac(nic.GetPhysicalAddress().GetAddressBytes()),
                addresses,
                ip.GatewayAddresses.Select(g => g.Address).Where(a => !IsUnspecified(a)).ToArray(),
                ip.DnsAddresses.Where(a => !a.IsIPv6SiteLocal).ToArray(), // fec0:0:0:ffff::1–3 are Windows' placeholders
                dhcp,
                string.IsNullOrWhiteSpace(ip.DnsSuffix) ? null : ip.DnsSuffix);
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            Diag.Log("Network details: " + ex.GetType().Name);
            return null;
        }
    }

    static bool IsUnspecified(IPAddress a) => a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any);

    /// <summary>"A4-5E-60-12-AB-CD", or null for adapters without one (tunnels).</summary>
    public static string? FormatMac(byte[] bytes)
        => bytes.Length == 0 || bytes.All(b => b == 0) ? null : string.Join("-", bytes.Select(b => b.ToString("X2")));

    /// <summary>An address without its scope ("fe80::1%12" → "fe80::1"): the scope means nothing to people.</summary>
    public static string Show(IPAddress address)
    {
        string text = address.ToString();
        int percent = text.IndexOf('%');
        return percent < 0 ? text : text[..percent];
    }

    /// <summary>The lines of the Wi-Fi page, label and value; a line is left out when its value isn't known.</summary>
    public static IReadOnlyList<(string Label, string Value)> Rows(NetworkLink? link, NetworkDetails? details)
    {
        var rows = new List<(string, string)>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, value)); }

        if (link == null && details == null) return rows;
        Add("Connection", link == null ? null : link.IsWifi ? "Wi-Fi" : "Ethernet");
        if (link?.IsWifi == true)
        {
            Add("Network", string.IsNullOrWhiteSpace(link.Ssid) ? "Name hidden by Windows (location is off)" : link.Ssid.Trim());
            Add("Signal", link.SignalBars is { } bars ? $"{bars} of 5 bars" : null);
        }
        if (link is { LinkBitsPerSecond: > 0 }) Add("Link speed", SpeedTest.FormatLinkSpeed(link.LinkBitsPerSecond));
        if (details != null)
        {
            Add("IPv4 address", details.Addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(Show).FirstOrDefault());
            Add("IPv6 address", details.Addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).Select(Show).FirstOrDefault());
            Add("Gateway", string.Join(", ", details.Gateways.Select(Show).Distinct()));
            Add("DNS servers", string.Join(", ", details.Dns.Select(Show).Distinct()));
            Add("DHCP", details.Dhcp switch { true => "On", false => "Off (fixed address)", null => null });
            Add("DNS suffix", details.DnsSuffix);
            Add("MAC address", details.Mac);
            Add("Adapter", details.Adapter);
        }
        return rows;
    }
}
