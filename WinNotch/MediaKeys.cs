using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The volume and media keys, taken before Windows sees them (Settings › Pop-outs › Volume and media only in the
/// notch): Holm changes the volume or the track itself, so Windows never opens its own pop-up and only the notch shows
/// it. Windows has no way to hide that pop-up from another app; a key Windows never receives makes none. Every other key
/// passes untouched, and is never stored or logged.
/// </summary>
internal sealed class MediaKeys : IDisposable
{
    public const int VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF, NextTrack = 0xB0, PreviousTrack = 0xB1, StopMedia = 0xB2, PlayPause = 0xB3;
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc; // kept alive while the hook is in
    readonly Dispatcher _dispatcher;
    readonly Func<int, bool> _takes;
    readonly Action<int> _press;
    IntPtr _hook;

    /// <param name="takes">Whether Holm handles this key now (asked on the UI thread, must be quick); otherwise Windows gets it.</param>
    /// <param name="press">What a press does; runs right after, on the UI thread, once per press and per auto-repeat.</param>
    public MediaKeys(Dispatcher dispatcher, Func<int, bool> takes, Action<int> press)
    {
        _dispatcher = dispatcher;
        _takes = takes;
        _press = press;
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static bool IsOurs(int vk) => vk is >= VolumeMute and <= PlayPause;

    /// <summary>The volume after a volume key, in Windows' own steps of 2 % (snapped to them, like Windows does).</summary>
    public static float Step(float level, bool up)
    {
        int percent = (int)MathF.Round(Math.Clamp(level, 0, 1) * 100);
        percent = up ? (percent / 2 + 1) * 2 : (percent + 1) / 2 * 2 - 2;
        return Math.Clamp(percent, 0, 100) / 100f;
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            // Only the key code is read, and only compared with the volume and media keys.
            int vk = Marshal.ReadInt32(lParam);
            if (IsOurs(vk) && _takes(vk))
            {
                int message = wParam.ToInt32();
                // Posted: a hook has to answer at once, and the volume is changed through COM.
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN) _dispatcher.InvokeAsync(() => _press(vk));
                return (IntPtr)1; // Windows doesn't see it (the key going up neither), so it shows no pop-up
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
