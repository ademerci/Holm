using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WinNotch;

/// <summary>
/// A USB stick or SD card pops out of the notch with "Open" and "Eject", and stays in the open notch while it is
/// plugged in. Windows tells every window when a volume comes or goes (WM_DEVICECHANGE), so nothing is polled.
/// </summary>
public partial class MainWindow
{
    const string UsbGlyph = "";
    const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;

    List<UsbDrive> _usb = new();
    char? _usbShown, _ejecting;

    void StartUsb()
    {
        if (!_settings.UsbDrives) { _usb = new(); ShowUsbRows(); return; }
        // Already plugged in at start-up: in the open notch, without popping out.
        Task.Run(UsbDrives.Present).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && _settings.UsbDrives) { _usb = t.Result; ShowUsbRows(); }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>From the window procedure: a volume arrived or went. Only plain drives, not media changes or network drives.</summary>
    void OnVolumeChange(IntPtr wParam, IntPtr lParam)
    {
        int ev = wParam.ToInt32();
        if (!_settings.UsbDrives || ev is not (DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE) || lParam == IntPtr.Zero) return;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) return;
        uint mask = (uint)Marshal.ReadInt32(lParam, 12);
        short flags = Marshal.ReadInt16(lParam, 16);
        if ((flags & 0x3) != 0) return; // DBTF_MEDIA (a disc changed), DBTF_NET (a network drive)
        var letters = UsbDrives.Letters(mask);
        if (ev == DBT_DEVICEARRIVAL) _ = UsbArrivedAsync(letters);
        else Dispatcher.InvokeAsync(() => UsbRemoved(letters));
    }

    async Task UsbArrivedAsync(List<char> letters)
    {
        // The label can take a moment on a slow stick: not on the UI thread.
        var drives = await Task.Run(() => letters.Select(UsbDrives.Read).OfType<UsbDrive>().ToList());
        foreach (var drive in drives)
        {
            _usb.RemoveAll(d => d.Letter == drive.Letter);
            _usb.Add(drive);
            Diag.Log("USB drive plugged in");
            if (!_busy && !_taskVisible && !_sharing && !_installingUpdate && !_hiddenForFullscreen) ShowUsb(drive);
        }
        ShowUsbRows();
    }

    void UsbRemoved(List<char> letters)
    {
        _usb.RemoveAll(d => letters.Contains(d.Letter));
        // Pulled out while its pop-out was up: nothing left to open. (After "Eject", "Safe to remove" stays.)
        if (_usbShown is { } shown && letters.Contains(shown) && _ejecting != shown) HideTask();
        ShowUsbRows();
    }

    void ShowUsb(UsbDrive drive)
    {
        ShowTask(UsbGlyph, ShareBlue, drive.Name, drive.Detail, null,
                 ("Open", () => { HideTask(); TryOpen(drive.Root); }), ("Eject", () => _ = EjectAsync(drive)));
        _usbShown = drive.Letter;
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    async Task EjectAsync(UsbDrive drive)
    {
        _ejecting = drive.Letter;
        ShowTask(UsbGlyph, ShareBlue, "Ejecting " + drive.Name + "…", "", null, null, null);
        _usbShown = drive.Letter;
        string? problem;
        try { problem = await Task.Run(() => UsbDrives.Eject(drive.Letter)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            Diag.Log("Eject: " + ex.GetType().Name);
            problem = "Windows didn't let go of it";
        }
        if (problem == null) EndTask(UsbGlyph, GreenBrush, "Safe to remove", drive.Name);
        else EndTask(UsbGlyph, RedBrush, $"Can't eject {drive.Letter}:", problem);
        _usbShown = drive.Letter;
        _taskHide.Interval = TimeSpan.FromSeconds(problem == null ? 5 : 8);
        _taskHide.Start();
        _ejecting = null;
    }

    /// <summary>One row per plugged-in drive in the open notch: open it, or eject it.</summary>
    void ShowUsbRows()
    {
        UsbRows.Children.Clear();
        foreach (var drive in _usb)
        {
            var row = new Grid { Margin = new Thickness(2, 12, 2, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = UsbGlyph, FontFamily = IconFont, FontSize = 14, Foreground = ShareBlue, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = drive.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = drive.Detail, FontSize = 11, Foreground = DimBrush });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var open = new Button { Style = (Style)FindResource("PillButton"), Content = "Open", VerticalAlignment = VerticalAlignment.Center };
            open.Click += (_, _) => TryOpen(drive.Root);
            Grid.SetColumn(open, 2);
            row.Children.Add(open);
            var eject = new Button { Style = (Style)FindResource("PillButton"), Content = "Eject", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            eject.Click += (_, _) => _ = EjectAsync(drive);
            Grid.SetColumn(eject, 3);
            row.Children.Add(eject);
            UsbRows.Children.Add(row);
        }
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void MenuUsbDrives_Click(object sender, RoutedEventArgs e)
    {
        _settings.UsbDrives = MenuUsbDrives.IsChecked;
        _settings.Save();
        StartUsb();
    }
}
