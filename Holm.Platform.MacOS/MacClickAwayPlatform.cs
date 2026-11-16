using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Clicks in other apps, from a global mouse-down monitor (no permission needed for the mouse): macOS only hands
/// such a monitor events meant for other apps, so a click on Holm itself never arrives here.
/// </summary>
public sealed unsafe class MacClickAwayPlatform : IClickAwayPlatform
{
    const ulong LeftMouseDown = 1 << 1, RightMouseDown = 1 << 3, OtherMouseDown = 1UL << 25;
    static MacClickAwayPlatform? _current;
    static IntPtr _monitor;
    volatile bool _watching;

    public event Action? ClickedElsewhere;

    public void Watch(bool on)
    {
        _watching = on;
        _current = this;
        if (!on || _monitor != IntPtr.Zero) return;
        try
        {
            delegate* unmanaged<IntPtr, IntPtr, void> handler = &OnMouseDown;
            _monitor = ObjC.Send(ObjC.Class("NSEvent"), ObjC.Sel("addGlobalMonitorForEventsMatchingMask:handler:"),
                LeftMouseDown | RightMouseDown | OtherMouseDown, ObjC.GlobalBlock((IntPtr)handler));
            if (_monitor != IntPtr.Zero) ObjC.Send(_monitor, ObjC.Sel("retain"));
        }
        catch { _monitor = IntPtr.Zero; }
    }

    [UnmanagedCallersOnly]
    static void OnMouseDown(IntPtr block, IntPtr e)
    {
        try { if (_current is { _watching: true } me) me.ClickedElsewhere?.Invoke(); } catch { }
    }
}
