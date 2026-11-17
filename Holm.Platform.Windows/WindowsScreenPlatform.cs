using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>The screen's pixels through GDI, as the WPF app's colour picker reads them.</summary>
public sealed class WindowsScreenPlatform : IScreenPlatform
{
    public Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height) => Task.Run(() => Capture(x, y, width, height));

    static ScreenImage? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, // top-down
                Planes = 1, BitCount = 32,
            };
            bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) return null;
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */)) return null;
            var bgra = new byte[width * height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 0xFF;
            return new ScreenImage(width, height, bgra);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size, Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    /// <summary>Windows' own OCR, through the WPF app's ScreenText (linked).</summary>
    public bool CanReadText => WinNotch.ScreenText.Available;

    public Task<string?> ReadTextAsync(string path) => WinNotch.ScreenText.ReadAsync(path);

    public IScreenGrabber? OpenGrabber(int x, int y, int areaWidth, int areaHeight, int width, int height)
        => new WindowsScreenGrabber(x, y, areaWidth, areaHeight, width, height);

    public bool HideFromCapture(IntPtr window, bool hide) => WindowsScreenGrabber.HideFromCapture(window, hide);
}
