using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Holm.Platform;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// The XDG Downloads folder (user-dirs.dirs, which every desktop keeps), xdg-open for opening, and the file
/// manager's own D-Bus interface (org.freedesktop.FileManager1: Nautilus, Dolphin, Nemo, Thunar, ...) to show a
/// file selected; where no file manager offers that, its folder opens.
/// </summary>
public sealed class LinuxShellPlatform : IShellPlatform
{
    public string DownloadsFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        try
        {
            string file = Path.Combine(config, "user-dirs.dirs");
            if (File.Exists(file) && DownloadDir(File.ReadAllLines(file), home) is { } dir) return dir;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Path.Combine(home, "Downloads");
    }

    /// <summary>XDG_DOWNLOAD_DIR="$HOME/Downloads" (the only variable the format allows is $HOME).</summary>
    internal static string? DownloadDir(IEnumerable<string> lines, string home) => UserDir(lines, home, "XDG_DOWNLOAD_DIR");

    internal static string? UserDir(IEnumerable<string> lines, string home, string name)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (!line.StartsWith(name + "=", StringComparison.Ordinal)) continue;
            string value = line[(name.Length + 1)..].Trim().Trim('"');
            if (value.StartsWith("$HOME", StringComparison.Ordinal)) value = home + value["$HOME".Length..];
            return value.StartsWith('/') ? value.TrimEnd('/') : null;
        }
        return null;
    }

    public void Open(string path) => XdgOpen(path);

    /// <summary>
    /// GNOME, KDE's Spectacle and most tools save into Pictures/Screenshots (the XDG pictures folder, in the user's
    /// language); without that folder, Pictures itself.
    /// </summary>
    public string? ScreenshotsFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        string pictures = Path.Combine(home, "Pictures");
        try
        {
            string file = Path.Combine(config, "user-dirs.dirs");
            if (File.Exists(file) && UserDir(File.ReadAllLines(file), home, "XDG_PICTURES_DIR") is { } dir) pictures = dir;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        string screenshots = Path.Combine(pictures, "Screenshots");
        return Directory.Exists(screenshots) ? screenshots : Directory.Exists(pictures) ? pictures : null;
    }

    public bool IsScreenshot(string path) => true;

    public void Reveal(string path)
    {
        string uri = new Uri(path).AbsoluteUri;
        _ = Bus.Session.CallAsync("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1",
                "ShowItems", "ass", w => { w.Strings(new[] { uri }); w.String(""); })
            .ContinueWith(t => { if (t.IsFaulted || t.Result == false) XdgOpen(Path.GetDirectoryName(path) ?? path); });
    }

    public string SystemDrive() => "/";

    /// <summary>The desktop's disk usage tool where there is one (GNOME's Baobab, KDE's Filelight), else the home folder.</summary>
    public void OpenStorage()
    {
        foreach (string tool in new[] { "baobab", "filelight", "qdirstat" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(tool) { UseShellExecute = false })?.Dispose();
                return;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        XdgOpen(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    static void XdgOpen(string path)
    {
        try
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
