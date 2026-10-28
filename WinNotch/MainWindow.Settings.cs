using System;
using System.Windows;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Settings… in the right-click menu opens the on/off switches in a window of their own; the menu keeps what you
/// use (timer, Beam, friends) and the settings that need a submenu.
/// </summary>
public partial class MainWindow
{
    SettingsWindow? _settingsWindow;

    SettingsGroup[] SettingsGroups() => new[]
    {
        new SettingsGroup("General", new[] { MenuAnim, MenuMini, MenuAutostart }),
        new SettingsGroup("Pop-outs", new[] { MenuDownloads, MenuScreenshots, MenuScreenshotText, MenuCapsLock, MenuDiskWarning, MenuUsbDrives, MenuConnectivity }),
        new SettingsGroup("Open notch", new[] { MenuMediaControls, MenuNotes, MenuReminders, MenuCalculator, MenuClipboardHistory, MenuAppVolume, MenuColorPicker }),
        new SettingsGroup("Weather", new[] { MenuWeatherAutoLocation }),
        new SettingsGroup("Calls and privacy", new[] { MenuCalls, MenuPauseForCalls, MenuPrivacy, MenuVpn, MenuClearPasswords }),
        new SettingsGroup("Beam", new[] { MenuBeam, MenuSendFolders, MenuSendLater }),
    };

    /// <summary>Checks the switches as the settings stand.</summary>
    void SyncSwitches()
    {
        MenuAnim.IsChecked = _settings.Animations;
        MenuMini.IsChecked = _settings.MiniIdle;
        MenuMediaControls.IsChecked = _settings.MediaControls;
        MenuWeatherAutoLocation.IsChecked = _settings.WeatherAutoLocation;
        try { MenuAutostart.IsChecked = Autostart.IsEnabled; } catch { MenuAutostart.IsChecked = false; }
        MenuDownloads.IsChecked = _settings.Downloads;
        MenuVpn.IsChecked = _settings.Vpn;
        MenuPrivacy.IsChecked = _settings.Privacy;
        MenuCalls.IsChecked = _settings.Calls;
        MenuPauseForCalls.IsChecked = _settings.PauseForCalls;
        MenuNotes.IsChecked = _settings.Notes;
        MenuReminders.IsChecked = _settings.Reminders;
        MenuCalculator.IsChecked = _settings.Calculator;
        MenuClipboardHistory.IsChecked = _settings.ClipboardHistory;
        MenuAppVolume.IsChecked = _settings.AppVolume;
        MenuSendFolders.IsChecked = _settings.SendFolders;
        MenuSendLater.IsChecked = _settings.SendLater;
        MenuClearPasswords.IsChecked = _settings.ClearPasswords;
        MenuColorPicker.IsChecked = _settings.ColorPicker;
        MenuScreenshotText.IsChecked = _settings.ScreenshotText;
        MenuCapsLock.IsChecked = _settings.CapsLock;
        MenuDiskWarning.IsChecked = _settings.DiskWarning;
        MenuUsbDrives.IsChecked = _settings.UsbDrives;
        MenuConnectivity.IsChecked = _settings.Connectivity;
        MenuScreenshots.IsChecked = _settings.Screenshots;
        MenuBeam.IsChecked = _settings.Beam;
    }

    // After the menu has closed: a window opened from inside the click would lose the focus to the closing menu.
    void MenuSettings_Click(object sender, RoutedEventArgs e) => Dispatcher.InvokeAsync(ShowSettings, DispatcherPriority.Background);

    void ShowSettings()
    {
        SetExpanded(false);
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = MakeSettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    SettingsWindow MakeSettingsWindow()
    {
        var version = RunningVersion;
        return new SettingsWindow(SettingsGroups(), $"{version.Major}.{version.Minor}.{version.Build}", _settings.Animations, SyncSwitches);
    }
}
