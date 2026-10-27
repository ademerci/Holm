using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Tells when Caps Lock is switched on or off, from a low-level keyboard hook that looks at that one key only:
/// every other key is passed on untouched, never stored and never logged.
/// </summary>
internal sealed class CapsLockWatcher : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extraInfo; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);

    readonly HookProc _proc; // kept alive while the hook is in
    readonly Dispatcher _dispatcher;
    readonly CapsKey _key = new();
    readonly DispatcherTimer _settle;
    IntPtr _hook;
    bool _last;

    /// <summary>Raised on the UI thread with the new state.</summary>
    public event Action<bool>? Changed;

    public static bool IsOn => (GetKeyState(CapsKey.VirtualKey) & 1) != 0;

    public CapsLockWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = OnKey;
        // Windows flips the toggle after the hook has seen the key: read it a moment later.
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => Report(), dispatcher);
        _settle.Stop();
        _last = IsOn;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            // Only the key code is read, and only compared with Caps Lock's.
            uint vk = (uint)Marshal.ReadInt32(lParam);
            if (_key.Pressed(vk, message is WM_KEYDOWN or WM_SYSKEYDOWN, message is WM_KEYUP or WM_SYSKEYUP))
                _dispatcher.InvokeAsync(() => { _settle.Stop(); _settle.Start(); });
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    void Report()
    {
        _settle.Stop();
        bool on = IsOn;
        if (on == _last) return;
        _last = on;
        Changed?.Invoke(on);
    }

    public void Dispose()
    {
        _settle.Stop();
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}

/// <summary>Caps Lock going down, once per press: holding it repeats the key, which must not count again.</summary>
internal sealed class CapsKey
{
    public const int VirtualKey = 0x14; // VK_CAPITAL

    bool _down;

    public bool Pressed(uint vk, bool down, bool up)
    {
        if (vk != VirtualKey) return false;
        if (up) { _down = false; return false; }
        if (!down || _down) return false;
        _down = true;
        return true;
    }
}
