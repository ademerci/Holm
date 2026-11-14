using System;
using System.Runtime.InteropServices;

namespace WinNotch;

/// <summary>
/// Windows' own pop-up for volume, brightness and media keys, kept out of sight so the notch is the only one that shows
/// (Settings › Pop-outs). There is no API for it: like the long-standing HideVolumeOSD tools, Holm minimizes the window
/// that hosts it, and restores it when the switch goes off or Holm quits. That host is found by its window classes
/// (Windows 10: NativeHWNDHost › DirectUIHWND), never by its title, which is translated.
/// </summary>
/// <remarks>
/// On Windows 11 the pop-up lives in a XAML host that another process can't tell apart from Alt+Tab's (same classes, only
/// a translated title differs), and on 25H2 it isn't among the windows another process can see at all; hiding it there
/// would take code running inside Explorer, which Holm doesn't do. <see cref="Hide"/> is then false and nothing changes.
/// </remarks>
internal static class NativeOsd
{
    static IntPtr _hidden;

    /// <summary>Finds and minimizes the pop-up's host; false when this Windows has none Holm can reach.</summary>
    public static bool Hide()
    {
        IntPtr host = Find();
        if (host == IntPtr.Zero) return false;
        // Minimized, it can't show; Windows keeps it so until it is restored, even across pop-ups.
        if (!IsIconic(host)) ShowWindow(host, SW_MINIMIZE);
        _hidden = host;
        return true;
    }

    /// <summary>Gives Windows its pop-up back (the switch went off, or Holm quits).</summary>
    public static void Restore()
    {
        if (_hidden == IntPtr.Zero) return;
        if (IsWindow(_hidden)) ShowWindow(_hidden, SW_RESTORE);
        _hidden = IntPtr.Zero;
    }

    /// <summary>The host window, or zero: the first NativeHWNDHost with a DirectUIHWND inside.</summary>
    static IntPtr Find()
    {
        IntPtr host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, "NativeHWNDHost", null)) != IntPtr.Zero)
            if (FindWindowEx(host, IntPtr.Zero, "DirectUIHWND", null) != IntPtr.Zero) return host;
        return IntPtr.Zero;
    }

    const int SW_MINIMIZE = 6, SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
}
