using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>
/// Intercepts the volume and media keys before Windows sees them (the WPF app's MediaKeys.cs), so Windows doesn't
/// show its own pop-up next to the notch's. Every other key passes untouched, and is never stored or logged.
/// </summary>
public sealed class WindowsMediaKeysPlatform : IMediaKeysPlatform
{
    const int VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF, NextTrack = 0xB0, PreviousTrack = 0xB1, StopMedia = 0xB2, PlayPause = 0xB3;
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc;
    Func<MediaKey, bool>? _takes;
    Action<MediaKey>? _press;
    IntPtr _hook;

    public WindowsMediaKeysPlatform() => _proc = OnKey;

    public bool IsSupported => true;

    public bool Start(Func<MediaKey, bool> takes, Action<MediaKey> press)
    {
        Stop();
        _takes = takes;
        _press = press;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _takes = null;
        _press = null;
    }

    static MediaKey? Map(int vk) => vk switch
    {
        VolumeMute => MediaKey.VolumeMute,
        VolumeDown => MediaKey.VolumeDown,
        VolumeUp => MediaKey.VolumeUp,
        NextTrack => MediaKey.NextTrack,
        PreviousTrack => MediaKey.PreviousTrack,
        StopMedia => MediaKey.StopMedia,
        PlayPause => MediaKey.PlayPause,
        _ => null,
    };

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            if (Map(vk) is { } key && _takes?.Invoke(key) == true)
            {
                int message = wParam.ToInt32();
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN) _press?.Invoke(key);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
