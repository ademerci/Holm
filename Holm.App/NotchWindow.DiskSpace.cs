using System;
using System.IO;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A word when the system drive runs low (under 10 % or 10 GB), once per low spell (the WPF app's
/// MainWindow.DiskSpace.cs, with its DiskSpace rules linked). No system says when free space changes, so Holm looks
/// a little after start, a little after each finished download, and otherwise hourly: one cheap call each time.
/// </summary>
public sealed partial class NotchWindow
{
    const string DriveGlyph = "";
    readonly DispatcherTimer _diskHourly = new() { Interval = TimeSpan.FromHours(1) };
    readonly DispatcherTimer _diskSoon = new() { Interval = TimeSpan.FromSeconds(10) };

    void InitDiskSpace()
    {
        _diskHourly.Tick += (_, _) => CheckDiskSpace();
        _diskSoon.Tick += (_, _) => { _diskSoon.Stop(); CheckDiskSpace(); };
    }

    void StartDiskSpace()
    {
        _diskHourly.Stop();
        _diskSoon.Stop();
        if (!_settings.DiskWarning) return;
        _diskHourly.Start();
        _diskSoon.Start(); // not right at start-up, while everything else reports in
    }

    void CheckDiskSoon()
    {
        if (!_settings.DiskWarning) return;
        _diskSoon.Stop();
        _diskSoon.Start();
    }

    void CheckDiskSpace()
    {
        if (!_settings.DiskWarning) return;
        string root = _shell.SystemDrive();
        long free, total;
        try
        {
            var drive = new DriveInfo(root);
            (free, total) = (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Diag.Log("Disk space: " + ex.Message);
            return;
        }
        // Something else is on show: ask again shortly rather than push it away.
        if (DiskSpace.IsLow(free, total) && !_settings.DiskWarned && _taskVisible)
        {
            CheckDiskSoon();
            return;
        }
        bool warned = _settings.DiskWarned;
        bool warn = DiskSpace.ShouldWarn(free, total, ref warned);
        if (warned != _settings.DiskWarned)
        {
            _settings.DiskWarned = warned;
            _settings.Save();
        }
        if (warn) ShowDiskWarning(DiskSpace.Describe(free, total));
    }

    void ShowDiskWarning(string detail)
    {
        Diag.Log("Disk space low");
        // The same words on every system: a drive letter only means something on Windows.
        ShowTask(DriveGlyph, TimerOrange, "Your drive is almost full", detail, null,
                 ("Clean up", () => { HideTask(); Try(_shell.OpenStorage); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(20);
        _taskHide.Start();
    }

    void SetDiskWarning(bool on)
    {
        _settings.DiskWarning = on;
        _settings.Save();
        StartDiskSpace();
    }
}
