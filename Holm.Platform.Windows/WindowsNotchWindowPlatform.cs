using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

public sealed class WindowsNotchWindowPlatform : INotchWindowPlatform
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    public void MakeOverlay(IntPtr window)
    {
        long style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void SetHitRegion(IntPtr window, int x, int y, int width, int height)
    {
        IntPtr region = CreateRectRgn(x, y, x + width, y + height);
        // The window owns the region once this succeeds; only a failed call leaves it to us.
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }

    public void SetKeyboard(IntPtr window, bool take)
    {
        long style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
        style = take ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(style));
        if (take) SetForegroundWindow(window);
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);
}
