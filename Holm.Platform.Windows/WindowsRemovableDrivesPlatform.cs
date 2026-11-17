using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// Removable drives through the WPF app's UsbDrives (linked). Windows tells every top-level window when a volume
/// comes or goes (WM_DEVICECHANGE); a hidden one of our own listens, so nothing is polled.
/// </summary>
public sealed unsafe class WindowsRemovableDrivesPlatform : IRemovableDrivesPlatform
{
    const uint WmDeviceChange = 0x0219;
    const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
    static WindowsRemovableDrivesPlatform? _current;
    static ushort _class;
    IntPtr _window;

    public event Action<RemovableDrive>? Arrived;
    public event Action<string>? Removed;

    public void Start()
    {
        _current = this;
        if (_window != IntPtr.Zero) return;
        IntPtr instance = GetModuleHandleW(IntPtr.Zero);
        if (_class == 0)
        {
            fixed (char* name = "HolmDrives")
            {
                var wc = new WndClassEx
                {
                    Size = (uint)sizeof(WndClassEx), Instance = instance, ClassName = (IntPtr)name,
                    WndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
                };
                _class = RegisterClassExW(ref wc);
            }
            if (_class == 0) return;
        }
        // A top-level window that never shows: message-only windows don't get the broadcast.
        _window = CreateWindowExW(0x80 /* WS_EX_TOOLWINDOW */, (IntPtr)_class, IntPtr.Zero, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
    }

    public void Stop()
    {
        if (_window == IntPtr.Zero) return;
        DestroyWindow(_window);
        _window = IntPtr.Zero;
    }

    public List<RemovableDrive> Present() => UsbDrives.Present().Select(ToDrive).ToList();

    public string? Eject(string id) => id.Length == 1 ? UsbDrives.Eject(id[0]) : "Not a drive letter";

    static RemovableDrive ToDrive(UsbDrive d) => new(d.Letter.ToString(), d.Name, d.Root, d.Total, d.Free);

    [UnmanagedCallersOnly]
    static IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        if (message == WmDeviceChange)
        {
            try { _current?.OnDeviceChange(w, l); } catch { }
            return new IntPtr(1);
        }
        return DefWindowProcW(window, message, w, l);
    }

    /// <summary>Only plain drives, not media changes or network drives.</summary>
    void OnDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        int ev = wParam.ToInt32();
        if (ev is not (DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE) || lParam == IntPtr.Zero) return;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) return;
        uint mask = (uint)Marshal.ReadInt32(lParam, 12);
        short flags = Marshal.ReadInt16(lParam, 16);
        if ((flags & 0x3) != 0) return; // DBTF_MEDIA (a disc changed), DBTF_NET (a network drive)
        var letters = UsbDrives.Letters(mask);
        if (ev == DBT_DEVICEARRIVAL)
            System.Threading.Tasks.Task.Run(() =>
            {
                // The label can take a moment on a slow stick.
                foreach (var letter in letters) if (UsbDrives.Read(letter) is { } drive) Arrived?.Invoke(ToDrive(drive));
            });
        else foreach (var letter in letters) Removed?.Invoke(letter.ToString());
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
}
