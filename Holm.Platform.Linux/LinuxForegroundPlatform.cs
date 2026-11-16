using System;
using System.IO;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// The window manager's active window (_NET_ACTIVE_WINDOW): its state says fullscreen or maximized, its _NET_WM_PID
/// names the program, its place says whether it is on the notch's screen.
/// </summary>
public sealed class LinuxForegroundPlatform : IForegroundPlatform
{
    const string X11 = "libX11.so.6";
    IntPtr _display;
    bool _unavailable;

    public ForegroundWindow Read(int x, int y, int width, int height)
    {
        if (_unavailable) return default;
        try
        {
            if (_display == IntPtr.Zero)
            {
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) { _unavailable = true; return default; }
            }
            IntPtr root = XDefaultRootWindow(_display);
            var active = Property(root, "_NET_ACTIVE_WINDOW", out int count);
            if (count < 1 || active[0] == 0) return default;
            IntPtr window = new(active[0]);
            var states = Property(window, "_NET_WM_STATE", out int stateCount);
            bool fullscreen = false, maxV = false, maxH = false;
            IntPtr full = Atom("_NET_WM_STATE_FULLSCREEN"), vert = Atom("_NET_WM_STATE_MAXIMIZED_VERT"), horz = Atom("_NET_WM_STATE_MAXIMIZED_HORZ");
            for (int i = 0; i < stateCount; i++)
            {
                fullscreen |= states[i] == full;
                maxV |= states[i] == vert;
                maxH |= states[i] == horz;
            }
            // On the notch's screen: the window's middle lies within it.
            if (XGetWindowAttributes(_display, window, out var attributes) != 0
                && XTranslateCoordinates(_display, window, root, 0, 0, out int wx, out int wy, out _) != 0)
            {
                int cx = wx + attributes.Width / 2, cy = wy + attributes.Height / 2;
                if (cx < x || cx >= x + width || cy < y || cy >= y + height) return default;
                // Games that cover the screen without asking the window manager, and windows sized to fill it
                // without its maximized flag (as Windows' notification state and IsZoomed would still see them).
                fullscreen |= wx <= x && wy <= y && attributes.Width >= width && attributes.Height >= height;
                if (!fullscreen && attributes.Width >= width * 0.98 && attributes.Height >= height * 0.85) maxV = maxH = true;
            }
            var pid = Property(window, "_NET_WM_PID", out int pidCount);
            string name = "";
            if (pidCount > 0)
            {
                try { name = File.ReadAllText($"/proc/{pid[0]}/comm").Trim(); } catch (IOException) { }
            }
            return new ForegroundWindow(fullscreen, name, maxV && maxH);
        }
        catch (DllNotFoundException) { _unavailable = true; return default; }
    }

    IntPtr Atom(string name) => XInternAtom(_display, name, false);

    /// <summary>A window property of 32-bit items (X hands them out as longs).</summary>
    long[] Property(IntPtr window, string name, out int count)
    {
        count = 0;
        if (XGetWindowProperty(_display, window, Atom(name), 0, 64, false, IntPtr.Zero, out _, out int format, out ulong items, out _, out IntPtr data) != 0 || data == IntPtr.Zero)
            return Array.Empty<long>();
        try
        {
            if (format != 32) return Array.Empty<long>();
            count = (int)items;
            var values = new long[count];
            Marshal.Copy(data, values, 0, count);
            return values;
        }
        finally { XFree(data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XWindowAttributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public IntPtr Visual, Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public ulong BackingPlanes, BackingPixel;
        public int SaveUnder;
        public IntPtr Colormap;
        public int MapInstalled, MapState;
        public long AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public IntPtr Screen;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);
    [DllImport(X11)] static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, long length, bool delete,
        IntPtr type, out IntPtr actualType, out int actualFormat, out ulong items, out ulong bytesAfter, out IntPtr data);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(X11)] static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);
    [DllImport(X11)] static extern int XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int x, int y, out int destX, out int destY, out IntPtr child);
}
