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
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
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

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, char[] name, ref int size);

    static uint _lastPid;
    static string _lastExe = "";

    /// <summary>File name (without .exe) of the program that owns a window; remembers the last answer.</summary>
    public static string ProcessNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return "";
        if (pid == _lastPid) return _lastExe;
        string exe = "";
        IntPtr process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process != IntPtr.Zero)
        {
            var buffer = new char[1024];
            int size = buffer.Length;
            if (QueryFullProcessImageName(process, 0, buffer, ref size))
                exe = System.IO.Path.GetFileNameWithoutExtension(new string(buffer, 0, size));
            CloseHandle(process);
        }
        (_lastPid, _lastExe) = (pid, exe);
        return exe;
    }

    public const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    /// <summary>Whether the left or right mouse button is held (physical buttons, so swapped buttons count too).</summary>
    public static bool MouseButtonDown() => GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetUserNameEx(int format, System.Text.StringBuilder name, ref uint size);

    static string? _displayName;

    /// <summary>How other Holm users see you: your Windows display name, or the account name.</summary>
    public static string DisplayName()
    {
        if (_displayName != null) return _displayName;
        string name = "";
        try
        {
            var buffer = new System.Text.StringBuilder(256);
            uint size = (uint)buffer.Capacity;
            if (GetUserNameEx(3 /* NameDisplay */, buffer, ref size)) name = buffer.ToString().Trim();
        }
        catch { }
        if (name.Length == 0) name = Environment.UserName;
        return _displayName = name.Length > 40 ? name[..40] : name;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The user's Downloads folder (wherever they moved it), or the usual place.</summary>
    public static string DownloadsFolder()
    {
        var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref downloads, 0, IntPtr.Zero, out IntPtr path) == 0)
        {
            try { return Marshal.PtrToStringUni(path) ?? ""; }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Where Windows saves screenshots (Win+Print Screen, and the Snipping Tool by default), or null.</summary>
    public static string? ScreenshotsFolder()
    {
        var screenshots = new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
        int result = SHGetKnownFolderPath(ref screenshots, 0, IntPtr.Zero, out IntPtr path);
        try { return result == 0 ? Marshal.PtrToStringUni(path) : null; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    /// <summary>The window of the app that last put something on the clipboard.</summary>
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();

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

    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint exitCode);

    /// <summary>Ends this process at once, without running anything else on the way out.</summary>
    public static void EndCurrentProcess(uint exitCode) => TerminateProcess(GetCurrentProcess(), exitCode);
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
