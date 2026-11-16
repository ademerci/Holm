using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using WinNotch.Notify;

namespace Holm.Platform.MacOS;

/// <summary>
/// macOS lets no app read another's notifications, but the numbers on the Dock icons are public: lsappinfo reports
/// a running app's badge ("StatusLabel"). Only the messengers Holm knows are asked, every 3 s while one runs, so
/// the notch counts their unread messages as the WPF app does with taskbar badges.
/// </summary>
public sealed class MacNotificationsPlatform : INotificationsPlatform
{
    static readonly Regex Label = new("\"label\"=\"([^\"]*)\"", RegexOptions.Compiled);

    readonly object _gate = new();
    Dictionary<string, string> _badges = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _logos = new(StringComparer.OrdinalIgnoreCase);
    Timer? _timer;
    int _reading;

    public event Action? Changed;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Off;

    public string? AccessHint => Access == NotificationAccess.Unavailable ? "macOS doesn't share notification text – app badge counts only" : null;

    public void OpenAccessSettings() { }

    public Task StartAsync(bool ask)
    {
        Access = NotificationAccess.Unavailable; // the text: never; the badges: always
        _timer ??= new Timer(_ => Look(), null, 0, 3000);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_gate) _badges = new(StringComparer.OrdinalIgnoreCase);
        Access = NotificationAccess.Off;
    }

    public Task<NotificationSnapshot> ReadAsync()
    {
        lock (_gate) return Task.FromResult(new NotificationSnapshot(null, new Dictionary<string, string>(_badges), null));
    }

    public byte[]? LogoOf(string appId)
    {
        lock (_gate) return _logos.TryGetValue(appId, out var logo) ? logo : null;
    }

    void Look()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1) return;
        try
        {
            var badges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, bundle, path) in RunningMessengers())
            {
                badges[bundle] = BadgeOf(bundle) ?? "0";
                lock (_gate)
                    if (!_logos.ContainsKey(bundle) && IconPng(path) is { } png) _logos[bundle] = png;
            }
            bool changed;
            lock (_gate)
            {
                changed = badges.Count != _badges.Count || badges.Any(b => !_badges.TryGetValue(b.Key, out var old) || old != b.Value);
                _badges = badges;
            }
            if (changed) Changed?.Invoke();
        }
        catch { }
        finally { Interlocked.Exchange(ref _reading, 0); }
    }

    /// <summary>The running apps that are messengers Holm knows: name, bundle id ("net.whatsapp.WhatsApp"), bundle path.</summary>
    static List<(string Name, string Bundle, string Path)> RunningMessengers()
    {
        var found = new List<(string, string, string)>();
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            IntPtr apps = ObjC.Send(ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace")), ObjC.Sel("runningApplications"));
            nint count = ObjC.SendReturnLong(apps, ObjC.Sel("count"));
            for (nint i = 0; i < count; i++)
            {
                IntPtr app = ObjC.SendLong(apps, ObjC.Sel("objectAtIndex:"), i);
                string name = ObjC.ToText(ObjC.Send(app, ObjC.Sel("localizedName")));
                string bundle = ObjC.ToText(ObjC.Send(app, ObjC.Sel("bundleIdentifier")));
                if (bundle.Length == 0 || (Messengers.ForApp(bundle) ?? Messengers.ForApp(null, name)) == null) continue;
                string path = ObjC.ToText(ObjC.Send(ObjC.Send(app, ObjC.Sel("bundleURL")), ObjC.Sel("path")));
                // The bundle id names the messenger where the name alone might not ("Microsoft Outlook" → outlook).
                found.Add((name, Messengers.ForApp(bundle) != null ? bundle : name, path));
            }
        }
        finally { ObjC.objc_autoreleasePoolPop(pool); }
        return found;
    }

    /// <summary>The Dock badge's text ("3"), null for none.</summary>
    static string? BadgeOf(string bundle)
    {
        try
        {
            var start = new ProcessStartInfo("/usr/bin/lsappinfo") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string arg in new[] { "info", "-only", "StatusLabel", "-app", bundle }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            var match = Label.Match(output);
            return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    /// <summary>The app's icon as a 64 px PNG.</summary>
    static byte[]? IconPng(string path)
    {
        if (path.Length == 0) return null;
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            IntPtr icon = ObjC.Send(ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace")), ObjC.Sel("iconForFile:"), ObjC.String(path));
            if (icon == IntPtr.Zero) return null;
            IntPtr tiff = ObjC.Send(icon, ObjC.Sel("TIFFRepresentation"));
            IntPtr rep = ObjC.Send(ObjC.Send(ObjC.Class("NSBitmapImageRep"), ObjC.Sel("alloc")), ObjC.Sel("initWithData:"), tiff);
            if (rep == IntPtr.Zero) return null;
            IntPtr properties = ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary"));
            IntPtr png = SendPng(rep, ObjC.Sel("representationUsingType:properties:"), 4 /* NSBitmapImageFileTypePNG */, properties);
            byte[]? bytes = null;
            if (png != IntPtr.Zero)
            {
                nint length = ObjC.SendReturnLong(png, ObjC.Sel("length"));
                bytes = new byte[length];
                Marshal.Copy(ObjC.Send(png, ObjC.Sel("bytes")), bytes, 0, (int)length);
            }
            ObjC.Send(rep, ObjC.Sel("release"));
            return bytes;
        }
        catch { return null; }
        finally { ObjC.objc_autoreleasePoolPop(pool); }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr SendPng(IntPtr receiver, IntPtr selector, nuint type, IntPtr properties);
}
