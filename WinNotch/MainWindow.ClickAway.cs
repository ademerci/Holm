using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinNotch;

// A click anywhere that isn't Holm closes the notch: the open notch and the pop-outs that come and go on their own
// shrink back at once, as a menu closes when you click away. The notch never takes the focus (it would steal it from
// the app you type in), so it can't hear that click itself; while something is open, a light timer watches the mouse
// buttons instead and asks Windows whose window is under the pointer. Nothing runs while the notch is closed, and
// pop-outs that wait for a decision (an incoming file, a friend request) stay until they're answered.
public partial class MainWindow
{
    DispatcherTimer? _clickAway;
    bool _buttonWasDown;

    /// <summary>Something is open that a click elsewhere should close.</summary>
    bool ClosesOnClickAway =>
        !_hiddenForFullscreen
        && (_expanded || _peekActive || _volumePeek || ScreenshotShowing || (TaskShowing && _taskHide?.IsEnabled == true));

    /// <summary>Starts or stops watching for clicks away as the notch opens and closes (from <see cref="ApplyState"/>).</summary>
    void WatchClicksAway()
    {
        if (ClosesOnClickAway)
        {
            if (_clickAway is { IsEnabled: true }) return;
            _clickAway ??= new DispatcherTimer(TimeSpan.FromMilliseconds(35), DispatcherPriority.Input, (_, _) => CheckClickAway(), Dispatcher);
            _buttonWasDown = Native.MouseButtonDown(); // the click that opened it doesn't count
            _clickAway.Start();
        }
        else _clickAway?.Stop();
    }

    void CheckClickAway()
    {
        bool down = Native.MouseButtonDown();
        bool pressed = down && !_buttonWasDown;
        _buttonWasDown = down;
        if (!ClosesOnClickAway) { _clickAway?.Stop(); return; }
        if (!pressed || MenuOpen || IsHolmUnderPointer()) return;
        CloseOnClickAway();
    }

    /// <summary>Whether the pointer is over one of Holm's own windows: the notch, its menus, the Holm window, a stream.</summary>
    static bool IsHolmUnderPointer()
    {
        if (!Native.GetCursorPos(out var point)) return true; // can't tell: leave things as they are
        IntPtr window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        IntPtr top = GetAncestor(window, GA_ROOT);
        if (top == IntPtr.Zero) top = window;
        return GetWindowThreadProcessId(top, out uint pid) != 0 && pid == (uint)Environment.ProcessId;
    }

    void CloseOnClickAway()
    {
        Diag.Log("Closed by a click elsewhere");
        _expandDelay.Stop();
        _peekTimer.Stop();
        _volumeTimer.Stop();
        _peekActive = false;
        _volumePeek = false;
        if (ScreenshotShowing || (TaskShowing && _taskHide?.IsEnabled == true)) HideTask();
        if (_expanded)
        {
            EndTyping();
            SetExpanded(false); // also redraws
        }
        else ApplyState();
    }

    const uint GA_ROOT = 2;
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
