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
    Windows.ApplicationModel.StartupTask? _startupTask;

    SettingsGroup[] SettingsGroups() => new[]
    {
        new SettingsGroup("General", new[] { MenuAnim, MenuMini, MenuAutostart, MenuHideOverBrowsers }),
        new SettingsGroup("Pop-outs", new[] { MenuDownloads, MenuScreenshots, MenuScreenshotText, MenuCapsLock, MenuDiskWarning, MenuUsbDrives, MenuConnectivity }),
        new SettingsGroup("Open notch", new[] { MenuMediaControls, MenuNotes, MenuReminders, MenuCalculator, MenuClipboardHistory, MenuAppVolume, MenuColorPicker, MenuFocumon }),
        new SettingsGroup("Weather", new[] { MenuWeatherAutoLocation }),
        new SettingsGroup("Calls and privacy", new[] { MenuCalls, MenuPauseForCalls, MenuPrivacy, MenuVpn, MenuClearPasswords }),
        new SettingsGroup("Beam", new[] { MenuBeam, MenuSendFolders, MenuSendLater }),
    };

    /// <summary>Checks the switches as the settings stand.</summary>
    void SyncSwitches()
    {
        MenuAnim.IsChecked = _settings.Animations;
        MenuMini.IsChecked = _settings.MiniIdle;
        MenuHideOverBrowsers.IsChecked = _settings.HideOverBrowsers;
        MenuMediaControls.IsChecked = _settings.MediaControls;
        MenuWeatherAutoLocation.IsChecked = _settings.WeatherAutoLocation;
        MenuAutostart.IsEnabled = !PackageIdentity.IsPackaged || _startupTask != null;
        MenuAutostart.ToolTip = PackageIdentity.IsPackaged && _startupTask == null ? "Windows startup is unavailable." : null;
        try { MenuAutostart.IsChecked = PackageIdentity.IsPackaged ? _startupTask?.State == Windows.ApplicationModel.StartupTaskState.Enabled : Autostart.IsEnabled; } catch { MenuAutostart.IsChecked = false; }
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
        MenuFocumon.IsChecked = _settings.Focumon;
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

    async void ShowSettings()
    {
        SetExpanded(false);
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        if (PackageIdentity.IsPackaged)
        {
            try { _startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("HolmStartup"); }
            catch (Exception ex) { Diag.Error(ex); }
        }
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = MakeSettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <param name="demo">For snapshots: a made-up profile in place of yours, and Apply saves nothing.</param>
    SettingsWindow MakeSettingsWindow(ProfileDraft? demo = null)
    {
        var version = RunningVersion;
        var profile = demo != null
            ? new ProfileEditor("Alex", () => demo, _ => { }, SettingsWindow.ActionButtonStyle)
            : new ProfileEditor(Native.DisplayName(), CurrentProfile, ApplyProfile, SettingsWindow.ActionButtonStyle);
        return new SettingsWindow(SettingsGroups(), $"{version.Major}.{version.Minor}.{version.Build}", _settings.Animations, SyncSwitches, profile);
    }
}
