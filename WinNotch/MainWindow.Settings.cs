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
    Windows.ApplicationModel.StartupTask? _startupTask;

    SettingsGroup[] SettingsGroups() => new[]
    {
        new SettingsGroup("general", "General", "", Color.FromRgb(0x63, 0x63, 0x66), "How the notch looks and starts", new SettingsSwitch[]
        {
            new(MenuAnim, "", "Smooth transitions, the moving equalizer, GIFs and the animated sky."),
            new(MenuBounce, "\uE945", "The notch springs open with a little bounce. Off, it glides in without overshooting. How much it bounces: right-click the notch \u203A Customize \u203A Bounce."),
            new(MenuReactiveEq, "\uE8D6", "The equalizer bars move with the music that plays. Only the loudness is read, nothing is recorded."),
            new(MenuDraggable, "\uE7C2", "Drag the notch by its top edge to move it left or right. Its position is remembered, even with dragging off.", "Reset Position", ResetNotchPosition),
            new(MenuMini, "", "The notch shrinks to a small pill while nothing is going on."),
            new(MenuAiUsage, "", "A small bubble beside the notch fills with how much of Claude Code's and Codex's limits you have used; hover it to see what their agents are working on. Read from their logs on this PC, never shared."),
            new(MenuAutostart, "", "Holm starts when you sign in to Windows."),
            new(MenuHotKey, "", "Press Ctrl+H anywhere to open the notch, and again to close it. Other apps don't get Ctrl+H while this is on."),
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
            new(MenuAllNotifications, "", "Every app's notification pops out at the top in the notch, not only messages. Turn on Do not disturb in Windows to keep its own banners away; they still reach Holm."),
            new(MenuNotchOnlyPopups, "", "Holm takes the volume and media keys and changes the volume or track itself, so Windows' own pop-up doesn't show and the notch does. Brightness keys stay with Windows."),
            new(MenuDiskWarning, "", "One warning when the system drive runs low."),
            new(MenuUsbDrives, "", "USB sticks pop out with Open and Eject."),
            new(MenuConnectivity, "", "Says when the internet drops and when it's back."),
        }),
        new SettingsGroup("privacy", "Calls and privacy", "", Color.FromRgb(0x34, 0xC7, 0x59), "Calls, the microphone and camera, and your clipboard", new SettingsSwitch[]
        {
            new(MenuCalls, "", "Shows a call with its time and a mute button."),
            new(MenuPauseForCalls, "", "Music pauses when a call starts and plays on after."),
            new(MenuPrivacy, "", "A dot beside the notch while an app uses the microphone or camera."),
            new(MenuDictation, "", "Dictate with Handy, free and offline: the notch shows when it listens, and Handy's own overlay stays out of sight. Holm finds Handy or offers to install it."),
            new(MenuVpn, "", "A small lock while a VPN is connected."),
            new(MenuClearPasswords, "", "Copied passwords leave the clipboard after 30 seconds. Copied texts stay in memory; copied pictures wait in your temp folder until the list is cleared or Holm restarts. Passwords marked private by password managers are never read."),
        }),
        new SettingsGroup("beam", "Beam", "", Color.FromRgb(0x0A, 0x84, 0xFF), "Sending files to friends, people nearby and your phone", new SettingsSwitch[]
        {
            new(MenuBeam, "", "Drop files on the notch to send them. Also needed for friends and chat. Holm looks at copied text for friend and file codes; nothing of it is kept or sent."),
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
            new(MenuWeatherAutoLocation, "", "Uses Windows location once if no city is set. Coordinates are rounded and sent only to Open-Meteo for the forecast; no IP lookup."),
            new(MenuSkyColors, "", "With nothing playing, the open notch glows in the colours of the time of day and the weather. Off, it stays plain black."),
        }),
    };

    /// <summary>Checks the switches as the settings stand.</summary>
    void SyncSwitches()
    {
        MenuAnim.IsChecked = _settings.Animations;
        MenuBounce.IsChecked = _settings.Bounce;
        MenuReactiveEq.IsChecked = _settings.ReactiveEqualizer;
        MenuMini.IsChecked = _settings.MiniIdle;
        MenuDraggable.IsChecked = _settings.Draggable;
        MenuHotKey.IsChecked = _settings.HotKey;
        MenuHideOverBrowsers.IsChecked = _settings.HideOverBrowsers;
        MenuMediaControls.IsChecked = _settings.MediaControls;
        MenuWeatherAutoLocation.IsChecked = _settings.WeatherAutoLocation;
        MenuSkyColors.IsChecked = _settings.SkyColors;
        MenuAiUsage.IsChecked = _settings.AiUsage;
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
        MenuAllNotifications.IsChecked = _settings.AllNotifications;
        MenuNotchOnlyPopups.IsChecked = _settings.NotchOnlyPopups;
        MenuDictation.IsChecked = _settings.Dictation;
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

    void MenuNetwork_Click(object sender, RoutedEventArgs e) => Dispatcher.InvokeAsync(() => ShowHub(NetworkKey), DispatcherPriority.Background);

    const string NetworkKey = "network";

    /// <summary>Wi-Fi in the Holm window: the speed test and the connection's details.</summary>
    static HubPage NetworkHubPage(bool demo) => new(NetworkKey, "", "Wi-Fi", "Your connection and how fast it is",
        () => new NetworkPage(demo));

    /// <summary>Opens the Holm window (or brings it forward) at <paramref name="page"/>.</summary>
    async void ShowHub(string? page = null)
    {
        SetExpanded(false);
        if (PackageIdentity.IsPackaged)
        {
            try { _startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("HolmStartup"); }
            catch (Exception ex) { Diag.Error(ex); }
        }
        if (_settingsWindow == null)
        {
            _settingsWindow = MakeSettingsWindow(start: page);
            _settingsWindow.Closed += OnHubClosed;
            _settingsWindow.Show();
        }
        else if (page != null) _settingsWindow.Navigate(page);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.BringForward();
    }

    DispatcherTimer? _handBack;

    /// <summary>
    /// The Holm window closed. Its pictures and the decoded GIFs of its chats would otherwise stay in memory (some 60–90 MB
    /// after a few openings); they go, and a moment later Holm hands the memory back to Windows.
    /// </summary>
    void OnHubClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _settingsWindow)) _settingsWindow = null;
        GifCache.ReleaseDecoded();
        _handBack ??= MakeTimer(5000, HandMemoryBack, oneShot: true);
        _handBack.Stop();
        _handBack.Start();
    }

    void HandMemoryBack()
    {
        if (_settingsWindow != null) return; // opened again meanwhile
        GC.Collect();
        GC.WaitForPendingFinalizers(); // the pictures' memory goes with their finalizers
        GC.Collect();
        if (!_expanded) Native.TrimWorkingSet();
    }

    /// <param name="demo">For snapshots: a made-up profile in place of yours, and Apply saves nothing.</param>
    SettingsWindow MakeSettingsWindow(ProfileDraft? demo = null, string? start = null)
    {
        var version = RunningVersion;
        var profile = demo != null
            ? new ProfileEditor("Alex", () => demo, _ => { }, HubTheme.ActionButtonStyle)
            : new ProfileEditor(Native.DisplayName(), CurrentProfile, ApplyProfile, HubTheme.ActionButtonStyle);
        Func<ProfileDraft> current = demo != null ? () => demo : CurrentProfile;
        return new SettingsWindow(SettingsGroups(), $"{version.Major}.{version.Minor}.{version.Build}", _settings.Animations, SyncSwitches, profile,
                                  HubPages(demo != null), () => MeCard(current()), UpdatesPage(),
                                  new[] { NetworkHubPage(demo != null) }, start);
    }
}
