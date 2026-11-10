using System;
using System.Windows;
using System.Windows.Media;
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
        new SettingsGroup("general", "General", "", Color.FromRgb(0x63, 0x63, 0x66), "How the notch looks and starts", new SettingsSwitch[]
        {
            new(MenuAnim, "", "Smooth transitions and the moving equalizer."),
            new(MenuReactiveEq, "\uE8D6", "The equalizer bars move with the music that plays. Only the loudness is read, nothing is recorded."),
            new(MenuMini, "", "The notch shrinks to a small pill while nothing is going on."),
            new(MenuAutostart, "", "Holm starts when you sign in to Windows."),
            new(MenuHideOverBrowsers, ""),
        }),
        new SettingsGroup("notch", "Open notch", "", Color.FromRgb(0x5E, 0x5C, 0xE6), "What the open notch shows and the tools in its bar", new SettingsSwitch[]
        {
            new(MenuMediaControls, ""),
            new(MenuNotes, "", "A small note in the open notch, kept on this PC."),
            new(MenuReminders, "", "Type “20 min laundry” and the notch rings on time."),
            new(MenuCalculator, "", "Type a sum; the result is ready to copy."),
            new(MenuClipboardHistory, "", "Your last 20 copied texts and pictures, in memory only."),
            new(MenuAppVolume, "", "A volume slider for every app that plays sound."),
            new(MenuColorPicker, "", "Pick any pixel's color and copy its hex code."),
            new(MenuFocumon, ""),
        }),
        new SettingsGroup("popouts", "Pop-outs", "", Color.FromRgb(0xFF, 0x9F, 0x0A), "What pops out of the notch when it happens", new SettingsSwitch[]
        {
            new(MenuDownloads, "", "A finished download pops out with Open and Show."),
            new(MenuScreenshots, "", "A new screenshot pops out with Send and Shelf."),
            new(MenuScreenshotText, "", "Read the text in a screenshot with Windows' own OCR."),
            new(MenuCapsLock, "", "The notch says when Caps Lock goes on or off."),
            new(MenuDiskWarning, "", "One warning when the system drive runs low."),
            new(MenuUsbDrives, "", "USB sticks pop out with Open and Eject."),
            new(MenuConnectivity, "", "Says when the internet drops and when it's back."),
        }),
        new SettingsGroup("privacy", "Calls and privacy", "", Color.FromRgb(0x34, 0xC7, 0x59), "Calls, the microphone and camera, and your clipboard", new SettingsSwitch[]
        {
            new(MenuCalls, "", "Shows a call with its time and a mute button."),
            new(MenuPauseForCalls, "", "Music pauses when a call starts and plays on after."),
            new(MenuPrivacy, "", "A dot beside the notch while an app uses the microphone or camera."),
            new(MenuVpn, "", "A small lock while a VPN is connected."),
            new(MenuClearPasswords, "", "Copied passwords leave the clipboard after 30 seconds."),
        }),
        new SettingsGroup("beam", "Beam", "", Color.FromRgb(0x0A, 0x84, 0xFF), "Sending files to friends, people nearby and your phone", new SettingsSwitch[]
        {
            new(MenuBeam, "", "Drop files on the notch to send them. Also needed for friends and chat."),
            new(MenuSendFolders, "", "Folders are packed as a ZIP before they're sent."),
            new(MenuSendLater, "", "Files for an offline friend wait and go once they're back."),
        }),
        new SettingsGroup("friends", "Friends", "", Color.FromRgb(0xFF, 0x37, 0x5F), "Chat and what your friends see", new SettingsSwitch[]
        {
            new(MenuShareActivity, "", "Friends see what you're doing: the song, a call, the game you play, a timer. Only friends, sealed like every note."),
            new(MenuChatPopups, "", "A friend's message pops out of the notch with Reply. It's in Chat either way."),
            new(MenuAchievementPopups, "", "A trophy pops out of the notch when you unlock an achievement."),
        }),
        new SettingsGroup("weather", "Weather", "", Color.FromRgb(0x32, 0xAD, 0xE6), "The weather in the notch", new SettingsSwitch[]
        {
            new(MenuWeatherAutoLocation, ""),
        }),
    };

    /// <summary>Checks the switches as the settings stand.</summary>
    void SyncSwitches()
    {
        MenuAnim.IsChecked = _settings.Animations;
        MenuReactiveEq.IsChecked = _settings.ReactiveEqualizer;
        MenuMini.IsChecked = _settings.MiniIdle;
        MenuHideOverBrowsers.IsChecked = _settings.HideOverBrowsers;
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
        MenuFocumon.IsChecked = _settings.Focumon;
        MenuScreenshotText.IsChecked = _settings.ScreenshotText;
        MenuCapsLock.IsChecked = _settings.CapsLock;
        MenuDiskWarning.IsChecked = _settings.DiskWarning;
        MenuUsbDrives.IsChecked = _settings.UsbDrives;
        MenuConnectivity.IsChecked = _settings.Connectivity;
        MenuScreenshots.IsChecked = _settings.Screenshots;
        MenuBeam.IsChecked = _settings.Beam;
        MenuChatPopups.IsChecked = _settings.ChatPopups;
        MenuShareActivity.IsChecked = _settings.ShareActivity;
        MenuAchievementPopups.IsChecked = _settings.AchievementPopups;
    }

    // After the menu has closed: a window opened from inside the click would lose the focus to the closing menu.
    void MenuSettings_Click(object sender, RoutedEventArgs e) => Dispatcher.InvokeAsync(() => ShowHub("general"), DispatcherPriority.Background);

    void MenuProfile_Click(object sender, RoutedEventArgs e) => Dispatcher.InvokeAsync(() => ShowHub("profile"), DispatcherPriority.Background);

    /// <summary>Opens the Holm window (or brings it forward) at <paramref name="page"/>.</summary>
    void ShowHub(string? page = null)
    {
        SetExpanded(false);
        if (_settingsWindow == null)
        {
            _settingsWindow = MakeSettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (page != null) _settingsWindow.Navigate(page);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <param name="demo">For snapshots: a made-up profile in place of yours, and Apply saves nothing.</param>
    SettingsWindow MakeSettingsWindow(ProfileDraft? demo = null)
    {
        var version = RunningVersion;
        var profile = demo != null
            ? new ProfileEditor("Alex", () => demo, _ => { }, HubTheme.ActionButtonStyle)
            : new ProfileEditor(Native.DisplayName(), CurrentProfile, ApplyProfile, HubTheme.ActionButtonStyle);
        Func<ProfileDraft> current = demo != null ? () => demo : CurrentProfile;
        return new SettingsWindow(SettingsGroups(), $"{version.Major}.{version.Minor}.{version.Build}", _settings.Animations, SyncSwitches, profile,
                                  HubPages(demo != null), () => MeCard(current()));
    }
}
