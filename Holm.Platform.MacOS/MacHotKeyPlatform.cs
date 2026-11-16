using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Control+H through the Carbon hot key service, which needs no permission: macOS hands that one combination to
/// Holm alone and calls back on the main thread. (⌘H hides the front app on every Mac, so Control it is.)
/// </summary>
public sealed unsafe class MacHotKeyPlatform : IHotKeyPlatform
{
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    const uint KeyH = 4, ControlKey = 0x1000;
    const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    const uint EventHotKeyPressed = 5;

    static MacHotKeyPlatform? _current;
    static bool _handlerInstalled;
    IntPtr _hotKey;

    public event Action? Pressed;

    public bool Start()
    {
        _current = this;
        try
        {
            if (!_handlerInstalled)
            {
                var spec = new EventTypeSpec { Class = EventClassKeyboard, Kind = EventHotKeyPressed };
                delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> handler = &OnHotKey;
                if (InstallEventHandler(GetApplicationEventTarget(), (IntPtr)handler, 1, ref spec, IntPtr.Zero, out _) != 0) return false;
                _handlerInstalled = true;
            }
            if (_hotKey != IntPtr.Zero) return true;
            var id = new EventHotKeyId { Signature = 0x484F4C4D /* 'HOLM' */, Id = 1 };
            return RegisterEventHotKey(KeyH, ControlKey, id, GetApplicationEventTarget(), 0, out _hotKey) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public void Stop()
    {
        if (_hotKey == IntPtr.Zero) return;
        UnregisterEventHotKey(_hotKey);
        _hotKey = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    static int OnHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try { _current?.Pressed?.Invoke(); } catch { }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] struct EventTypeSpec { public uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] struct EventHotKeyId { public uint Signature, Id; }

    [DllImport(Carbon)] static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint count, ref EventTypeSpec types, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKey);
    [DllImport(Carbon)] static extern int UnregisterEventHotKey(IntPtr hotKey);
}
