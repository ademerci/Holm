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
/// Notices a VPN coming and going: Windows' own VPN connections (PPP adapters), the tunnel adapters of the usual
/// VPN apps, and Tailscale while it's connected to a tailnet. Windows says when addresses and routes change, so
/// nothing is polled.
/// </summary>
internal sealed class VpnWatcher : IDisposable
{
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
            return VpnNames.Find(NetworkInterface.GetAllNetworkInterfaces().Select(n => Describe(n, internet)));
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
