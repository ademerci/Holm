using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>
/// The WPF app's ScreenCapture.Grabber without WPF: GDI copies the area and makes it smaller as it copies (no full-size
/// copy in memory), draws the mouse pointer in, and keeps its DCs, bitmap and pixel buffer from one picture to the next.
/// Windows kept out with <see cref="WindowsScreenPlatform.HideFromCapture"/> stay out of it.
/// </summary>
sealed class WindowsScreenGrabber : IScreenGrabber
{
    readonly int _x, _y, _areaWidth, _areaHeight, _width, _height;
    readonly IntPtr _screen, _memory, _bitmap;

    public WindowsScreenGrabber(int x, int y, int areaWidth, int areaHeight, int width, int height)
    {
        (_x, _y, _areaWidth, _areaHeight) = (x, y, Math.Max(1, areaWidth), Math.Max(1, areaHeight));
        (_width, _height) = (Math.Max(1, width), Math.Max(1, height));
        _screen = GetDC(IntPtr.Zero);
        _memory = CreateCompatibleDC(_screen);
        _bitmap = CreateCompatibleBitmap(_screen, _width, _height);
        SetStretchBltMode(_memory, HALFTONE);
        SetBrushOrgEx(_memory, 0, 0, IntPtr.Zero);
    }

    public ScreenImage? Grab()
    {
        IntPtr old = SelectObject(_memory, _bitmap);
        try
        {
            if (!StretchBlt(_memory, 0, 0, _width, _height, _screen, _x, _y, _areaWidth, _areaHeight, SRCCOPY | CAPTUREBLT)) return null;
            DrawPointer(_width / (double)_areaWidth);
        }
        finally { SelectObject(_memory, old); } // GetDIBits wants the bitmap out of the DC
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = _width, biHeight = -_height, // top-down
            biPlanes = 1, biBitCount = 32,
        };
        var pixels = new byte[_width * _height * 4];
        if (GetDIBits(_screen, _bitmap, 0, (uint)_height, pixels, ref header, 0) == 0) return null;
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;
        return new ScreenImage(_width, _height, pixels);
    }

    /// <summary>The mouse pointer where it is, as viewers expect to see it.</summary>
    void DrawPointer(double scale)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero) return;
        int hotX = 0, hotY = 0;
        if (GetIconInfo(info.hCursor, out var icon))
        {
            (hotX, hotY) = (icon.xHotspot, icon.yHotspot);
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
        }
        int size = scale < 1 ? Math.Max(12, (int)Math.Round(32 * scale)) : 0; // 0: the pointer's own size
        DrawIconEx(_memory, (int)Math.Round((info.ptScreenPos.X - _x - hotX) * scale), (int)Math.Round((info.ptScreenPos.Y - _y - hotY) * scale),
                   info.hCursor, size, size, 0, IntPtr.Zero, DI_NORMAL);
    }

    public void Dispose()
    {
        DeleteObject(_bitmap);
        DeleteDC(_memory);
        ReleaseDC(IntPtr.Zero, _screen);
    }

    /// <summary>Keeps a window out of screen pictures, or lets it back in (Windows 10 2004 and later).</summary>
    public static bool HideFromCapture(IntPtr window, bool hide)
        => window != IntPtr.Zero && SetWindowDisplayAffinity(window, hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int HALFTONE = 4, CURSOR_SHOWING = 1, DI_NORMAL = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
