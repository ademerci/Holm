using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// XFixes tells whenever the CLIPBOARD selection gets a new owner, which is what copying does. A thread of its own
/// waits on a connection of its own for that event (blocking, not polling). Under Wayland the app runs through
/// XWayland, which hands Wayland's clipboard over to X the same way.
/// </summary>
public sealed class LinuxClipboardPlatform : IClipboardPlatform
{
    const string X11 = "libX11.so.6", Xfixes = "libXfixes.so.3";
    const ulong SetSelectionOwnerNotifyMask = 1;

    IntPtr _display;
    Thread? _thread;
    volatile bool _running;

    public event Action? Changed;

    public void Start()
    {
        if (_running) return;
        try
        {
            if (XInitThreads() == 0) return;
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) return;
            if (XFixesQueryExtension(_display, out int eventBase, out _) == 0) { XCloseDisplay(_display); _display = IntPtr.Zero; return; }
            IntPtr clipboard = XInternAtom(_display, "CLIPBOARD", false);
            XFixesSelectSelectionInput(_display, XDefaultRootWindow(_display), clipboard, SetSelectionOwnerNotifyMask);
            XFlush(_display);
            _running = true;
            _thread = new Thread(() => Listen(eventBase)) { IsBackground = true, Name = "Holm clipboard" };
            _thread.Start();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    void Listen(int eventBase)
    {
        // An XEvent is a union of 24 longs; its first int is the type.
        var e = new long[24];
        while (_running)
        {
            if (XNextEvent(_display, e) != 0) break;
            if ((int)e[0] == eventBase) Changed?.Invoke();
        }
    }

    /// <summary>The connection lives as long as the app; the thread ends with the process.</summary>
    public void Stop() => _running = false;

    [DllImport(X11)] static extern int XInitThreads();
    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XFlush(IntPtr display);
    [DllImport(X11)] static extern int XNextEvent(IntPtr display, long[] e);
    [DllImport(Xfixes)] static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(Xfixes)] static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window, IntPtr selection, ulong mask);
}
