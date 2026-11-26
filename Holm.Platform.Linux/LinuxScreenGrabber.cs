using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Pictures of one area of the X screen for a stream: the root window's pixels, made smaller by averaging (so text
/// stays readable), with the mouse pointer from XFixes drawn in. Keeps its own connection to the X server; one thread only.
/// Under Wayland only X windows show, as for <see cref="LinuxScreenPlatform.CaptureAsync"/>.
/// </summary>
sealed unsafe class LinuxScreenGrabber : IScreenGrabber
{
    const string X11 = "libX11.so.6", XFixes = "libXfixes.so.3";
    const int ZPixmap = 2;

    readonly IntPtr _display, _root;
    readonly int _x, _y, _areaWidth, _areaHeight, _width, _height;
    readonly int[] _columnStart, _rowStart; // where each picture column/row begins in the area; one more for the end
    readonly bool _pointer;

    LinuxScreenGrabber(IntPtr display, int x, int y, int areaWidth, int areaHeight, int width, int height)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
        (_x, _y, _areaWidth, _areaHeight) = (x, y, areaWidth, areaHeight);
        (_width, _height) = (Math.Min(width, areaWidth), Math.Min(height, areaHeight));
        _columnStart = Bounds(_width, _areaWidth);
        _rowStart = Bounds(_height, _areaHeight);
        try { _pointer = XFixesQueryExtension(display, out _, out _); }
        catch (DllNotFoundException) { _pointer = false; }
        catch (EntryPointNotFoundException) { _pointer = false; }
    }

    public static LinuxScreenGrabber? Open(int x, int y, int areaWidth, int areaHeight, int width, int height)
    {
        if (areaWidth <= 0 || areaHeight <= 0 || width <= 0 || height <= 0) return null;
        try
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            return display == IntPtr.Zero ? null : new LinuxScreenGrabber(display, x, y, areaWidth, areaHeight, width, height);
        }
        catch (DllNotFoundException) { return null; }
    }

    static int[] Bounds(int count, int over)
    {
        var bounds = new int[count + 1];
        for (int i = 0; i <= count; i++) bounds[i] = (int)((long)i * over / count);
        return bounds;
    }

    public ScreenImage? Grab()
    {
        IntPtr prevHandler = XSetErrorHandler(IgnoreErrors);
        IntPtr image = IntPtr.Zero;
        try
        {
            if (XGetGeometry(_display, _root, out _, out _, out _, out uint rootW, out uint rootH, out _, out _) == 0)
                return null;

            int x0 = Math.Max(0, _x), y0 = Math.Max(0, _y);
            int x1 = Math.Min((int)rootW, _x + _areaWidth), y1 = Math.Min((int)rootH, _y + _areaHeight);
            int rw = x1 - x0, rh = y1 - y0;
            if (rw <= 0 || rh <= 0) return null;

            image = XGetImage(_display, _root, x0, y0, (uint)rw, (uint)rh, ulong.MaxValue, ZPixmap);
            if (image == IntPtr.Zero) return null;

            var info = (XImage*)image;
            if (info->Data == IntPtr.Zero || info->BitsPerPixel != 32 || info->ByteOrder != 0
                || info->Width < rw || info->Height < rh || info->BytesPerLine < rw * 4)
                return null; // a true-colour screen, blue first

            var pixels = new byte[_width * _height * 4];
            byte* data = (byte*)info->Data;
            int stride = info->BytesPerLine;
            int offX = x0 - _x, offY = y0 - _y;
            fixed (byte* target = pixels)
            {
                for (int row = 0; row < _height; row++)
                {
                    int rowStart = _rowStart[row], rowEnd = Math.Max(rowStart + 1, _rowStart[row + 1]);
                    int cy0 = Math.Max(rowStart, offY), cy1 = Math.Min(rowEnd, offY + rh);
                    for (int column = 0; column < _width; column++)
                    {
                        int colStart = _columnStart[column], colEnd = Math.Max(colStart + 1, _columnStart[column + 1]);
                        int cx0 = Math.Max(colStart, offX), cx1 = Math.Min(colEnd, offX + rw);
                        byte* q = target + (row * _width + column) * 4;
                        if (cx1 <= cx0 || cy1 <= cy0)
                        {
                            q[0] = 0; q[1] = 0; q[2] = 0; q[3] = 0xFF;
                            continue;
                        }
                        int b = 0, g = 0, r = 0, n = (cx1 - cx0) * (cy1 - cy0);
                        for (int sy = cy0; sy < cy1; sy++)
                        {
                            byte* p = data + (sy - offY) * stride + (cx0 - offX) * 4;
                            for (int sx = cx0; sx < cx1; sx++, p += 4) { b += p[0]; g += p[1]; r += p[2]; }
                        }
                        q[0] = (byte)(b / n); q[1] = (byte)(g / n); q[2] = (byte)(r / n); q[3] = 0xFF;
                    }
                }
                if (_pointer) DrawPointer(target);
            }
            return new ScreenImage(_width, _height, pixels);
        }
        finally
        {
            if (image != IntPtr.Zero)
            {
                var info = (XImage*)image;
                if (info->Data != IntPtr.Zero) XFree(info->Data);
                XFree(image);
            }
            if (prevHandler != IntPtr.Zero) XSetErrorHandler(prevHandler);
        }
    }

    /// <summary>The pointer as XFixes has it (premultiplied ARGB in longs), shrunk like the windows' picture on Windows.</summary>
    void DrawPointer(byte* target)
    {
        var cursor = XFixesGetCursorImage(_display);
        if (cursor == null) return;
        try
        {
            if (cursor->Width == 0 || cursor->Height == 0 || cursor->Pixels == null) return;
            double scale = _width / (double)_areaWidth;
            // As on Windows: a smaller picture gets a smaller pointer, never smaller than 12 pixels on a 32-pixel pointer.
            double size = scale < 1 ? Math.Max(12, 32 * scale) / 32 : 1;
            int w = Math.Max(1, (int)Math.Round(cursor->Width * size)), h = Math.Max(1, (int)Math.Round(cursor->Height * size));
            int left = (int)Math.Round((cursor->X - _x) * scale - cursor->XHot * size);
            int top = (int)Math.Round((cursor->Y - _y) * scale - cursor->YHot * size);
            for (int py = 0; py < h; py++)
            {
                int ty = top + py;
                if (ty < 0 || ty >= _height) continue;
                int sy = Math.Min(cursor->Height - 1, (int)(py / size));
                for (int px = 0; px < w; px++)
                {
                    int tx = left + px;
                    if (tx < 0 || tx >= _width) continue;
                    int sx = Math.Min(cursor->Width - 1, (int)(px / size));
                    uint argb = (uint)cursor->Pixels[sy * cursor->Width + sx];
                    uint a = argb >> 24;
                    if (a == 0) continue;
                    byte* q = target + (ty * _width + tx) * 4;
                    uint keep = 255 - a;
                    q[0] = (byte)Math.Min(255, (argb & 0xFF) + q[0] * keep / 255);
                    q[1] = (byte)Math.Min(255, ((argb >> 8) & 0xFF) + q[1] * keep / 255);
                    q[2] = (byte)Math.Min(255, ((argb >> 16) & 0xFF) + q[2] * keep / 255);
                }
            }
        }
        finally { XFree((IntPtr)cursor); }
    }

    public void Dispose() => XCloseDisplay(_display);

    [StructLayout(LayoutKind.Sequential)]
    struct XImage
    {
        public int Width, Height, XOffset, Format;
        public IntPtr Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XFixesCursorImage
    {
        public short X, Y;
        public ushort Width, Height, XHot, YHot;
        public nuint CursorSerial;
        public nuint* Pixels; // one unsigned long per pixel, ARGB in its low 32 bits
        public nuint Atom;
        public IntPtr Name;
    }

    static readonly ErrorHandler IgnoreErrors = (_, _) => 0;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ErrorHandler(IntPtr display, IntPtr error);

    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11)] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(ErrorHandler handler);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport(XFixes)] static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(XFixes)] static extern XFixesCursorImage* XFixesGetCursorImage(IntPtr display);
}
