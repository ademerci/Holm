using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// The screen's pixels through macOS's own screencapture tool: a PNG of the rectangle, which the app decodes. The
/// first time, macOS asks whether Holm may record the screen; without that the picture holds only the desktop.
/// The PNG is decoded by the caller (Avalonia), so this hands it over as it comes.
/// </summary>
public sealed class MacScreenPlatform : IScreenPlatform
{
    /// <summary>Set by the app: turns a PNG into BGRA pixels (the backend has no image library of its own).</summary>
    public static Func<byte[], ScreenImage?>? DecodePng;

    public Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height) => Task.Run(() => Capture(x, y, width, height));

    static ScreenImage? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || DecodePng == null) return null;
        string file = Path.Combine(Path.GetTempPath(), $"holm-screen-{Environment.ProcessId}.png");
        try
        {
            // -x: no sound; -R: the rectangle, in points (the caller speaks device pixels). The PNG comes back in
            // device pixels again.
            double scale = MainScreenScale();
            int px = (int)Math.Floor(x / scale), py = (int)Math.Floor(y / scale);
            int pw = (int)Math.Ceiling(width / scale), ph = (int)Math.Ceiling(height / scale);
            var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-x", "-t", "png", "-R", $"{px},{py},{pw},{ph}", file }) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            if (process == null || !process.WaitForExit(10_000) || process.ExitCode != 0 || !File.Exists(file)) return null;
            return DecodePng(File.ReadAllBytes(file));
        }
        catch { return null; }
        finally { try { File.Delete(file); } catch { } }
    }

    static double MainScreenScale()
    {
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            IntPtr screen = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen"));
            double scale = screen == IntPtr.Zero ? 1 : ObjC.SendReturnDouble(screen, ObjC.Sel("backingScaleFactor"));
            return scale > 0 ? scale : 1;
        }
        catch { return 1; }
        finally { ObjC.objc_autoreleasePoolPop(pool); }
    }
}
