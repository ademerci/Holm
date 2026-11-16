using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>Downloads through the known folder, Explorer for opening and showing (as the WPF app's Shell).</summary>
public sealed class WindowsShellPlatform : IShellPlatform
{
    public string DownloadsFolder() => Native.DownloadsFolder();

    public void Open(string path)
    {
        // The click went to us, so we may hand the foreground to whatever opens; otherwise Windows would only flash it.
        AllowSetForegroundWindow(-1);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    public bool OpenApp(string appId)
    {
        try
        {
            AllowSetForegroundWindow(-1);
            Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + appId) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    public void Reveal(string path)
    {
        AllowSetForegroundWindow(-1);
        // Explorer wants exactly /select,"path" (quotes around the path only).
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""))?.Dispose();
    }

    public string SystemDrive() => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    public void OpenStorage() => Open("ms-settings:storagesense");

    /// <summary>Win+Print Screen and the Snipping Tool save there (the WPF app's folder).</summary>
    public string? ScreenshotsFolder() => Native.ScreenshotsFolder();

    public bool IsScreenshot(string path) => true;

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
}
