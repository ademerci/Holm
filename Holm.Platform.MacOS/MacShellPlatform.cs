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

    /// <summary>System Settings › General › Storage.</summary>
    public void OpenStorage() => Run("x-apple.systempreferences:com.apple.settings.Storage");

    static void Run(params string[] args)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }
}
