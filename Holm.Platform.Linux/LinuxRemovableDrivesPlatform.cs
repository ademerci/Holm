using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// USB sticks and SD cards as the desktop mounts them: its automounter (UDisks) makes a folder per drive under
/// /run/media/&lt;user&gt; or /media/&lt;user&gt;, and the kernel tells about new and removed folders there (inotify), so
/// nothing is polled. Ejecting unmounts the drive and powers it off, as the file manager's Eject does.
/// </summary>
public sealed class LinuxRemovableDrivesPlatform : IRemovableDrivesPlatform
{
    readonly List<FileSystemWatcher> _watchers = new();

    public event Action<RemovableDrive>? Arrived;
    public event Action<string>? Removed;

    static IEnumerable<string> Roots()
    {
        string user = Environment.UserName;
        foreach (string root in new[] { Path.Combine("/run/media", user), Path.Combine("/media", user) })
            if (Directory.Exists(root)) yield return root;
    }

    public void Start()
    {
        if (_watchers.Count > 0) return;
        foreach (string root in Roots())
        {
            try
            {
                var watcher = new FileSystemWatcher(root) { NotifyFilter = NotifyFilters.DirectoryName, IncludeSubdirectories = false };
                // The folder comes a moment before the drive is mounted in it.
                watcher.Created += (_, e) => _ = ArrivedAsync(e.FullPath);
                watcher.Deleted += (_, e) => Removed?.Invoke(e.FullPath);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
    }

    async Task ArrivedAsync(string path)
    {
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            if (Read(path) is { } drive) { Arrived?.Invoke(drive); return; }
            if (!Directory.Exists(path)) return;
        }
    }

    public void Stop()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    public List<RemovableDrive> Present()
        => Roots().SelectMany(root => Directory.EnumerateDirectories(root)).Select(Read).OfType<RemovableDrive>().ToList();

    /// <summary>The drive mounted at that folder, once it is mounted.</summary>
    static RemovableDrive? Read(string path)
    {
        if (!IsMountPoint(path)) return null;
        try
        {
            var drive = new DriveInfo(path);
            return new RemovableDrive(path, Path.GetFileName(path), path, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    internal static bool IsMountPoint(string path, string mounts = "/proc/self/mounts")
    {
        try
        {
            foreach (string line in File.ReadLines(mounts))
            {
                var fields = line.Split(' ');
                // Spaces in a mount point are written as \040.
                if (fields.Length > 1 && fields[1].Replace("\\040", " ") == path) return true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    public string? Eject(string id) => EjectAsync(id).GetAwaiter().GetResult();

    static async Task<string?> EjectAsync(string id)
    {
        // gio does what the file manager's Eject does (unmount, then power off); udisksctl where there's no gio.
        if (await RunAsync("gio", ["mount", "-e", id]).ConfigureAwait(false) is null) return null;
        string? device = DeviceOf(id);
        if (device == null) return "It's no longer mounted";
        if (await RunAsync("udisksctl", ["unmount", "-b", device]).ConfigureAwait(false) is { } problem) return problem;
        if (await RunAsync("udisksctl", ["power-off", "-b", device]).ConfigureAwait(false) is { } powerProblem)
            return "The drive was unmounted. Power off failed. " + powerProblem;
        return null;
    }

    static string? DeviceOf(string path)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/self/mounts"))
            {
                var fields = line.Split(' ');
                if (fields.Length > 1 && fields[1].Replace("\\040", " ") == path) return fields[0];
            }
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>Null when it worked, else what the tool said (or that it's missing).</summary>
    internal static async Task<string?> RunAsync(string tool, string[] args, TimeSpan? timeLimit = null)
    {
        try
        {
            var start = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            if (process == null) return "Couldn't start " + tool;
            using var timeout = new CancellationTokenSource(timeLimit ?? TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            var stderr = process.StandardError.ReadToEndAsync(token);
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, token);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr).WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return tool + " timed out";
            }
            if (process.ExitCode == 0) return null;
            string error = await stderr.ConfigureAwait(false);
            string first = error.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            return first.Contains("busy", StringComparison.OrdinalIgnoreCase) ? "An app still uses it" : first.Length > 0 ? first : "It didn't let go";
        }
        catch (System.ComponentModel.Win32Exception) { return tool + " isn't installed"; }
    }
}
