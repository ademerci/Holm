using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Internet connectivity from the Network framework's path monitor (nw_path_monitor), which macOS calls whenever
/// the route to the internet changes. Its update handler is a block, called on a system queue.
/// </summary>
public sealed unsafe class MacConnectivityPlatform : IConnectivityPlatform
{
    const string Network = "/System/Library/Frameworks/Network.framework/Network";
    const string LibSystem = "/usr/lib/libSystem.B.dylib";
    const int StatusSatisfied = 1;

    static MacConnectivityPlatform? _current;
    IntPtr _monitor;
    volatile int _state = -1;

    public event Action<bool>? Changed;
    public bool? Online => _state < 0 ? null : _state == 1;

    public void Start()
    {
        if (_monitor != IntPtr.Zero) return;
        _current = this;
        try
        {
            _monitor = nw_path_monitor_create();
            if (_monitor == IntPtr.Zero) return;
            delegate* unmanaged<IntPtr, IntPtr, void> handler = &OnPath;
            nw_path_monitor_set_update_handler(_monitor, ObjC.GlobalBlock((IntPtr)handler));
            nw_path_monitor_set_queue(_monitor, dispatch_get_global_queue(0, 0));
            nw_path_monitor_start(_monitor);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _monitor = IntPtr.Zero; }
    }

    public void Stop()
    {
        if (_monitor == IntPtr.Zero) return;
        nw_path_monitor_cancel(_monitor);
        _monitor = IntPtr.Zero;
        _state = -1;
    }

    [UnmanagedCallersOnly]
    static void OnPath(IntPtr block, IntPtr path)
    {
        try
        {
            if (_current is not { } me) return;
            int state = nw_path_get_status(path) == StatusSatisfied ? 1 : 0;
            int before = me._state;
            me._state = state;
            if (before >= 0 && before != state) me.Changed?.Invoke(state == 1);
        }
        catch { }
    }

    [DllImport(Network)] static extern IntPtr nw_path_monitor_create();
    [DllImport(Network)] static extern void nw_path_monitor_set_update_handler(IntPtr monitor, IntPtr block);
    [DllImport(Network)] static extern void nw_path_monitor_set_queue(IntPtr monitor, IntPtr queue);
    [DllImport(Network)] static extern void nw_path_monitor_start(IntPtr monitor);
    [DllImport(Network)] static extern void nw_path_monitor_cancel(IntPtr monitor);
    [DllImport(Network)] static extern int nw_path_get_status(IntPtr path);
    [DllImport(LibSystem)] static extern IntPtr dispatch_get_global_queue(nint identifier, nuint flags);
}
