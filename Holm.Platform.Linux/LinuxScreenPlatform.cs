using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// The screen's pixels from the X server's root window. Under Wayland that is XWayland's root, which only holds X
/// windows: there the picture is mostly black, and a portal would be needed (not done yet).
/// </summary>
public sealed class LinuxScreenPlatform : IScreenPlatform
{
    const string X11 = "libX11.so.6";
    const int ZPixmap = 2;

    public Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height) => Task.Run(() => Capture(x, y, width, height));

    static unsafe ScreenImage? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        IntPtr display = IntPtr.Zero, image = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            image = XGetImage(display, XDefaultRootWindow(display), x, y, (uint)width, (uint)height, ulong.MaxValue, ZPixmap);
            if (image == IntPtr.Zero) return null;
            var info = (XImage*)image;
            // A true-colour screen: 32 bits per pixel, blue first in memory (what every current X server uses).
            if (info->BitsPerPixel != 32 || info->ByteOrder != 0) return null;
            var bgra = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                var source = new ReadOnlySpan<byte>((byte*)info->Data + row * info->BytesPerLine, width * 4);
                var target = bgra.AsSpan(row * width * 4, width * 4);
                source.CopyTo(target);
                for (int i = 3; i < target.Length; i += 4) target[i] = 0xFF; // the fourth byte is padding, not alpha
            }
            return new ScreenImage(width, height, bgra);
        }
        catch (DllNotFoundException) { return null; }
        finally
        {
            if (image != IntPtr.Zero)
            {
                var info = (XImage*)image;
                if (info->Data != IntPtr.Zero) XFree(info->Data);
                XFree(image);
            }
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XImage
    {
        public int Width, Height, XOffset, Format;
        public IntPtr Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(X11)] static extern int XFree(IntPtr data);
}
