using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A USB stick or SD card pops out of the notch with "Open" and "Eject", and stays in the open notch while it is
/// plugged in (the WPF app's MainWindow.Usb.cs). The system tells when a drive comes or goes; nothing is polled.
/// </summary>
public sealed partial class NotchWindow
{
    const string UsbGlyph = "";

    readonly StackPanel _usbRows = new();
    readonly List<RemovableDrive> _usb = new();
    IRemovableDrivesPlatform _drives = null!;
    string? _usbShown, _ejecting;

    void InitUsb(IPlatformServices platform)
    {
        _drives = platform.Drives;
        _drives.Arrived += drive => Dispatcher.UIThread.Post(() => UsbArrived(drive));
        _drives.Removed += id => Dispatcher.UIThread.Post(() => UsbRemoved(id));
        // _usbRows joins the activity section of home in the constructor.
    }

    async void StartUsb()
    {
        _drives.Stop();
        _usb.Clear();
        ShowUsbRows();
        if (!_settings.UsbDrives) return;
        _drives.Start();
        // Already plugged in at start-up: in the open notch, without popping out.
        var present = await Task.Run(_drives.Present);
        if (!_settings.UsbDrives) return;
        foreach (var drive in present) { _usb.RemoveAll(d => d.Id == drive.Id); _usb.Add(drive); }
        ShowUsbRows();
    }

    void UsbArrived(RemovableDrive drive)
    {
        if (!_settings.UsbDrives) return;
        _usb.RemoveAll(d => d.Id == drive.Id);
        _usb.Add(drive);
        Diag.Log("USB drive plugged in");
        if (!_taskVisible && !_sharing) ShowUsb(drive);
        ShowUsbRows();
    }

    void UsbRemoved(string id)
    {
        _usb.RemoveAll(d => d.Id == id);
        // Pulled out while its pop-out was up: nothing left to open. (After "Eject", "Safe to remove" stays.)
        if (_usbShown == id && _ejecting != id) HideTask();
        ShowUsbRows();
    }

    void ShowUsb(RemovableDrive drive)
    {
        ShowTask(UsbGlyph, ShareBlue, drive.Name, UsbDrives.Describe(drive.Total, drive.Free), null,
                 ("Open", () => { HideTask(); Try(() => _shell.Open(drive.Root)); }), ("Eject", () => _ = EjectAsync(drive)));
        _usbShown = drive.Id;
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    async Task EjectAsync(RemovableDrive drive)
    {
        _ejecting = drive.Id;
        ShowTask(UsbGlyph, ShareBlue, "Ejecting " + drive.Name + "…", "", null, null, null);
        _usbShown = drive.Id;
        string? problem;
        try { problem = await Task.Run(() => _drives.Eject(drive.Id)); }
        catch (Exception ex)
        {
            Diag.Log("Eject: " + ex.GetType().Name);
            problem = "It didn't let go";
        }
        if (problem == null) ShowTask(UsbGlyph, Green, "Safe to remove", drive.Name, null, null, null);
        else ShowTask(UsbGlyph, Red, "Can't eject " + drive.Name, problem, null, null, null);
        _usbShown = drive.Id;
        _taskHide.Interval = TimeSpan.FromSeconds(problem == null ? 5 : 8);
        _taskHide.Start();
        _ejecting = null;
    }

    /// <summary>One row per plugged-in drive in the open notch: open it, or eject it.</summary>
    void ShowUsbRows()
    {
        _usbRows.Children.Clear();
        foreach (var drive in _usb)
        {
            var row = new Grid { Margin = new Thickness(2, 12, 2, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
            row.Children.Add(new TextBlock { Text = UsbGlyph, FontFamily = IconFont, FontSize = 14, Foreground = ShareBlue, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = drive.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = UsbDrives.Describe(drive.Total, drive.Free), FontSize = 11, Foreground = Dim });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var open = NotchButton.Pill();
            open.Label = "Open";
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Click += () => Try(() => _shell.Open(drive.Root));
            Grid.SetColumn(open, 2);
            row.Children.Add(open);
            var eject = NotchButton.Pill();
            eject.Label = "Eject";
            eject.Margin = new Thickness(6, 0, 0, 0);
            eject.VerticalAlignment = VerticalAlignment.Center;
            eject.Click += () => _ = EjectAsync(drive);
            Grid.SetColumn(eject, 3);
            row.Children.Add(eject);
            _usbRows.Children.Add(row);
        }
        if (_state == NotchState.Expanded) ApplyState(animate: true);
    }

    void SetUsbDrives(bool on)
    {
        _settings.UsbDrives = on;
        _settings.Save();
        StartUsb();
    }
}
