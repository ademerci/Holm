using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// The frontmost window from the window server's list (CGWindowListCopyWindowInfo, which gives bounds and owners
/// without any permission): filling the screen means fullscreen, filling all but the menu bar and the Dock means zoomed.
/// </summary>
public sealed class MacForegroundPlatform : IForegroundPlatform
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;

    public ForegroundWindow Read(int x, int y, int width, int height)
    {
        IntPtr list = IntPtr.Zero;
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            double scale = ObjC.SendReturnDouble(ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen")), ObjC.Sel("backingScaleFactor"));
            if (scale <= 0) scale = 1;
            double sx = x / scale, sy = y / scale, sw = width / scale, sh = height / scale;
            list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
            if (list == IntPtr.Zero) return default;
            int own = Environment.ProcessId;
            for (nint i = 0, n = CF.CFArrayGetCount(list); i < n; i++)
            {
                IntPtr info = CF.CFArrayGetValueAtIndex(list, i);
                if (CF.Number(CF.Get(info, "kCGWindowLayer")) is not 0) continue; // menus, the Dock, overlays
                if (CF.Number(CF.Get(info, "kCGWindowOwnerPID")) is { } pid && (int)pid == own) continue;
                if (!CGRectMakeWithDictionaryRepresentation(CF.Get(info, "kCGWindowBounds"), out var r)) continue;
                double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                if (cx < sx || cx >= sx + sw || cy < sy || cy >= sy + sh) return default; // the front window is elsewhere
                bool fullscreen = Math.Abs(r.Width - sw) < 1 && Math.Abs(r.Height - sh) < 1;
                bool zoomed = !fullscreen && r.Width >= sw * 0.98 && r.Height >= sh * 0.85;
                return new ForegroundWindow(fullscreen, CF.Text(CF.Get(info, "kCGWindowOwnerName")) ?? "", zoomed);
            }
            return default;
        }
        catch { return default; }
        finally
        {
            if (list != IntPtr.Zero) CF.CFRelease(list);
            ObjC.objc_autoreleasePoolPop(pool);
        }
    }

    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out NSRect rect);
}
