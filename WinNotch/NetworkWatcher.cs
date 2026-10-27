using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Whether this PC reaches the internet, told by Windows' Network List Manager the moment it changes
/// (INetworkListManagerEvents.ConnectivityChanged): nothing is pinged or polled.
/// </summary>
internal sealed class NetworkWatcher : IDisposable
{
    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface INetworkListManager
    {
        [DispId(1)] object GetNetworks(int flags);
        [DispId(2)] object GetNetwork(Guid id);
        [DispId(3)] object GetNetworkConnections();
        [DispId(4)] object GetNetworkConnection(Guid id);
        [DispId(5)] bool IsConnectedToInternet { get; }
        [DispId(6)] bool IsConnected { get; }
        [DispId(7)] int GetConnectivity();
    }

    [ComImport, Guid("DCB00001-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INetworkListManagerEvents
    {
        void ConnectivityChanged(int newConnectivity);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Sink : INetworkListManagerEvents
    {
        readonly Action<int> _changed;
        public Sink(Action<int> changed) => _changed = changed;
        public void ConnectivityChanged(int newConnectivity) => _changed(newConnectivity);
    }

    static readonly Guid ManagerClass = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    readonly Dispatcher _dispatcher;
    readonly object _manager;
    readonly IConnectionPoint _point;
    readonly int _cookie;

    /// <summary>Raised on the UI thread with whether the internet is reachable now.</summary>
    public event Action<bool>? Changed;

    public bool Online { get; private set; }

    public NetworkWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _manager = Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClass, throwOnError: true)!)!;
        Online = Outage.HasInternet(((INetworkListManager)_manager).GetConnectivity());
        var events = typeof(INetworkListManagerEvents).GUID;
        ((IConnectionPointContainer)_manager).FindConnectionPoint(ref events, out var point);
        _point = point ?? throw new COMException("No connectivity events");
        _point.Advise(new Sink(OnChanged), out _cookie);
    }

    void OnChanged(int connectivity) => _dispatcher.InvokeAsync(() =>
    {
        bool online = Outage.HasInternet(connectivity);
        if (online == Online) return;
        Online = online;
        Changed?.Invoke(online);
    });

    public void Dispose()
    {
        try { _point.Unadvise(_cookie); } catch (COMException) { }
        Marshal.ReleaseComObject(_point);
        Marshal.ReleaseComObject(_manager);
    }
}

/// <summary>
/// Internet outages as the notch tells them: one that lasts past a short grace (a Wi-Fi roam isn't one), and its
/// end with how long it took. Around sleep the network drops anyway, so that is hushed.
/// </summary>
internal sealed class Outage
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    const int IPV4_INTERNET = 0x40, IPV6_INTERNET = 0x400;

    DateTime? _lostAt;
    DateTime _hushedUntil;
    bool _told;

    /// <summary>NLM_CONNECTIVITY flags: internet over IPv4 or IPv6. A captive portal or local network only is not.</summary>
    public static bool HasInternet(int connectivity) => (connectivity & (IPV4_INTERNET | IPV6_INTERNET)) != 0;

    /// <summary>The PC goes to sleep or just woke up: drops until then are no outages.</summary>
    public void Hush(DateTime until)
    {
        _hushedUntil = until;
        _lostAt = null;
        _told = false;
    }

    public void Lost(DateTime now)
    {
        if (now < _hushedUntil || _lostAt != null) return;
        _lostAt = now;
        _told = false;
    }

    /// <summary>After the grace: whether to say it's gone (once).</summary>
    public bool StillLost(DateTime now)
    {
        if (_lostAt is not { } since || _told || now - since < Grace || now < _hushedUntil) return false;
        _told = true;
        return true;
    }

    /// <summary>It's back: how long it was gone, if that was told; null otherwise.</summary>
    public TimeSpan? Back(DateTime now)
    {
        var since = _lostAt;
        bool told = _told;
        _lostAt = null;
        _told = false;
        return since is { } start && told ? now - start : null;
    }

    /// <summary>"40 s", "3 min", "1 h 5 min".</summary>
    public static string Describe(TimeSpan length)
    {
        if (length < TimeSpan.FromMinutes(1)) return $"{Math.Max(1, (int)length.TotalSeconds)} s";
        if (length < TimeSpan.FromHours(1)) return $"{(int)length.TotalMinutes} min";
        int minutes = length.Minutes;
        return minutes == 0 ? $"{(int)length.TotalHours} h" : $"{(int)length.TotalHours} h {minutes} min";
    }
}
