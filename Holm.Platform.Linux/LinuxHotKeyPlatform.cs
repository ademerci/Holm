using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Ctrl+H as a key grab on the X root window: the X server hands that one combination to Holm alone, on a
/// connection of its own that a thread waits on. The grab is made once per lock state (Caps Lock, Num Lock), since X
/// counts them as modifiers. Under Wayland only X windows are covered: a key pressed in a Wayland app isn't seen.
/// </summary>
public sealed class LinuxHotKeyPlatform : IHotKeyPlatform
{
    const string X11 = "libX11.so.6";
    const uint ControlMask = 1 << 2, LockMask = 1 << 1, Mod2Mask = 1 << 4; // Mod2 is Num Lock on every current keymap
    const int KeyPress = 2, GrabModeAsync = 1, XK_h = 0x68;

    static readonly uint[] LockStates = { 0, LockMask, Mod2Mask, LockMask | Mod2Mask };

    IntPtr _display;
    int _keycode;
    volatile bool _grabbed;

    public event Action? Pressed;

    public bool Start()
    {
        try
        {
            if (_display == IntPtr.Zero)
            {
                if (XInitThreads() == 0) return false;
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) return false;
                _keycode = XKeysymToKeycode(_display, XK_h);
                XSetErrorHandler(IgnoreErrors); // a grab another app holds is reported asynchronously as an error
                new Thread(Listen) { IsBackground = true, Name = "Holm hot key" }.Start();
            }
            if (_grabbed) return true;
            IntPtr root = XDefaultRootWindow(_display);
            foreach (uint locks in LockStates)
                XGrabKey(_display, _keycode, ControlMask | locks, root, false, GrabModeAsync, GrabModeAsync);
            XSync(_display, false);
            _grabbed = true;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public void Stop()
    {
        if (!_grabbed || _display == IntPtr.Zero) return;
        IntPtr root = XDefaultRootWindow(_display);
        foreach (uint locks in LockStates) XUngrabKey(_display, _keycode, ControlMask | locks, root);
        XFlush(_display);
        _grabbed = false;
    }

    void Listen()
    {
        var e = new long[24];
        while (true)
        {
            if (XNextEvent(_display, e) != 0) break;
            if ((int)e[0] == KeyPress && _grabbed) Pressed?.Invoke();
        }
    }

    static readonly ErrorHandler IgnoreErrors = (_, _) => 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ErrorHandler(IntPtr display, IntPtr error);

    [DllImport(X11)] static extern int XInitThreads();
    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XKeysymToKeycode(IntPtr display, int keysym);
    [DllImport(X11)] static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(X11)] static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);
    [DllImport(X11)] static extern int XSync(IntPtr display, bool discard);
    [DllImport(X11)] static extern int XFlush(IntPtr display);
    [DllImport(X11)] static extern int XNextEvent(IntPtr display, long[] e);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(ErrorHandler handler);
}
