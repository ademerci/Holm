using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// The overlay on X11, which is also what the window is under Wayland (Avalonia runs through XWayland there).
/// The window is a dock (set by the app before it shows, see NotchWindow): window managers keep those above
/// normal windows, on every workspace and out of the task switcher. Here: the states to match, the input shape
/// that lets clicks beside the notch through, and the keyboard hand-over.
///
/// Works on a connection of its own, since Avalonia doesn't hand out its display; the calls are few and all come
/// from the UI thread. See-through corners need a compositing window manager, which every current desktop has.
/// </summary>
public sealed class LinuxNotchWindowPlatform : INotchWindowPlatform
{
    const int ShapeInput = 2, ShapeSet = 0, ClientMessage = 33, RevertToParent = 2;
    const long SubstructureNotifyMask = 1L << 19, SubstructureRedirectMask = 1L << 20;
    const long NetWmStateAdd = 1;

    readonly IntPtr _display;
    IntPtr _focusBefore;

    public LinuxNotchWindowPlatform()
    {
        try { _display = XOpenDisplay(IntPtr.Zero); }
        catch (DllNotFoundException) { } // no X libraries: the window stays an ordinary topmost window
    }

    public void MakeOverlay(IntPtr window)
    {
        if (_display == IntPtr.Zero) return;
        // Two states fit in one request.
        SendToRoot(window, "_NET_WM_STATE", NetWmStateAdd, Atom("_NET_WM_STATE_ABOVE"), Atom("_NET_WM_STATE_STICKY"), 1);
        SendToRoot(window, "_NET_WM_STATE", NetWmStateAdd, Atom("_NET_WM_STATE_SKIP_TASKBAR"), Atom("_NET_WM_STATE_SKIP_PAGER"), 1);
        XFlush(_display);
    }

    public void SetHitRegion(IntPtr window, int x, int y, int width, int height)
        => SetHitRegions(window, new[] { new HitRect(x, y, width, height) });

    public void SetHitRegions(IntPtr window, HitRect[] rects)
    {
        if (_display == IntPtr.Zero || rects.Length == 0) return;
        var xRects = new XRectangle[rects.Length];
        for (int i = 0; i < rects.Length; i++)
            xRects[i] = new XRectangle
            {
                X = (short)rects[i].X,
                Y = (short)rects[i].Y,
                Width = (ushort)Math.Max(0, rects[i].Width),
                Height = (ushort)Math.Max(0, rects[i].Height),
            };
        try
        {
            XShapeCombineRectangles(_display, window, ShapeInput, 0, 0, xRects, xRects.Length, ShapeSet, 0);
            XFlush(_display);
        }
        catch (DllNotFoundException) { } // no libXext: the whole window takes the mouse
        catch (EntryPointNotFoundException) { }
    }

    public void SetKeyboard(IntPtr window, bool take)
    {
        if (_display == IntPtr.Zero) return;
        if (take)
        {
            // Remember who had the keyboard, to hand it back; then ask the window manager, and take it ourselves
            // for the ones that don't activate docks.
            XGetInputFocus(_display, out _focusBefore, out _);
            SendToRoot(window, "_NET_ACTIVE_WINDOW", 2 /* from a pager: not subject to focus-stealing rules */, 0, 0, 0);
            XSetInputFocus(_display, window, RevertToParent, IntPtr.Zero);
        }
        else if (_focusBefore != IntPtr.Zero && _focusBefore != window)
        {
            // Only when the keyboard is still ours: if the user clicked into another app, it is already there.
            XGetInputFocus(_display, out IntPtr now, out _);
            if (now == window || IsChildOf(now, window)) XSetInputFocus(_display, _focusBefore, RevertToParent, IntPtr.Zero);
            _focusBefore = IntPtr.Zero;
        }
        XFlush(_display);
    }

    /// <summary>Avalonia keeps the keyboard on a small child window of its own, not on the window itself.</summary>
    bool IsChildOf(IntPtr window, IntPtr parent)
    {
        if (window == IntPtr.Zero || window.ToInt64() == 1 /* PointerRoot */) return false;
        if (XQueryTree(_display, window, out _, out IntPtr actualParent, out IntPtr children, out _) == 0) return false;
        if (children != IntPtr.Zero) XFree(children);
        return actualParent == parent;
    }

    IntPtr Atom(string name) => XInternAtom(_display, name, false);

    /// <summary>A request to the window manager, the way the EWMH specification wants them sent.</summary>
    void SendToRoot(IntPtr window, string message, long d0, long d1, long d2, long d3)
    {
        // An XClientMessageEvent, as the 24 longs every XEvent is: type, serial, send_event, display, window,
        // message_type, format, then five longs of data.
        var e = new long[24];
        e[0] = ClientMessage;
        e[2] = 1;
        e[3] = _display.ToInt64();
        e[4] = window.ToInt64();
        e[5] = Atom(message).ToInt64();
        e[6] = 32;
        e[7] = d0; e[8] = d1; e[9] = d2; e[10] = d3;
        XSendEvent(_display, XDefaultRootWindow(_display), false, new IntPtr(SubstructureNotifyMask | SubstructureRedirectMask), e);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XRectangle { public short X, Y; public ushort Width, Height; }

    const string X11 = "libX11.so.6", Xext = "libXext.so.6";

    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, long[] e);
    [DllImport(X11)] static extern int XFlush(IntPtr display);
    [DllImport(X11)] static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);
    [DllImport(X11)] static extern int XSetInputFocus(IntPtr display, IntPtr focus, int revertTo, IntPtr time);
    [DllImport(X11)] static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(Xext)] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int xOff, int yOff, XRectangle[] rects, int count, int op, int ordering);

    /// <summary>glibc keeps freed memory for later; malloc_trim gives it back. Other C libraries have no such call.</summary>
    public void TrimMemory()
    {
        try { malloc_trim(0); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("libc")] static extern int malloc_trim(nuint pad);
}
