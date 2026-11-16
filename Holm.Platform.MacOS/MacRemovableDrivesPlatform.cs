using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// USB sticks and SD cards as macOS mounts them, in /Volumes; macOS tells about new and removed volumes there
/// (FSEvents), nothing is polled. Only external, physical drives count: not the startup disk, not disk images.
/// Ejecting goes through diskutil, as Finder's Eject does.
/// </summary>
public sealed class MacRemovableDrivesPlatform : IRemovableDrivesPlatform
{
    const string Volumes = "/Volumes";
    FileSystemWatcher? _watcher;

    public event Action<RemovableDrive>? Arrived;
    public event Action<string>? Removed;

    public void Start()
    {
        if (_watcher != null || !Directory.Exists(Volumes)) return;
        try
        {
            _watcher = new FileSystemWatcher(Volumes) { NotifyFilter = NotifyFilters.DirectoryName, IncludeSubdirectories = false };
            _watcher.Created += (_, e) => _ = ArrivedAsync(e.FullPath);
            _watcher.Deleted += (_, e) => Removed?.Invoke(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { _watcher = null; }
    }

    async Task ArrivedAsync(string path)
    {
        await Task.Delay(500).ConfigureAwait(false); // let the mount settle
        if (Read(path) is { } drive) Arrived?.Invoke(drive);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public List<RemovableDrive> Present()
        => Directory.Exists(Volumes) ? Directory.EnumerateDirectories(Volumes).Select(Read).OfType<RemovableDrive>().ToList() : new();

    static RemovableDrive? Read(string path)
    {
        if (!IsExternal(Info(path))) return null;
        try
        {
            var drive = new DriveInfo(path);
            return new RemovableDrive(path, Path.GetFileName(path), path, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>What diskutil says about a volume, as its "Key: Value" lines.</summary>
    static string Info(string path) => Run("info", path).Output;

    /// <summary>Not internal, and a real device (a disk image says "Disk Image" as its protocol).</summary>
    internal static bool IsExternal(string info)
    {
        string? Value(string key) => info.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(key + ":", StringComparison.Ordinal))
            .Select(l => l[(key.Length + 1)..].Trim()).FirstOrDefault();
        if (Value("Protocol") is "Disk Image") return false;
        string? location = Value("Device Location");
        if (location != null) return location == "External";
        return Value("Internal") == "No";
    }

    public string? Eject(string id)
    {
        var (ok, output) = Run("eject", id);
        if (ok) return null;
        return output.Contains("in use", StringComparison.OrdinalIgnoreCase) || output.Contains("busy", StringComparison.OrdinalIgnoreCase)
            ? "An app still uses it" : "macOS didn't let go of it";
    }

    static (bool Ok, string Output) Run(params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("/usr/sbin/diskutil") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            if (process == null) return (false, "");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(20_000);
            return (process.ExitCode == 0, output);
        }
        catch { return (false, ""); }
    }
}
