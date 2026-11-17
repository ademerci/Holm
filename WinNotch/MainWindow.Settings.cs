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
            new(MenuAnim, "", "Off: the notch jumps between sizes and nothing in it moves."),
            new(MenuBounce, "\uE945", "Holm opens the notch with a small overshoot. Off, it stops without one. Set the amount under right-click \u203A Customize \u203A Bounce."),
            new(MenuReactiveEq, "\uE8D6", "The equalizer bars follow the loudness of your music. Holm reads the level and records nothing."),
            new(MenuDraggable, "\uE7C2", "Drag the notch by its top edge to move it left or right. Holm keeps the position after you switch this off.", "Reset Position", ResetNotchPosition),
            new(MenuMini, "", "With nothing playing, Holm shrinks the notch to a thin line."),
            new(MenuAiUsage, "", "A ring at each end of the notch shows how much of your Claude and Codex limits you have used. Hover a ring for each limit and the sessions at work."),
            new(MenuAutostart, "", "Holm opens when you sign in."),
            new(MenuHotKey, "", "Ctrl+H opens the notch from any app and closes it again. Other apps lose Ctrl+H while this is on."),
            new(MenuHideOverBrowsers, ""),
        }),
        new SettingsGroup("notch", "Open notch", "", Color.FromRgb(0x5E, 0x5C, 0xE6), "What the open notch shows and the tools in its bar", new SettingsSwitch[]
        {
            new(MenuMediaControls, ""),
            new(MenuNotes, "", "A note in the open notch, saved on this PC."),
            new(MenuReminders, "", "Type “20 min laundry” and Holm rings in 20 minutes."),
            new(MenuCalculator, "", "Type a sum and copy the result."),
            new(MenuClipboardHistory, "", "Your last 20 copied texts and pictures, kept in memory."),
            new(MenuAppVolume, "", "A volume slider for each app that plays sound."),
            new(MenuColorPicker, "", "Pick a pixel on screen and copy its hex code."),
            new(MenuFocumon, ""),
        }),
        new SettingsGroup("popouts", "Pop-outs", "", Color.FromRgb(0xFF, 0x9F, 0x0A), "What pops out of the notch when it happens", new SettingsSwitch[]
        {
            new(MenuDownloads, "", "A finished download pops out with Open and Show."),
            new(MenuScreenshots, "", "A new screenshot pops out with Send and Shelf."),
            new(MenuScreenshotText, "", "Holm reads the text in a screenshot with the OCR built into Windows."),
            new(MenuCapsLock, "", "The notch shows when you turn Caps Lock on or off."),
            new(MenuAllNotifications, "", "Holm shows the notifications of all your apps in the notch, besides messages. Turn on Do not disturb in Windows to hide its banners. Holm still gets them."),
            new(MenuNotchOnlyPopups, "", "Holm takes the volume and media keys, and the notch shows the change in place of the Windows pop-up. Brightness keys stay with Windows."),
            new(MenuDiskWarning, "", "One warning when your system drive runs low on space."),
            new(MenuUsbDrives, "", "A USB stick pops out with Open and Eject."),
            new(MenuConnectivity, "", "Holm tells you when the internet drops and when it comes back."),
        }),
        new SettingsGroup("privacy", "Calls and privacy", "", Color.FromRgb(0x34, 0xC7, 0x59), "Calls, the microphone and camera, and your clipboard", new SettingsSwitch[]
        {
            new(MenuCalls, "", "Holm shows a running call with its time and a mute button."),
            new(MenuPauseForCalls, "", "Holm pauses your music when a call starts and plays it after."),
            new(MenuPrivacy, "", "A dot beside the notch while an app uses the microphone or camera."),
            new(MenuDictation, "", "Dictate with Handy, free and offline. The notch shows when Handy listens and hides its overlay. Holm finds Handy or offers to install it."),
            new(MenuVpn, "", "A lock in the notch while a VPN is connected."),
            new(MenuClearPasswords, "", "Holm removes a copied password from the clipboard after 30 seconds. It keeps copied texts in memory and copied pictures in your temp folder until you clear the list or restart Holm. Holm skips passwords that your password manager marks as private."),
        }),
        new SettingsGroup("beam", "Beam", "", Color.FromRgb(0x0A, 0x84, 0xFF), "Sending files to friends, people nearby and your phone", new SettingsSwitch[]
        {
            new(MenuBeam, "", "Drop files on the notch to send them. Friends and chat need this too. Holm checks copied text for friend and file codes and keeps none of it."),
            new(MenuSendFolders, "", "Holm packs a folder as a ZIP before it sends it."),
            new(MenuSendLater, "", "Files for an offline friend wait until your friend comes back online."),
        }),
        new SettingsGroup("friends", "Friends", "", Color.FromRgb(0xFF, 0x37, 0x5F), "Chat and what your friends see", new SettingsSwitch[]
        {
            new(MenuShareActivity, "", "Your friends see the song or the game you play. Holm seals each note so only your friends can read it."),
            new(MenuChatPopups, "", "A friend's message pops out with Reply. You find it in Chat either way."),
            new(MenuAchievementPopups, "", "A trophy pops out when you unlock an achievement."),
        }),
        new SettingsGroup("weather", "Weather", "", Color.FromRgb(0x32, 0xAD, 0xE6), "The weather in the notch", new SettingsSwitch[]
        {
            new(MenuWeatherAutoLocation, "", "If you set no city, Holm asks Windows once for your location. Holm rounds the coordinates and sends them to Open-Meteo for the forecast."),
            new(MenuSkyColors, "", "With nothing playing, the open notch takes the colours of the time of day and the weather. Off, it stays black."),
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
    void ShowHub(string? page = null)
    {
        SetExpanded(false);
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
