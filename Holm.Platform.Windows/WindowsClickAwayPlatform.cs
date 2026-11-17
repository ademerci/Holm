using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// The WPF app's click-away (MainWindow.ClickAway.cs): the notch never takes the focus, so it can't hear a click
/// elsewhere; while something is open, the mouse buttons are read every 35 ms and Windows is asked whose window is
/// under the pointer. Nothing runs while nothing is open.
/// </summary>
public sealed class WindowsClickAwayPlatform : IClickAwayPlatform
{
    const uint GA_ROOT = 2;
    Timer? _timer;
    bool _buttonWasDown;

    public event Action? ClickedElsewhere;

    public void Watch(bool on)
    {
        if (on)
        {
            if (_timer != null) return;
            _buttonWasDown = Native.MouseButtonDown(); // the click that opened it doesn't count
            _timer = new Timer(_ => Check(), null, 35, 35);
        }
        else
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    void Check()
    {
        bool down = Native.MouseButtonDown();
        bool pressed = down && !_buttonWasDown;
        _buttonWasDown = down;
        if (pressed && !IsHolmUnderPointer()) ClickedElsewhere?.Invoke();
    }

    /// <summary>Whether the pointer is over one of Holm's own windows: the notch, its menus, the Holm window.</summary>
    static bool IsHolmUnderPointer()
    {
        if (!Native.GetCursorPos(out var point)) return true; // can't tell: leave things as they are
        IntPtr window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        IntPtr top = GetAncestor(window, GA_ROOT);
        if (top == IntPtr.Zero) top = window;
        return GetWindowThreadProcessId(top, out uint pid) != 0 && pid == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
