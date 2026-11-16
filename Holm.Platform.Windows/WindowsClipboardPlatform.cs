using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>
/// A message-only window registered as a clipboard format listener: Windows sends it WM_CLIPBOARDUPDATE after each
/// copy. Created on the UI thread, whose message loop (Avalonia's) delivers the messages; nothing is polled.
/// </summary>
public sealed unsafe class WindowsClipboardPlatform : IClipboardPlatform
{
    const uint WmClipboardUpdate = 0x031D;
    static readonly IntPtr HwndMessage = new(-3);
    static WindowsClipboardPlatform? _current;
    static ushort _class;
    IntPtr _window;

    public event Action? Changed;

    public void Start()
    {
        if (_window != IntPtr.Zero) return;
        _current = this;
        IntPtr instance = GetModuleHandleW(IntPtr.Zero);
        if (_class == 0)
        {
            fixed (char* name = "HolmClipboardListener")
            {
                var wc = new WndClassEx
                {
                    Size = (uint)sizeof(WndClassEx),
                    WndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
                    Instance = instance,
                    ClassName = (IntPtr)name,
                };
                _class = RegisterClassExW(ref wc);
            }
            if (_class == 0) return;
        }
        _window = CreateWindowExW(0, (IntPtr)_class, IntPtr.Zero, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window != IntPtr.Zero) AddClipboardFormatListener(_window);
    }

    public void Stop()
    {
        if (_window == IntPtr.Zero) return;
        RemoveClipboardFormatListener(_window);
        DestroyWindow(_window);
        _window = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    static IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        if (message == WmClipboardUpdate)
        {
            try { _current?.Changed?.Invoke(); } catch { }
            return IntPtr.Zero;
        }
        return DefWindowProcW(window, message, w, l);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WndClassEx
    {
        public uint Size, Style;
        public IntPtr WndProc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background, MenuName, ClassName, IconSmall;
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(IntPtr name);
    [DllImport("user32.dll")] static extern ushort RegisterClassExW(ref WndClassEx wc);
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(uint exStyle, IntPtr className, IntPtr title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr window);
}
