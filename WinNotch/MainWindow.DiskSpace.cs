using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// A word when the system drive runs low (under 10 % or 10 GB), once per low spell. Windows has no event for free
/// space, so Holm looks when it starts, after sleep, a little after each finished download, and otherwise hourly:
/// one cheap call each time.
/// </summary>
public partial class MainWindow
{
    const string DriveGlyph = "\uEDA2";
    DispatcherTimer _diskHourly = null!, _diskSoon = null!;

    void InitDiskSpace()
    {
        _diskHourly = MakeTimer(60 * 60_000, CheckDiskSpace);
        _diskSoon = MakeTimer(10_000, CheckDiskSpace, oneShot: true); // after a download, or once a question is gone
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
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
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
        if (DiskSpace.IsLow(free, total) && !_settings.DiskWarned && (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen))
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
        if (warn) ShowDiskWarning(root.TrimEnd('\\'), DiskSpace.Describe(free, total));
    }

    void ShowDiskWarning(string drive, string detail)
    {
        Diag.Log("Disk space low");
        ShowTask(DriveGlyph, TimerOrange, $"Drive {drive} is almost full", detail, null,
                 ("Clean up", () => { HideTask(); TryOpen("ms-settings:storagesense"); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(20);
        _taskHide.Start();
    }

    void MenuDiskWarning_Click(object sender, RoutedEventArgs e)
    {
        _settings.DiskWarning = MenuDiskWarning.IsChecked;
        _settings.Save();
        StartDiskSpace();
    }
}
