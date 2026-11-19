using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// Caps Lock from a low-level keyboard hook that looks at that one key only (the WPF app's CapsLockWatcher,
/// without the WPF dispatcher): every other key is passed on untouched, never stored and never logged. Installed
/// on the UI thread, whose message loop runs the hook.
/// </summary>
public sealed class WindowsCapsLockPlatform : ICapsLockPlatform
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    readonly HookProc _proc;
    readonly CapsKey _key = new();
    IntPtr _hook;
    bool _last;

    public event Action<bool>? Changed;

    public WindowsCapsLockPlatform() => _proc = OnKey;

    static bool IsOn => (GetKeyState(CapsKey.VirtualKey) & 1) != 0;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _last = IsOn;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            // Only the key code is read, and only compared with Caps Lock's.
            uint vk = (uint)Marshal.ReadInt32(lParam);
            if (_key.Pressed(vk, message is WM_KEYDOWN or WM_SYSKEYDOWN, message is WM_KEYUP or WM_SYSKEYUP))
                _ = Task.Delay(60).ContinueWith(_ => Report()); // Windows flips the toggle after the hook has seen the key
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    void Report()
    {
        bool on = IsOn;
        if (on == _last) return;
        _last = on;
        Changed?.Invoke(on);
    }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
}
