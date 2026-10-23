using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>A network adapter, as far as the VPN check cares.</summary>
internal readonly record struct NetAdapter(string Name, string Description, NetworkInterfaceType Type, bool Up);

/// <summary>
/// Notices a VPN coming and going: Windows' own VPN connections (PPP adapters) and the tunnel adapters of the usual
/// VPN apps. Windows says when addresses change, so nothing is polled.
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
            return Find(NetworkInterface.GetAllNetworkInterfaces()
                .Select(n => new NetAdapter(n.Name, n.Description, n.NetworkInterfaceType, n.OperationalStatus == OperationalStatus.Up)));
        }
        catch (NetworkInformationException ex)
        {
            Diag.Log("Network adapters unavailable: " + ex.Message);
            return null;
        }
    }

    /// <summary>The name of the VPN among these adapters, or null if none is connected.</summary>
    public static string? Find(IEnumerable<NetAdapter> adapters)
    {
        foreach (var adapter in adapters)
        {
            if (!adapter.Up) continue;
            // Windows' own VPN connections (IKEv2, SSTP, L2TP, PPTP) carry the name you gave them.
            if (adapter.Type == NetworkInterfaceType.Ppp) return adapter.Name.Trim() is { Length: > 0 } name ? name : "VPN";
            string text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            if (NotVpn.Any(text.Contains)) continue;
            foreach (var (word, label) in Known)
                if (text.Contains(word)) return label;
            if (adapter.Type == NetworkInterfaceType.Tunnel) return "VPN";
        }
        return null;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _settle.Stop();
    }
}
