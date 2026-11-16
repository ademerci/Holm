using System;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Caps Lock from the keyboard's modifier state (CGEventSourceFlagsState). Watching key events would need the
/// Accessibility or Input Monitoring permission; reading the modifier state needs none, so it is read four times
/// a second: one cheap call, and no key is ever seen.
/// </summary>
public sealed class MacCapsLockPlatform : ICapsLockPlatform
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const int HidSystemState = 1;
    const ulong AlphaShift = 1UL << 16;

    Timer? _timer;
    bool _last;

    public event Action<bool>? Changed;

    public void Start()
    {
        if (_timer != null) return;
        try { _last = CapsOn(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return; }
        _timer = new Timer(_ => Check(), null, 250, 250);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    void Check()
    {
        bool on = CapsOn();
        if (on == _last) return;
        _last = on;
        Changed?.Invoke(on);
    }

    static bool CapsOn() => (CGEventSourceFlagsState(HidSystemState) & AlphaShift) != 0;

    [DllImport(CoreGraphics)] static extern ulong CGEventSourceFlagsState(int stateId);
}
