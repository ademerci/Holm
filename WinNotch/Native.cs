using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace WinNotch;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x00040000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    public const int WM_DISPLAYCHANGE = 0x007E, WM_POWERBROADCAST = 0x0218, WM_DEVICECHANGE = 0x0219, WM_DPICHANGED = 0x02E0;
    public const int PBT_APMPOWERSTATUSCHANGE = 0x000A, PBT_POWERSETTINGCHANGE = 0x8013, DBT_DEVNODES_CHANGED = 0x0007;

    public static Guid GUID_BATTERY_PERCENTAGE_REMAINING = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    public static Guid GUID_ACDC_POWER_SOURCE = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>A connected display. <see cref="Device"/> (e.g. \\.\DISPLAY2) is what the settings remember.</summary>
    public sealed record Screen(IntPtr Handle, string Device, RECT Bounds, bool Primary);

    /// <summary>All displays, left to right.</summary>
    public static List<Screen> Screens()
    {
        var screens = new List<Screen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
                screens.Add(new Screen(monitor, info.szDevice, info.rcMonitor, (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return screens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
    }
    [DllImport("user32.dll")] public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    /// <summary>Asks the shell whether a fullscreen app (game, video, presentation) is in front.</summary>
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [StructLayout(LayoutKind.Sequential)] struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Windows 11 "efficiency mode" (EcoQoS): the scheduler may run us on slow, low-power cores.</summary>
    public static void EnableEcoQoS()
    {
        var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = 1 };
        SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
    }

    public static void TrimWorkingSet() => EmptyWorkingSet(GetCurrentProcess());
}

internal readonly record struct PowerInfo(bool HasBattery, int Percent, bool OnAc, bool Charging)
{
    public static PowerInfo Read()
    {
        if (!Native.GetSystemPowerStatus(out var s)) return default;
        bool hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryLifePercent <= 100;
        return new PowerInfo(hasBattery, s.BatteryLifePercent, s.ACLineStatus == 1, s.BatteryFlag != 255 && (s.BatteryFlag & 8) != 0);
    }
}
