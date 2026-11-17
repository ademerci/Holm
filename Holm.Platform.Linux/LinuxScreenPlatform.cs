using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// Captures the screen for the colour picker and screenshot OCR.
/// On Wayland, X11's root window only holds XWayland windows, so we try native Wayland capture first (grim on
/// wlroots, org.freedesktop.portal.Screenshot via D-Bus on GNOME/KDE, spectacle/gnome-screenshot as fallbacks),
/// then fall back to clamped, error-guarded XGetImage on X11.
/// </summary>
public sealed class LinuxScreenPlatform : IScreenPlatform
{
    const string X11 = "libX11.so.6";
    const int ZPixmap = 2;

    static bool IsWayland =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public async Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        if (IsWayland)
        {
            if (await CaptureWaylandAsync(x, y, width, height).ConfigureAwait(false) is { } wayland)
                return wayland;
        }
        return await Task.Run(() => CaptureX11(x, y, width, height)).ConfigureAwait(false);
    }

    static async Task<ScreenImage?> CaptureWaylandAsync(int x, int y, int width, int height)
    {
        if (await CaptureGrimAsync(x, y, width, height).ConfigureAwait(false) is { } grim)
            return grim;
        if (await CapturePortalAsync(x, y, width, height).ConfigureAwait(false) is { } portal)
            return portal;
        if (await CaptureCliFileAsync("spectacle", new[] { "-b", "-n", "-f", "-o" }, x, y, width, height).ConfigureAwait(false) is { } spectacle)
            return spectacle;
        if (await CaptureCliFileAsync("gnome-screenshot", new[] { "-f" }, x, y, width, height).ConfigureAwait(false) is { } gnomeShot)
            return gnomeShot;
        return null;
    }

    static async Task<ScreenImage?> CaptureGrimAsync(int x, int y, int width, int height)
    {
        if (FindExe("grim") is not { } exe) return null;
        try
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-g");
            psi.ArgumentList.Add($"{x},{y} {width}x{height}");
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("png");
            psi.ArgumentList.Add("-");
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            using var ms = new MemoryStream();
            var copy = proc.StandardOutput.BaseStream.CopyToAsync(ms);
            _ = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await Task.WhenAll(copy, proc.WaitForExitAsync(cts.Token)).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch (InvalidOperationException) { } // a grim that hangs mustn't stay behind
                return null;
            }
            if (proc.ExitCode != 0 || ms.Length == 0) return null;
            return DecodeImage(ms.ToArray(), 0, 0, width, height, allowScaledWhole: true);
        }
        catch { return null; }
    }

    static async Task<ScreenImage?> CapturePortalAsync(int x, int y, int width, int height)
    {
        try
        {
            if (await Bus.Session.ConnectAsync().ConfigureAwait(false) is not { } connection) return null;
            string token = "holm_" + Guid.NewGuid().ToString("N")[..12];
            string? sender = connection.UniqueName?.TrimStart(':').Replace('.', '_');
            string expectedPath = sender is { Length: > 0 }
                ? $"/org/freedesktop/portal/desktop/request/{sender}/{token}"
                : "";

            var tcs = new TaskCompletionSource<(uint Code, string? Uri)>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? actualPath = null;

            var rule = new MatchRule
            {
                Type = MessageType.Signal,
                Sender = "org.freedesktop.portal.Desktop",
                Interface = "org.freedesktop.portal.Request",
                Member = "Response",
            };
            using var sub = await connection.AddMatchAsync(
                rule,
                static (Message m, object? _) => ReadPortalResponse(m),
                (Notification<(string Path, uint Code, string? Uri)> n) =>
                {
                    if (!n.HasValue) return;
                    var (path, code, uri) = n.Value;
                    if (path == expectedPath || (actualPath != null && path == actualPath) || (expectedPath.Length == 0 && path.EndsWith("/" + token, StringComparison.Ordinal)))
                        tcs.TrySetResult((code, uri));
                },
                false,
                ObserverFlags.None).ConfigureAwait(false);

            MessageBuffer call;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(
                    "org.freedesktop.portal.Desktop",
                    "/org/freedesktop/portal/desktop",
                    "org.freedesktop.portal.Screenshot",
                    "Screenshot",
                    "sa{sv}");
                writer.WriteString("");
                var dictStart = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("handle_token");
                writer.WriteVariant(VariantValue.String(token));
                writer.WriteDictionaryEntryStart();
                writer.WriteString("interactive");
                writer.WriteVariant(VariantValue.Bool(false));
                writer.WriteDictionaryEnd(dictStart);
                call = writer.CreateMessage();
            }

            actualPath = await connection.CallMethodAsync(call, static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
            var (responseCode, uri) = await tcs.Task.ConfigureAwait(false);
            if (responseCode != 0 || string.IsNullOrEmpty(uri)) return null;

            string localPath = new Uri(uri).LocalPath;
            if (!File.Exists(localPath)) return null;
            byte[] bytes = await File.ReadAllBytesAsync(localPath, cts.Token).ConfigureAwait(false);
            try { File.Delete(localPath); } catch { }
            return DecodeImage(bytes, x, y, width, height, allowScaledWhole: true);
        }
        catch { return null; }
    }

    static (string Path, uint Code, string? Uri) ReadPortalResponse(Message m)
    {
        var reader = m.GetBodyReader();
        uint code = reader.ReadUInt32();
        var results = reader.ReadDictionaryOfStringToVariantValue();
        string? uri = results.TryGetValue("uri", out var v) && v.Type == VariantValueType.String ? v.GetString() : null;
        return (m.PathAsString ?? "", code, uri);
    }

    static async Task<ScreenImage?> CaptureCliFileAsync(string exeName, string[] argsBeforePath, int x, int y, int width, int height)
    {
        if (FindExe(exeName) is not { } exe) return null;
        string path = Path.Combine(Path.GetTempPath(), $"holm-pick-{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in argsBeforePath) psi.ArgumentList.Add(arg);
            psi.ArgumentList.Add(path);
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch (InvalidOperationException) { } // one that hangs mustn't stay behind
                return null;
            }
            if (proc.ExitCode != 0 || !File.Exists(path)) return null;
            byte[] bytes = await File.ReadAllBytesAsync(path, cts.Token).ConfigureAwait(false);
            return DecodeImage(bytes, x, y, width, height, allowScaledWhole: true);
        }
        catch { return null; }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }

    static string? FindExe(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':'))
        {
            if (folder.Length == 0) continue;
            string candidate = Path.Combine(folder, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    internal static ScreenImage? DecodeImage(byte[] encoded, int x, int y, int width, int height, bool allowScaledWhole)
    {
        if (encoded.Length == 0) return null;
        try
        {
            using var skData = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(skData);
            if (codec == null) return null;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return null;

            int sx = 0, sy = 0, sw = decoded.Width, sh = decoded.Height;
            if (decoded.Width == width && decoded.Height == height)
            {
                // exact match
            }
            else if (x >= 0 && y >= 0 && decoded.Width >= x + width && decoded.Height >= y + height
                     && (x > 0 || y > 0 || !allowScaledWhole || Math.Abs((double)decoded.Width / decoded.Height - (double)width / height) > 0.05))
            {
                sx = x; sy = y; sw = width; sh = height;
            }
            else if (!allowScaledWhole)
            {
                return null;
            }

            using var target = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(target))
            {
                canvas.DrawBitmap(decoded, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(0, 0, sw, sh));
            }

            var bgra = new byte[sw * sh * 4];
            IntPtr ptr = target.GetPixels();
            int rowBytes = target.RowBytes;
            for (int row = 0; row < sh; row++)
            {
                Marshal.Copy(ptr + row * rowBytes, bgra, row * sw * 4, sw * 4);
                for (int i = row * sw * 4 + 3; i < (row + 1) * sw * 4; i += 4) bgra[i] = 0xFF;
            }
            return new ScreenImage(sw, sh, bgra);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    static unsafe ScreenImage? CaptureX11(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        IntPtr display = IntPtr.Zero, image = IntPtr.Zero, prevHandler = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            prevHandler = XSetErrorHandler(IgnoreErrors);

            IntPtr root = XDefaultRootWindow(display);
            if (XGetGeometry(display, root, out _, out _, out _, out uint rootW, out uint rootH, out _, out _) == 0)
                return null;

            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min((int)rootW, x + width), y1 = Math.Min((int)rootH, y + height);
            int rw = x1 - x0, rh = y1 - y0;
            if (rw <= 0 || rh <= 0) return null;

            image = XGetImage(display, root, x0, y0, (uint)rw, (uint)rh, ulong.MaxValue, ZPixmap);
            if (image == IntPtr.Zero) return null;

            var info = (XImage*)image;
            if (info->BitsPerPixel is not (32 or 24)) return null;

            var bgra = new byte[width * height * 4];
            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 0xFF;

            int dstOffX = x0 - x, dstOffY = y0 - y;
            int bppBytes = info->BitsPerPixel / 8;
            byte* srcBase = (byte*)info->Data;
            for (int row = 0; row < rh; row++)
            {
                byte* src = srcBase + row * info->BytesPerLine;
                int dstRow = ((dstOffY + row) * width + dstOffX) * 4;
                if (info->BitsPerPixel == 32 && info->ByteOrder == 0)
                {
                    var srcSpan = new ReadOnlySpan<byte>(src, rw * 4);
                    var dstSpan = bgra.AsSpan(dstRow, rw * 4);
                    srcSpan.CopyTo(dstSpan);
                    for (int i = 3; i < dstSpan.Length; i += 4) dstSpan[i] = 0xFF;
                }
                else if (info->BitsPerPixel == 32)
                {
                    for (int col = 0; col < rw; col++)
                    {
                        byte* p = src + col * 4;
                        int d = dstRow + col * 4;
                        bgra[d] = p[3]; bgra[d + 1] = p[2]; bgra[d + 2] = p[1]; bgra[d + 3] = 0xFF;
                    }
                }
                else
                {
                    for (int col = 0; col < rw; col++)
                    {
                        byte* p = src + col * bppBytes;
                        int d = dstRow + col * 4;
                        if (info->ByteOrder == 0) { bgra[d] = p[0]; bgra[d + 1] = p[1]; bgra[d + 2] = p[2]; }
                        else { bgra[d] = p[2]; bgra[d + 1] = p[1]; bgra[d + 2] = p[0]; }
                        bgra[d + 3] = 0xFF;
                    }
                }
            }
            return new ScreenImage(width, height, bgra);
        }
        catch (DllNotFoundException) { return null; }
        finally
        {
            if (image != IntPtr.Zero)
            {
                var info = (XImage*)image;
                if (info->Data != IntPtr.Zero) XFree(info->Data);
                XFree(image);
            }
            if (prevHandler != IntPtr.Zero) XSetErrorHandler(prevHandler);
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }

    static readonly ErrorHandler IgnoreErrors = (_, _) => 0;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ErrorHandler(IntPtr display, IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    struct XImage
    {
        public int Width, Height, XOffset, Format;
        public IntPtr Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11)] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(ErrorHandler handler);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(IntPtr handler);

    /// <summary>Tesseract, when it is installed: the common OCR on Linux, run on this computer.</summary>
    public bool CanReadText => _tesseract.Value != null;

    static readonly Lazy<string?> _tesseract = new(() =>
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':'))
            if (folder.Length > 0 && Path.Exists(Path.Combine(folder, "tesseract"))) return Path.Combine(folder, "tesseract");
        return null;
    });

    public async Task<string?> ReadTextAsync(string path)
    {
        if (_tesseract.Value is not { } tesseract) return null;
        var start = new ProcessStartInfo(tesseract) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("stdout");
        using var process = Process.Start(start);
        if (process == null) return null;
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) return null;
        return string.Join(Environment.NewLine, (await output).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    public IScreenGrabber? OpenGrabber(int x, int y, int areaWidth, int areaHeight, int width, int height)
        => LinuxScreenGrabber.Open(x, y, areaWidth, areaHeight, width, height);

    /// <summary>X11 has no way to keep a window out of another app's pictures: the notch shows in a stream here.</summary>
    public bool HideFromCapture(IntPtr window, bool hide) => false;
}
