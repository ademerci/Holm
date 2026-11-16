using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// Internet connectivity from Windows' Network List Manager (the WPF app's NetworkWatcher, without the WPF
/// dispatcher): told the moment it changes, nothing is pinged or polled.
/// </summary>
public sealed class WindowsConnectivityPlatform : IConnectivityPlatform
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

    object? _manager;
    IConnectionPoint? _point;
    int _cookie;
    volatile int _state = -1;

    public event Action<bool>? Changed;
    public bool? Online => _state < 0 ? null : _state == 1;

    public void Start()
    {
        if (_manager != null) return;
        try
        {
            _manager = Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClass, throwOnError: true)!)!;
            _state = Outage.HasInternet(((INetworkListManager)_manager).GetConnectivity()) ? 1 : 0;
            var events = typeof(INetworkListManagerEvents).GUID;
            ((IConnectionPointContainer)_manager).FindConnectionPoint(ref events, out var point);
            _point = point;
            _point?.Advise(new Sink(OnChanged), out _cookie);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or NotSupportedException)
        {
            Diag.Log("Network list: " + ex.GetType().Name);
        }
    }

    void OnChanged(int connectivity)
    {
        int state = Outage.HasInternet(connectivity) ? 1 : 0;
        int before = _state;
        _state = state;
        if (before >= 0 && before != state) Changed?.Invoke(state == 1);
    }

    public void Stop()
    {
        if (_point != null)
        {
            try { _point.Unadvise(_cookie); } catch (COMException) { }
            Marshal.ReleaseComObject(_point);
            _point = null;
        }
        if (_manager != null) { Marshal.ReleaseComObject(_manager); _manager = null; }
        _state = -1;
    }
}
