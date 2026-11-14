using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch;

/// <summary>
/// Pictures of a screen for streaming: GDI copies the screen with the mouse pointer drawn in, and the picture goes out
/// as a JPEG at most <see cref="MaxSide"/> on its longer side. Windows marked with <see cref="HideFromCapture"/> (the
/// notch, which pops out your messages, and the LIVE badge) stay out of it.
/// </summary>
internal static class ScreenCapture
{
    /// <summary>
    /// The longer side of a streamed picture. 1600 px at quality 70 made 150–400 KB frames, 10–25 Mbit/s at 8 a second
    /// through a shared public relay; text stays readable at 1280.
    /// </summary>
    public const int MaxSide = 1280;

    /// <summary>
    /// Takes pictures of one area again and again, made smaller by GDI as it copies (no full-size copy in memory), with
    /// its DCs, bitmap and pixel buffer kept from one picture to the next. One capture thread only.
    /// </summary>
    public sealed class Grabber : IDisposable
    {
        readonly Int32Rect _area;
        readonly int _width, _height;
        readonly IntPtr _screen, _memory, _bitmap;
        readonly byte[] _pixels;

        public Grabber(Int32Rect area, int maxSide = MaxSide)
        {
            _area = area;
            (_width, _height) = Sharing.StreamProtocol.FitWithin(Math.Max(1, area.Width), Math.Max(1, area.Height), maxSide);
            _screen = GetDC(IntPtr.Zero);
            _memory = CreateCompatibleDC(_screen);
            _bitmap = CreateCompatibleBitmap(_screen, _width, _height);
            SetStretchBltMode(_memory, HALFTONE);
            SetBrushOrgEx(_memory, 0, 0, IntPtr.Zero);
            _pixels = new byte[_width * _height * 4];
        }

        /// <summary>The area now as a JPEG, or null if Windows wouldn't give it (the lock screen, a UAC prompt).</summary>
        public byte[]? Jpeg(int quality)
        {
            IntPtr old = SelectObject(_memory, _bitmap);
            try
            {
                if (!StretchBlt(_memory, 0, 0, _width, _height, _screen, _area.X, _area.Y, _area.Width, _area.Height, SRCCOPY | CAPTUREBLT)) return null;
                DrawPointer(_memory, _area, _width / (double)Math.Max(1, _area.Width));
            }
            finally { SelectObject(_memory, old); } // GetDIBits wants the bitmap out of the DC
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = _width, biHeight = -_height, // top-down
                biPlanes = 1, biBitCount = 32,
            };
            if (GetDIBits(_screen, _bitmap, 0, (uint)_height, _pixels, ref header, 0) == 0) return null;
            var picture = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr32, null, _pixels, _width * 4);
            var jpeg = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            jpeg.Frames.Add(BitmapFrame.Create(picture));
            using var stream = new MemoryStream();
            jpeg.Save(stream);
            return stream.ToArray();
        }

        public void Dispose()
        {
            DeleteObject(_bitmap);
            DeleteDC(_memory);
            ReleaseDC(IntPtr.Zero, _screen);
        }
    }

    /// <summary>The mouse pointer where it is, as viewers expect to see it.</summary>
    static void DrawPointer(IntPtr dc, Int32Rect area, double scale = 1)
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
        DrawIconEx(dc, (int)Math.Round((info.ptScreenPos.X - area.X - hotX) * scale), (int)Math.Round((info.ptScreenPos.Y - area.Y - hotY) * scale),
                   info.hCursor, size, size, 0, IntPtr.Zero, DI_NORMAL);
    }

    /// <summary>Keeps a window out of screen pictures (this one's and every other app's), or lets it back in.</summary>
    public static void HideFromCapture(IntPtr window, bool hide)
    {
        if (window == IntPtr.Zero) return;
        if (!SetWindowDisplayAffinity(window, hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE))
            Diag.Log("Couldn't change the window's capture setting: " + Marshal.GetLastWin32Error());
    }

    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int HALFTONE = 4;
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
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
