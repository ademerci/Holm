using System;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// macOS has no notification for the clipboard: every clipboard tool reads the pasteboard's change count, a
/// counter that goes up with each copy. This is the one thing Holm checks on a timer, twice a second, and reading a
/// number costs next to nothing; the content itself is only read when the number moved.
/// </summary>
public sealed class MacClipboardPlatform : IClipboardPlatform
{
    Timer? _timer;
    nint _lastCount = -1;

    public event Action? Changed;

    public void Start()
    {
        if (_timer != null) return;
        _lastCount = ChangeCount();
        _timer = new Timer(_ => Check(), null, 500, 500);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    void Check()
    {
        nint count = ChangeCount();
        if (count == _lastCount || count < 0) return;
        _lastCount = count;
        Changed?.Invoke();
    }

    static nint ChangeCount()
    {
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            IntPtr board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
            return board == IntPtr.Zero ? -1 : ObjC.SendReturnLong(board, ObjC.Sel("changeCount"));
        }
        catch { return -1; }
        finally { ObjC.objc_autoreleasePoolPop(pool); }
    }
}
