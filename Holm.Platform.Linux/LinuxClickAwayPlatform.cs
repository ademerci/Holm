using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Clicks anywhere, from XInput2's raw button events on the root window: the X server reports every button press
/// without taking it from the app it is meant for. A thread waits for them on a connection of its own; whether the
/// click was on Holm itself the app decides (it knows whether the pointer is over one of its windows).
/// </summary>
public sealed class LinuxClickAwayPlatform : IClickAwayPlatform
{
    const string X11 = "libX11.so.6", Xi = "libXi.so.6";
    const int GenericEvent = 35, XI_RawButtonPress = 15;

    IntPtr _display;
    int _opcode;
    volatile bool _watching;

    public event Action? ClickedElsewhere;

    public void Watch(bool on)
    {
        _watching = on;
        if (!on || _display != IntPtr.Zero) return;
        try
        {
            if (XInitThreads() == 0) return;
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) return;
            if (!XQueryExtension(_display, "XInputExtension", out _opcode, out _, out _)) { XCloseDisplay(_display); _display = IntPtr.Zero; return; }
            int major = 2, minor = 0;
            if (XIQueryVersion(_display, ref major, ref minor) != 0) { XCloseDisplay(_display); _display = IntPtr.Zero; return; }
            // XIAllMasterDevices = 1; a mask of one byte with bit 15 set needs two bytes.
            var mask = new byte[2];
            mask[XI_RawButtonPress >> 3] |= (byte)(1 << (XI_RawButtonPress & 7));
            var handle = GCHandle.Alloc(mask, GCHandleType.Pinned);
            try
            {
                var eventMask = new XIEventMask { DeviceId = 1, MaskLength = mask.Length, Mask = handle.AddrOfPinnedObject() };
                XISelectEvents(_display, XDefaultRootWindow(_display), ref eventMask, 1);
            }
            finally { handle.Free(); }
            XFlush(_display);
            new Thread(Listen) { IsBackground = true, Name = "Holm click away" }.Start();
        }
        catch (DllNotFoundException) { _display = IntPtr.Zero; }
        catch (EntryPointNotFoundException) { _display = IntPtr.Zero; }
    }

    void Listen()
    {
        var e = new long[24];
        while (true)
        {
            if (XNextEvent(_display, e) != 0) break;
            // An XGenericEventCookie: type, serial, send_event, display, then extension and evtype as two ints.
            if ((int)e[0] != GenericEvent) continue;
            int extension = (int)(e[4] & 0xFFFFFFFF), evtype = (int)(e[4] >> 32);
            if (extension == _opcode && evtype == XI_RawButtonPress && _watching) ClickedElsewhere?.Invoke();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XIEventMask { public int DeviceId; public int MaskLength; public IntPtr Mask; }

    [DllImport(X11)] static extern int XInitThreads();
    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern bool XQueryExtension(IntPtr display, string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport(X11)] static extern int XFlush(IntPtr display);
    [DllImport(X11)] static extern int XNextEvent(IntPtr display, long[] e);
    [DllImport(Xi)] static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport(Xi)] static extern int XISelectEvents(IntPtr display, IntPtr window, ref XIEventMask masks, int count);
}
