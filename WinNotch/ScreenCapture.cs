using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch;

/// <summary>
/// Pictures of a screen for streaming: GDI copies the screen with the mouse pointer drawn in, and the picture goes out
/// as a JPEG at most <see cref="MaxWidth"/> wide. Windows marked with <see cref="HideFromCapture"/> (the notch, which
/// pops out your messages, and the LIVE badge) stay out of it.
/// </summary>
internal static class ScreenCapture
{
    public const int MaxWidth = 1600;
    const int Quality = 70;

    /// <summary>The screen's area in physical pixels as a JPEG, or null if Windows wouldn't give it (the lock screen, a UAC prompt).</summary>
    public static byte[]? Jpeg(Int32Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SRCCOPY | CAPTUREBLT)) return null;
            DrawPointer(memory, area);
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = area.Width, biHeight = -area.Height, // top-down
                biPlanes = 1, biBitCount = 32,
            };
            var pixels = new byte[area.Width * area.Height * 4];
            SelectObject(memory, old); // GetDIBits wants the bitmap out of the DC
            old = IntPtr.Zero;
            if (GetDIBits(screen, bitmap, 0, (uint)area.Height, pixels, ref header, 0) == 0) return null;
            BitmapSource picture = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, pixels, area.Width * 4);
            if (area.Width > MaxWidth)
            {
                double scale = (double)MaxWidth / area.Width;
                picture = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
            }
            var jpeg = new JpegBitmapEncoder { QualityLevel = Quality };
            jpeg.Frames.Add(BitmapFrame.Create(picture));
            using var stream = new MemoryStream();
            jpeg.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The mouse pointer where it is, as viewers expect to see it.</summary>
    static void DrawPointer(IntPtr dc, Int32Rect area)
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
        DrawIconEx(dc, info.ptScreenPos.X - area.X - hotX, info.ptScreenPos.Y - area.Y - hotY, info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
    }

    /// <summary>Keeps a window out of screen pictures (this one's and every other app's), or lets it back in.</summary>
    public static void HideFromCapture(IntPtr window, bool hide)
    {
        if (window == IntPtr.Zero) return;
        if (!SetWindowDisplayAffinity(window, hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE))
            Diag.Log("Couldn't change the window's capture setting: " + Marshal.GetLastWin32Error());
    }

    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int CURSOR_SHOWING = 1, DI_NORMAL = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
