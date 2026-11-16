using System;
using System.Runtime.InteropServices;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>The WPF app's WatchFullscreen: the shell says whether a fullscreen app is in front, and whose window it is.</summary>
public sealed class WindowsForegroundPlatform : IForegroundPlatform
{
    public ForegroundWindow Read(int x, int y, int width, int height)
    {
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return default;
        // The shell's answer covers all monitors, so also check that the window is on ours.
        bool onOurs = MonitorFromWindow(foreground, 2 /* NEAREST */) == MonitorFromPoint(new Point { X = x + width / 2, Y = y + height / 2 }, 2);
        if (!onOurs) return default;
        bool fullscreen = Native.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;
        return new ForegroundWindow(fullscreen, Native.ProcessNameOf(foreground), Native.IsZoomed(foreground));
    }

    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
}
