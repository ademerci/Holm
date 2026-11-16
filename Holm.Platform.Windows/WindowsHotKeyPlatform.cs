using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>Ctrl+H through RegisterHotKey on a message-only window of its own (the WPF app registers it on its window).</summary>
public sealed unsafe class WindowsHotKeyPlatform : IHotKeyPlatform
{
    const uint WmHotKey = 0x0312, ModControl = 0x0002, ModNoRepeat = 0x4000, VkH = 0x48;
    const int HotKeyId = 0x484F; // "HO"
    static readonly IntPtr HwndMessage = new(-3);
    static WindowsHotKeyPlatform? _current;
    static ushort _class;
    IntPtr _window;
    bool _registered;

    public event Action? Pressed;

    public bool Start()
    {
        _current = this;
        if (_window == IntPtr.Zero)
        {
            IntPtr instance = GetModuleHandleW(IntPtr.Zero);
            if (_class == 0)
            {
                fixed (char* name = "HolmHotKey")
                {
                    var wc = new WndClassEx
                    {
                        Size = (uint)sizeof(WndClassEx), Instance = instance, ClassName = (IntPtr)name,
                        WndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
                    };
                    _class = RegisterClassExW(ref wc);
                }
                if (_class == 0) return false;
            }
            _window = CreateWindowExW(0, (IntPtr)_class, IntPtr.Zero, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
            if (_window == IntPtr.Zero) return false;
        }
        if (!_registered) _registered = RegisterHotKey(_window, HotKeyId, ModControl | ModNoRepeat, VkH);
        return _registered;
    }

    public void Stop()
    {
        if (!_registered) return;
        UnregisterHotKey(_window, HotKeyId);
        _registered = false;
    }

    [UnmanagedCallersOnly]
    static IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        if (message == WmHotKey && w.ToInt32() == HotKeyId)
        {
            try { _current?.Pressed?.Invoke(); } catch { }
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
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
