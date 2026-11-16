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
    internal static string? DownloadDir(IEnumerable<string> lines, string home)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (!line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal)) continue;
            string value = line["XDG_DOWNLOAD_DIR=".Length..].Trim().Trim('"');
            if (value.StartsWith("$HOME", StringComparison.Ordinal)) value = home + value["$HOME".Length..];
            return value.StartsWith('/') ? value.TrimEnd('/') : null;
        }
        return null;
    }

    public void Open(string path) => XdgOpen(path);

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
