using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Picks one color from anywhere on the screen. A low-level mouse hook takes the next click (so it never reaches
/// the app below), a magnifier beside the cursor shows the pixels around it, and Escape or a right click cancels.
/// There is no overlay over the screen: the pixel read is exactly what the display shows.
/// </summary>
internal sealed class ColorPicker : IDisposable
{
    const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
              WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_HOTKEY = 0x0312, VK_ESCAPE = 0x1B, HotkeyId = 0x4E31;
    const int Zoom = 10;

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public Native.POINT pt; public uint mouseData, flags, time; public IntPtr extraInfo; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    readonly HookProc _proc; // kept alive while the hook is in
    readonly Window _loupe;
    readonly Image _zoomed;
    readonly Border _swatch;
    readonly TextBlock _hex;
    readonly WriteableBitmap _pixels = new(ColorPick.Span, ColorPick.Span, 96, 96, PixelFormats.Bgr32, null);
    readonly byte[] _buffer = new byte[ColorPick.Span * ColorPick.Span * 4];
    readonly DispatcherTimer _giveUp;
    IntPtr _hook, _loupeHwnd;
    Native.POINT _cursor;
    bool _redrawQueued, _swallowUp, _done;

    /// <summary>The color clicked as "#RRGGBB", or null when cancelled. Raised once.</summary>
    public event Action<string?, Color>? Done;

    public ColorPicker()
    {
        _proc = OnMouse;
        _zoomed = new Image { Source = _pixels, Width = ColorPick.Span * Zoom, Height = ColorPick.Span * Zoom };
        RenderOptions.SetBitmapScalingMode(_zoomed, BitmapScalingMode.NearestNeighbor);
        var center = new Border
        {
            Width = Zoom + 2, Height = Zoom + 2, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) };
        _hex = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 0) };
        label.Children.Add(_swatch);
        label.Children.Add(_hex);
        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Grid { Children = { _zoomed, center } } });
        stack.Children.Add(label);

        _loupe = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            Topmost = true, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, Focusable = false,
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x10, 0x12)), CornerRadius = new CornerRadius(16), Padding = new Thickness(8), Child = stack },
        };
        _loupe.SourceInitialized += (_, _) =>
        {
            _loupeHwnd = new WindowInteropHelper(_loupe).Handle;
            long ex = Native.GetWindowLongPtr(_loupeHwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(_loupeHwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | 0x20 /* WS_EX_TRANSPARENT */));
            SetWindowDisplayAffinity(_loupeHwnd, 0x11); // WDA_EXCLUDEFROMCAPTURE: never in its own pixels
            HwndSource.FromHwnd(_loupeHwnd)?.AddHook(OnLoupeMessage);
            RegisterHotKey(_loupeHwnd, HotkeyId, 0x4000 /* MOD_NOREPEAT */, VK_ESCAPE);
        };
        // Nobody picks for a minute: give up. After a pick, the button's release is waited for at most a moment.
        _giveUp = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) =>
        {
            if (_done) { _giveUp!.Stop(); Unhook(); }
            else Finish(null);
        }, Dispatcher.CurrentDispatcher);
    }

    public void Start()
    {
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        Native.GetCursorPos(out _cursor);
        _loupe.Show();
        Redraw();
        _giveUp.Start();
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _hook == IntPtr.Zero) return CallNextHookEx(_hook, code, wParam, lParam);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        switch (wParam.ToInt32())
        {
            case WM_MOUSEMOVE:
                _cursor = info.pt;
                if (!_redrawQueued && !_done)
                {
                    _redrawQueued = true; // a mouse reports up to 1000 times a second: draw once per frame
                    _loupe.Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
                }
                break;
            case WM_LBUTTONDOWN:
                _cursor = info.pt;
                _swallowUp = true;
                _loupe.Dispatcher.InvokeAsync(() => Finish(Read(_cursor)));
                return new IntPtr(1); // the click is ours, not the app's below
            case WM_RBUTTONDOWN:
                _swallowUp = true;
                _loupe.Dispatcher.InvokeAsync(() => Finish(null));
                return new IntPtr(1);
            case WM_LBUTTONUP or WM_RBUTTONUP when _swallowUp:
                _swallowUp = false;
                _loupe.Dispatcher.InvokeAsync(Unhook);
                return new IntPtr(1);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    IntPtr OnLoupeMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId) { handled = true; Finish(null); }
        return IntPtr.Zero;
    }

    void Redraw()
    {
        _redrawQueued = false;
        if (_done) return;
        var cursor = _cursor;
        if (!Grab(cursor)) return;
        _pixels.WritePixels(new Int32Rect(0, 0, ColorPick.Span, ColorPick.Span), _buffer, ColorPick.Span * 4, 0);
        var (r, g, b) = ColorPick.At(_buffer, ColorPick.Span * 4, ColorPick.Reach, ColorPick.Reach);
        _swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        _hex.Text = ColorPick.Hex(r, g, b);

        if (_loupeHwnd == IntPtr.Zero) return;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(_loupe);
        int width = (int)Math.Ceiling(_loupe.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(_loupe.ActualHeight * dpi.DpiScaleY);
        var (x, y) = ColorPick.LoupeAt(cursor.X, cursor.Y, width, height, info.rcMonitor);
        Native.SetWindowPos(_loupeHwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    (byte R, byte G, byte B)? Read(Native.POINT at)
        => Grab(at) ? ColorPick.At(_buffer, ColorPick.Span * 4, ColorPick.Reach, ColorPick.Reach) : null;

    /// <summary>Copies the pixels around a point of the screen (physical pixels) into the buffer.</summary>
    bool Grab(Native.POINT at)
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = ColorPick.Span, biHeight = -ColorPick.Span, // top-down
                biPlanes = 1, biBitCount = 32,
            };
            bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) return false;
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, ColorPick.Span, ColorPick.Span, screen, at.X - ColorPick.Reach, at.Y - ColorPick.Reach, 0x00CC0020 /* SRCCOPY */))
                return false;
            Marshal.Copy(bits, _buffer, 0, _buffer.Length);
            return true;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    void Finish((byte R, byte G, byte B)? color)
    {
        if (_done) return;
        _done = true;
        _giveUp.Stop();
        if (!_swallowUp) Unhook(); // otherwise once the button is up, so the app never sees half a click
        else
        {
            _giveUp.Interval = TimeSpan.FromSeconds(2);
            _giveUp.Start();
        }
        if (_loupeHwnd != IntPtr.Zero) UnregisterHotKey(_loupeHwnd, HotkeyId);
        _loupe.Close();
        if (color is { } c) Done?.Invoke(ColorPick.Hex(c.R, c.G, c.B), Color.FromRgb(c.R, c.G, c.B));
        else Done?.Invoke(null, default);
    }

    void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        Finish(null);
        Unhook();
    }
}
