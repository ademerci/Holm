using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// The overlay on macOS. The window sits at the status-bar level (above the menu bar and normal windows), on every
/// Space and next to full-screen apps, and is left out of the window cycle. The Dock icon is turned off by the app
/// itself (Program.cs).
///
/// macOS has no input shape: a window either takes the mouse or it doesn't. So it ignores the mouse, and two
/// event monitors (one for the mouse over other apps, one for the mouse over this window) switch it on while the
/// pointer is over the notch's region and off again once it leaves. They only listen to mouse movement, which
/// needs no permission; nothing is polled.
/// </summary>
public sealed unsafe class MacNotchWindowPlatform : INotchWindowPlatform
{
    const nint StatusWindowLevel = 25;
    const nuint CanJoinAllSpaces = 1 << 0, Stationary = 1 << 4, IgnoresCycle = 1 << 6, FullScreenAuxiliary = 1 << 8;
    const ulong MouseMoved = 1 << 5, LeftMouseDragged = 1 << 6, RightMouseDragged = 1 << 7;

    static MacNotchWindowPlatform? _current;
    static IntPtr _globalMonitor, _localMonitor;

    IntPtr _window;
    int _hitX, _hitY, _hitWidth, _hitHeight;
    bool _ignoring, _keyboard;
    IntPtr _previousApp;

    public void MakeOverlay(IntPtr handle)
    {
        _window = WindowOf(handle);
        if (_window == IntPtr.Zero) return;
        _current = this;
        ObjC.SendLong(_window, ObjC.Sel("setLevel:"), StatusWindowLevel);
        ObjC.SendULong(_window, ObjC.Sel("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary);
        ObjC.SendBool(_window, ObjC.Sel("setHidesOnDeactivate:"), 0);
        ObjC.SendBool(_window, ObjC.Sel("setHasShadow:"), 0);
        StartMonitors();
        _ignoring = false;
        Update();
    }

    public void SetHitRegion(IntPtr handle, int x, int y, int width, int height)
    {
        (_hitX, _hitY, _hitWidth, _hitHeight) = (x, y, width, height);
        Update();
    }

    public void SetKeyboard(IntPtr handle, bool take)
    {
        if (_window == IntPtr.Zero) return;
        IntPtr app = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
        if (take)
        {
            // Remember who had the keyboard, to hand it back.
            if (_previousApp != IntPtr.Zero) ObjC.Send(_previousApp, ObjC.Sel("release"));
            IntPtr workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace"));
            _previousApp = ObjC.Send(workspace, ObjC.Sel("frontmostApplication"));
            if (_previousApp != IntPtr.Zero) ObjC.Send(_previousApp, ObjC.Sel("retain"));
            _keyboard = true;
            Update();
            ObjC.SendBool(app, ObjC.Sel("activateIgnoringOtherApps:"), 1);
            ObjC.Send(_window, ObjC.Sel("makeKeyWindow"));
        }
        else
        {
            _keyboard = false;
            // Only while the keyboard is still ours: if the user clicked into another app, it already has it.
            if (_previousApp != IntPtr.Zero && ObjC.SendReturnBool(app, ObjC.Sel("isActive")) != 0)
                ObjC.SendULong(_previousApp, ObjC.Sel("activateWithOptions:"), 0);
            if (_previousApp != IntPtr.Zero) ObjC.Send(_previousApp, ObjC.Sel("release"));
            _previousApp = IntPtr.Zero;
            Update();
        }
    }

    /// <summary>Avalonia hands out the window's content view; the window is what gets the settings.</summary>
    static IntPtr WindowOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        if (ObjC.SendReturnBool(handle, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSWindow")) != 0) return handle;
        if (ObjC.SendReturnBool(handle, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSView")) != 0) return ObjC.Send(handle, ObjC.Sel("window"));
        return IntPtr.Zero;
    }

    /// <summary>Takes the mouse while the pointer is over the notch (or while one of its fields is being typed in).</summary>
    void Update()
    {
        if (_window == IntPtr.Zero) return;
        NSPoint mouse = ObjC.SendReturnPoint(ObjC.Class("NSEvent"), ObjC.Sel("mouseLocation"));
        NSRect frame = ObjC.SendReturnRect(_window, ObjC.Sel("frame"));
        double scale = ObjC.SendReturnDouble(_window, ObjC.Sel("backingScaleFactor"));
        bool inside = HitTest(mouse, frame, scale, _hitX, _hitY, _hitWidth, _hitHeight);
        bool ignore = !inside && !_keyboard;
        if (ignore == _ignoring) return;
        _ignoring = ignore;
        ObjC.SendBool(_window, ObjC.Sel("setIgnoresMouseEvents:"), ignore ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Whether a point on screen (Cocoa's coordinates: points, origin bottom left) lies in a region given in device
    /// pixels from the window's top left corner.
    /// </summary>
    internal static bool HitTest(NSPoint mouse, NSRect frame, double scale, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        if (scale <= 0) scale = 1;
        double px = (mouse.X - frame.X) * scale;
        double py = (frame.Y + frame.Height - mouse.Y) * scale;
        return px >= x && px < x + width && py >= y && py < y + height;
    }

    static void StartMonitors()
    {
        if (_globalMonitor != IntPtr.Zero) return;
        IntPtr nsEvent = ObjC.Class("NSEvent");
        ulong mask = MouseMoved | LeftMouseDragged | RightMouseDragged;
        delegate* unmanaged<IntPtr, IntPtr, void> global = &OnGlobalEvent;
        delegate* unmanaged<IntPtr, IntPtr, IntPtr> local = &OnLocalEvent;
        _globalMonitor = ObjC.Send(nsEvent, ObjC.Sel("addGlobalMonitorForEventsMatchingMask:handler:"), mask, ObjC.GlobalBlock((IntPtr)global));
        _localMonitor = ObjC.Send(nsEvent, ObjC.Sel("addLocalMonitorForEventsMatchingMask:handler:"), mask, ObjC.GlobalBlock((IntPtr)local));
        if (_globalMonitor != IntPtr.Zero) ObjC.Send(_globalMonitor, ObjC.Sel("retain"));
        if (_localMonitor != IntPtr.Zero) ObjC.Send(_localMonitor, ObjC.Sel("retain"));
    }

    [UnmanagedCallersOnly]
    static void OnGlobalEvent(IntPtr block, IntPtr e)
    {
        try { _current?.Update(); } catch { }
    }

    [UnmanagedCallersOnly]
    static IntPtr OnLocalEvent(IntPtr block, IntPtr e)
    {
        try { _current?.Update(); } catch { }
        return e;
    }
}
