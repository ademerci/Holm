using System;
using System.Diagnostics;
using System.IO;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>~/Downloads, and macOS's open command: the file's app, or Finder with the file selected.</summary>
public sealed class MacShellPlatform : IShellPlatform
{
    public string DownloadsFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public void Open(string path) => Run(path);

    public void Reveal(string path) => Run("-R", path);

    public string SystemDrive() => "/";

    /// <summary>Where screenshots go: the place set in the Screenshot app (com.apple.screencapture), else the Desktop.</summary>
    public string? ScreenshotsFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var start = new ProcessStartInfo("/usr/bin/defaults") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "read", "com.apple.screencapture", "location" }) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            string? location = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(3000);
            if (!string.IsNullOrEmpty(location))
            {
                if (location.StartsWith('~')) location = home + location[1..];
                if (Directory.Exists(location)) return location;
            }
        }
        catch { }
        string desktop = Path.Combine(home, "Desktop");
        return Directory.Exists(desktop) ? desktop : null;
    }

    /// <summary>macOS marks the pictures it takes (kMDItemIsScreenCapture), so a picture saved to the Desktop otherwise isn't one.</summary>
    public bool IsScreenshot(string path)
    {
        try { return getxattr(path, "com.apple.metadata:kMDItemIsScreenCapture", IntPtr.Zero, 0, 0, 0) > 0; }
        catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    static extern nint getxattr(string path, string name, IntPtr value, nuint size, uint position, int options);

    /// <summary>System Settings › General › Storage.</summary>
    public void OpenStorage() => Run("x-apple.systempreferences:com.apple.settings.Storage");

    static void Run(params string[] args)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }
}
