using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

public sealed class WindowsNotchWindowPlatform : INotchWindowPlatform
{
    const int GWL_EXSTYLE = -20, RGN_OR = 2;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    bool _preciseAnimationTiming;

    public void SetAnimationTiming(bool active)
    {
        if (active == _preciseAnimationTiming) return;
        // Avalonia's software render loop uses timed waits. The normal Windows timer
        // granularity can make those waits miss an entire refresh on a 60 Hz display.
        if (active) _preciseAnimationTiming = timeBeginPeriod(1) == 0;
        else
        {
            timeEndPeriod(1);
            _preciseAnimationTiming = false;
        }
    }

    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);

    public void MakeOverlay(IntPtr window)
    {
        long style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // As the WPF app: only when another window really covers it, since re-ordering makes the window draw itself again.
    public void KeepOnTop(IntPtr window)
    {
        if (WinNotch.Native.IsCovered(window))
            SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void SetHitRegion(IntPtr window, int x, int y, int width, int height)
    {
        IntPtr region = CreateRectRgn(x, y, x + width, y + height);
        // The window owns the region once this succeeds; only a failed call leaves it to us.
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }

    public void SetHitRegions(IntPtr window, HitRect[] rects)
    {
        if (rects.Length == 0) return;
        if (rects.Length == 1) { SetHitRegion(window, rects[0].X, rects[0].Y, rects[0].Width, rects[0].Height); return; }
        IntPtr combined = CreateRectRgn(rects[0].X, rects[0].Y, rects[0].X + rects[0].Width, rects[0].Y + rects[0].Height);
        for (int i = 1; i < rects.Length; i++)
        {
            IntPtr next = CreateRectRgn(rects[i].X, rects[i].Y, rects[i].X + rects[i].Width, rects[i].Y + rects[i].Height);
            CombineRgn(combined, combined, next, RGN_OR);
            DeleteObject(next);
        }
        if (SetWindowRgn(window, combined, true) == 0) DeleteObject(combined);
    }

    public void SetRoundedWindowRegion(IntPtr window, int radius)
    {
        if (!WinNotch.Native.GetWindowRect(window, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return;
        int diameter = Math.Min(radius * 2, Math.Min(width, height));
        IntPtr region = CreateRoundRectRgn(0, 0, width, height, diameter, diameter);
        if (region != IntPtr.Zero && SetWindowRgn(window, region, true) == 0) DeleteObject(region);
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
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);

    public void TrimMemory() => K32EmptyWorkingSet(GetCurrentProcess());

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool K32EmptyWorkingSet(IntPtr process);
}
