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
