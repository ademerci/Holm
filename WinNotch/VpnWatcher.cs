using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// A network adapter, as far as the VPN check cares. <paramref name="Addresses"/> is null when unknown;
/// <paramref name="CarriesInternet"/> says the internet is reached through it (a full tunnel, an exit node).
/// </summary>
internal readonly record struct NetAdapter(string Name, string Description, NetworkInterfaceType Type, bool Up,
    IReadOnlyList<IPAddress>? Addresses = null, bool CarriesInternet = false);

/// <summary>
/// Notices a VPN coming and going: Windows' own VPN connections (PPP adapters), the tunnel adapters of the usual
/// VPN apps, and Tailscale while it's connected to a tailnet. Windows says when addresses and routes change, so
/// nothing is polled.
/// </summary>
internal sealed class VpnWatcher : IDisposable
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

    readonly Dispatcher _ui;
    readonly DispatcherTimer _settle;
    readonly RouteChangeCallback _routeChanged; // kept alive while Windows may call it
    IntPtr _routeNotification;

    /// <summary>The VPN connected now ("NordVPN", "Work VPN", "VPN" when it can't tell), or null.</summary>
    public string? Current { get; private set; }

    /// <summary>A VPN connected (its name) or disconnected (null). Raised on the UI thread.</summary>
    public event Action<string?>? Changed;

    public VpnWatcher(Dispatcher ui)
    {
        _ui = ui;
        // Connecting changes the addresses several times in a row; look once it has settled.
        _settle = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => { _settle!.Stop(); Check(); }, ui);
        _settle.Stop();
        Current = Scan();
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        // Turning a Tailscale exit node on or off changes only the routes, not the addresses.
        _routeChanged = (_, _, _) => OnAddressChanged(null, EventArgs.Empty);
        try
        {
            if (NotifyRouteChange2(0 /* AF_UNSPEC */, _routeChanged, IntPtr.Zero, false, out _routeNotification) != 0)
                _routeNotification = IntPtr.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Diag.Log("Route changes unavailable: " + ex.GetType().Name);
        }
    }

    void OnAddressChanged(object? sender, EventArgs e) => _ui.InvokeAsync(() => { _settle.Stop(); _settle.Start(); });

    void Check()
    {
        string? now = Scan();
        if (now == Current) return;
        Current = now;
        Diag.Log(now == null ? "VPN disconnected" : $"VPN connected ({now})");
        Changed?.Invoke(now);
    }

    static string? Scan()
    {
        try
        {
            int? internet = InternetInterfaceIndex();
            return Find(NetworkInterface.GetAllNetworkInterfaces().Select(n => Describe(n, internet)));
        }
        catch (NetworkInformationException ex)
        {
            Diag.Log("Network adapters unavailable: " + ex.Message);
            return null;
        }
    }

    static NetAdapter Describe(NetworkInterface n, int? internet)
    {
        bool up = n.OperationalStatus == OperationalStatus.Up;
        IReadOnlyList<IPAddress>? addresses = null;
        bool carries = false;
        if (up)
        {
            try
            {
                var ip = n.GetIPProperties();
                addresses = ip.UnicastAddresses.Select(a => a.Address).ToArray();
                carries = internet is { } index && ip.GetIPv4Properties()?.Index == index;
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException) { }
        }
        return new NetAdapter(n.Name, n.Description, n.NetworkInterfaceType, up, addresses, carries);
    }

    /// <summary>The adapter Windows would send internet traffic through (towards a public address), if it says.</summary>
    static int? InternetInterfaceIndex()
    {
        try
        {
            uint destination = BitConverter.ToUInt32(new byte[] { 1, 1, 1, 1 }, 0); // in network order already
            return GetBestInterface(destination, out uint index) == 0 ? (int)index : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate void RouteChangeCallback(IntPtr context, IntPtr row, int notificationType);

    [DllImport("iphlpapi.dll")]
    static extern int NotifyRouteChange2(ushort family, RouteChangeCallback callback, IntPtr context,
        [MarshalAs(UnmanagedType.U1)] bool initialNotification, out IntPtr handle);

    [DllImport("iphlpapi.dll")]
    static extern int CancelMibChangeNotify2(IntPtr handle);

    [DllImport("iphlpapi.dll")]
    static extern int GetBestInterface(uint destination, out uint index);

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        if (_routeNotification != IntPtr.Zero)
        {
            CancelMibChangeNotify2(_routeNotification); // waits for a callback that is running
            _routeNotification = IntPtr.Zero;
        }
        _settle.Stop();
    }
}
