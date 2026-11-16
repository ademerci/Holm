using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Caps Lock from the X keyboard extension: the server announces each change of the keyboard's indicators (the
/// lights: Caps Lock, Num Lock), which a thread of its own waits for on a connection of its own. Keys themselves
/// are never seen.
/// </summary>
public sealed class LinuxCapsLockPlatform : ICapsLockPlatform
{
    const string X11 = "libX11.so.6";
    const uint UseCoreKeyboard = 0x0100, IndicatorStateNotifyMask = 1 << 4;

    IntPtr _display;
    Thread? _thread;
    volatile bool _running;
    bool _last;

    public event Action<bool>? Changed;

    public void Start()
    {
        if (_running) return;
        try
        {
            if (_display == IntPtr.Zero)
            {
                int major = 1, minor = 0;
                if (XInitThreads() == 0) return;
                _display = XkbOpenDisplay(IntPtr.Zero, out int eventBase, out _, ref major, ref minor, out int reason);
                if (_display == IntPtr.Zero) return;
                XkbSelectEvents(_display, UseCoreKeyboard, IndicatorStateNotifyMask, IndicatorStateNotifyMask);
                XFlush(_display);
                _last = CapsOn();
                _running = true;
                _thread = new Thread(() => Listen(eventBase)) { IsBackground = true, Name = "Holm Caps Lock" };
                _thread.Start();
                return;
            }
            _last = CapsOn();
            _running = true;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    void Listen(int eventBase)
    {
        var e = new long[24];
        while (true)
        {
            if (XNextEvent(_display, e) != 0) break;
            if ((int)e[0] != eventBase || !_running) continue;
            bool on = CapsOn();
            if (on == _last) continue;
            _last = on;
            Changed?.Invoke(on);
        }
    }

    /// <summary>Caps Lock is the keyboard's first indicator on every X keymap in use.</summary>
    bool CapsOn() => XkbGetIndicatorState(_display, UseCoreKeyboard, out uint state) == 0 && (state & 1) != 0;

    /// <summary>The connection lives as long as the app; while stopped, its events are ignored.</summary>
    public void Stop() => _running = false;

    [DllImport(X11)] static extern int XInitThreads();
    [DllImport(X11)] static extern IntPtr XkbOpenDisplay(IntPtr name, out int eventBase, out int errorBase, ref int major, ref int minor, out int reason);
    [DllImport(X11)] static extern bool XkbSelectEvents(IntPtr display, uint device, ulong affect, ulong values);
    [DllImport(X11)] static extern int XkbGetIndicatorState(IntPtr display, uint device, out uint state);
    [DllImport(X11)] static extern int XFlush(IntPtr display);
    [DllImport(X11)] static extern int XNextEvent(IntPtr display, long[] e);
}
