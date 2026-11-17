using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Settings… in the right-click menu opens the on/off switches in a window of their own (SettingsWindow), as in the
/// WPF app (MainWindow.Settings.cs): the same groups, icons and words, for the features this app has so far.
/// Customize › Bounce picks how much the springs swing while Bouncy motion is on.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly (string Name, double Level)[] BounceLevels = { ("Subtle", 0.5), ("Normal", 1), ("Lively", Spring.MaxBounce) };

    SettingsWindow? _settingsWindow;

    SettingsSwitch Switch(string header, string glyph, string? help, Func<AppSettings, bool> get, Action<bool> set, string? actionLabel = null, Action? runAction = null)
        => new(header, glyph, help, () => get(_settings), set, get(new AppSettings()), actionLabel, runAction);

    List<SettingsGroup> SettingsGroups()
    {
        void Save() => _settings.Save();
        var groups = new List<SettingsGroup>
        {
            new("general", "General", "", Color.FromRgb(0x63, 0x63, 0x66), "How the notch looks and starts", new[]
            {
                Switch("Animations", "", "Off: the notch jumps between sizes and nothing in it moves.", s => s.Animations,
                    on => { _settings.Animations = on; Save(); ApplyState(animate: false); }),
                Switch("Bouncy motion", "", "Holm opens the notch with a small overshoot. Off, it stops without one. Set the amount under right-click \u203A Customize \u203A Bounce.",
                    s => s.Bounce, on => { _settings.Bounce = on; Save(); ApplyBounce(); }),
                Switch("Equalizer follows the music", "", "The equalizer bars follow the loudness of your music. Holm reads the level and records nothing.",
                    s => s.ReactiveEqualizer, on => { _settings.ReactiveEqualizer = on; Save(); UpdateEqualizer(); }),
                Switch("Drag notch position", "\uE7C2", "Drag the notch by its top edge to move it left or right. Holm keeps the position after you switch this off.",
                    s => s.Draggable, on => { FinishPositionDrag(); _settings.Draggable = on; Save(); }, "Reset Position", ResetNotchPosition),
                Switch("Minimize when idle", "\uE921", "With nothing playing, Holm shrinks the notch to a thin line.", s => s.MiniIdle,
                    on => { _settings.MiniIdle = on; Save(); ApplyState(animate: true); }),
                Switch("AI usage rings", "\uE9D9", "A ring at each end of the notch shows how much of your Claude and Codex limits you have used. Hover a ring for each limit and the sessions at work.",
                    s => s.AiUsage, on => { _settings.AiUsage = on; Save(); StartAiUsage(); if (on) _ = RefreshAiUsageAsync(); }),
                // Not a setting of the file: the system's own sign-in list, so its default is "off".
                new SettingsSwitch(OperatingSystem.IsWindows() ? "Start with Windows" : "Start at sign-in", "",
                    "Holm opens when you sign in.", () => _autostart.IsEnabled,
                    on => _autostart.Set(on), false),
                Switch("Ctrl+H opens and closes the notch", "\uE765", "Ctrl+H opens the notch from any app and closes it again. Other apps lose Ctrl+H while this is on.",
                    s => s.HotKey, SetHotKey),
                Switch("Minimize over browser tabs", "\uE774", "Over a maximized browser, Holm shrinks the notch to a thin line so you can click the tabs under it.",
                    s => s.HideOverBrowsers, on => { _settings.HideOverBrowsers = on; Save(); WatchFullscreen(); }),
            }),
            new("notch", "Open notch", "", Color.FromRgb(0x5E, 0x5C, 0xE6), "What the open notch shows and the tools in its bar", new[]
            {
                Switch("Media controls", "\uE8D6", "Music and video controls with the cover and the track.",
                    s => s.MediaControls, on =>
                    {
                        _settings.MediaControls = on;
                        Save();
                        if (!on) { CancelSleep(); _callPause.Reset(); }
                        RefreshMediaUi();
                        ShowSleep();
                        ShowSky();
                        ApplyState(animate: true);
                    }),
                Switch("Notes", "", "A note in the open notch, saved on " + (OperatingSystem.IsWindows() ? "this PC." : "this computer."), s => s.Notes, on => { _settings.Notes = on; Save(); RefreshToolBar(); }),
                Switch("Reminders", "", "Type “20 min laundry” and Holm rings in 20 minutes.", s => s.Reminders,
                    on => { _settings.Reminders = on; Save(); RefreshToolBar(); ShowIdleStatus(); }),
                Switch("Calculator", "", "Type a sum and copy the result.", s => s.Calculator, on => { _settings.Calculator = on; Save(); RefreshToolBar(); }),
                Switch("Clipboard history", "", "Your last 20 copied texts and pictures, kept in memory.", s => s.ClipboardHistory, SetClipboardHistory),
                Switch("Volume per app", "", "A volume slider for each app that plays sound.", s => s.AppVolume, on => { _settings.AppVolume = on; Save(); RefreshToolBar(); }),
                Switch("Color picker", "", "Pick a pixel on screen and copy its hex code.", s => s.ColorPicker, on => { _settings.ColorPicker = on; Save(); RefreshToolBar(); }),
                Switch("Focumon", "\uE7FC", "Show your Focumon monster and timer in the closed notch (instead of weather) and in the open bar. Needs the Focumon for Holm browser extension (extension/focumon in the repo).",
                    s => s.Focumon, SetFocumon),
            }),
            new("popouts", "Pop-outs", "\uEA8F", Color.FromRgb(0xFF, 0x9F, 0x0A), "What pops out of the notch when it happens", PopoutSwitches()),
            new("privacy", "Calls and privacy", "\uE72E", Color.FromRgb(0x34, 0xC7, 0x59), "Calls, the microphone and camera, and your clipboard", new[]
            {
                Switch("Calls", "\uE717", "Holm shows a running call with its time and a mute button.", s => s.Calls, SetCalls),
                Switch("Pause music during calls", "\uE769", "Holm pauses your music when a call starts and plays it after.", s => s.PauseForCalls, SetPauseForCalls),
                Switch("Microphone and camera dot", "\uE720", "A dot beside the notch while an app uses the microphone or camera.", s => s.Privacy, SetPrivacy),
                Switch("Dictation with Handy", "\uE720", "Dictate with Handy, free and offline. The notch shows when Handy listens" + (OperatingSystem.IsWindows() ? " and hides its overlay" : "") + ". Holm finds Handy or offers to install it.",
                    s => s.Dictation, SetDictation),
                Switch("VPN indicator", "\uE72E", "A lock in the notch while a VPN is connected.", s => s.Vpn, SetVpn),
                Switch("Clear copied passwords after 30 s", "\uE8D7", "Holm removes a copied password from the clipboard after 30 seconds. It keeps copied texts in memory and copied pictures in your temp folder until you clear the list or restart Holm. Holm skips passwords that your password manager marks as private.",
                    s => s.ClearPasswords, SetClearPasswords),
            }),
            new("beam", "Beam", "\uE724", Color.FromRgb(0x0A, 0x84, 0xFF), "Sending files to friends, people nearby and your phone", new[]
            {
                Switch("Beam (drop files on the notch)", "\uE724", "Drop files on the notch to send them. Friends and chat need this too. Holm checks copied text for friend and file codes and keeps none of it.",
                    s => s.Beam, SetBeam),
                Switch("Send folders as ZIP", "\uE8B7", "Holm packs a folder as a ZIP before it sends it.", s => s.SendFolders, on => { _settings.SendFolders = on; Save(); }),
                Switch("Send later when a friend is offline", "\uE823", "Files for an offline friend wait until your friend comes back online.", s => s.SendLater, SetSendLater),
            }),
            new("friends", "Friends", "\uE716", Color.FromRgb(0xFF, 0x37, 0x5F), "Chat and what your friends see", new[]
            {
                Switch("Share my activity", "\uE716", "Your friends see the song or the game you play. Holm seals each note so only your friends can read it.",
                    s => s.ShareActivity, SetShareActivity),
                Switch("Chat pop-outs", "\uE8BD", "A friend's message pops out with Reply. You find it in Chat either way.", s => s.ChatPopups, on => { _settings.ChatPopups = on; Save(); }),
                Switch("Achievement pop-outs", "\uE734", "A trophy pops out when you unlock an achievement.", s => s.AchievementPopups, on => { _settings.AchievementPopups = on; Save(); }),
            }),
            new("weather", "Weather", "\uE706", Color.FromRgb(0x32, 0xAD, 0xE6), "The weather in the notch", WeatherSwitches()),
        };
        return groups;
    }

    void OpenSettings() => ShowHub(SettingsGroups()[0].Key);

    /// <summary>Opens the Holm window on a page: a friends page ("chat", "add", …) or a group of settings.</summary>
    void ShowHub(string page)
    {
        // The notch closes first, so it can't hide the window.
        CloseFromInside();
        if (_settingsWindow != null)
        {
            _settingsWindow.Navigate(page);
            _settingsWindow.Activate();
            return;
        }
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
        _settingsWindow = new SettingsWindow(SettingsGroups(), version, () => _settings.Animations, HubPages(), page,
            new ProfileEditor(FriendsService.AccountName(), CurrentProfile, ApplyProfile, AvatarColors, AvatarColor),
            () => MeCard(CurrentProfile()),
            new[] { new HubPage("updates", "\uE896", "Updates", "New versions of Holm", UpdatesPage) },
            new[] { new HubPage("network", "\uE701", "Wi-Fi", "Speed test and connection details", () => new NetworkPage(CurrentLink, TrySetClipboardAsync)) });
        if ((Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null)) is { } screen)
            _settingsWindow.Height = Math.Min(740, screen.WorkingArea.Height / screen.Scaling - 24);
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; GifCache.ReleaseDecoded(); UpdateStreamMask(); HandMemoryBackSoon(); };
        _settingsWindow.Opened += (_, _) => KeepOutOfStream();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    List<SettingsSwitch> PopoutSwitches()
    {
        var list = new List<SettingsSwitch>
        {
            Switch("Downloads", "\uE896", "A finished download pops out with Open and Show.", s => s.Downloads, SetDownloads),
            Switch("Screenshots", "\uE722", "A new screenshot pops out with Send and Shelf.", s => s.Screenshots, SetScreenshots),
            Switch("Copy text from screenshots", "\uE8D2", OperatingSystem.IsWindows() ? "Holm reads the text in a screenshot with the OCR built into Windows." : "Holm reads the text in a screenshot with tesseract, on this computer.",
                s => s.ScreenshotText, on => { _settings.ScreenshotText = on; Save(); }),
            Switch("Caps Lock hint", "\uE765", "The notch shows when you turn Caps Lock on or off.", s => s.CapsLock, SetCapsLock),
            Switch("All notifications in the notch", "\uE7E7", OperatingSystem.IsWindows()
                    ? "Holm shows the notifications of all your apps in the notch, besides messages. Turn on Do not disturb in Windows to hide its banners. Holm still gets them."
                    : "Holm shows the notifications of all your apps in the notch, besides messages. Turn on Do not disturb in your desktop to hide its banners. Holm still gets them.",
                s => s.AllNotifications, SetAllNotifications),
        };
        if (_mediaKeys.IsSupported)
            list.Add(Switch("Volume and media only in the notch", "\uE995", "Holm takes the volume and media keys, and the notch shows the change in place of the Windows pop-up. Brightness keys stay with Windows.",
                s => s.NotchOnlyPopups, SetNotchOnlyPopups));
        list.AddRange(new[]
        {
            Switch("Low disk space warning", "\uEDA2", "One warning when your system drive runs low on space.", s => s.DiskWarning, SetDiskWarning),
            Switch("USB drives", "\uE88E", "A USB stick pops out with Open and Eject.", s => s.UsbDrives, SetUsbDrives),
            Switch("Internet connection lost", "\uE701", "Holm tells you when the internet drops and when it comes back.", s => s.Connectivity, SetConnectivity),
        });
        return list;
        void Save() => _settings.Save();
    }

    List<SettingsSwitch> WeatherSwitches()
    {
        var list = new List<SettingsSwitch>();
        if (_location.IsSupported)
            list.Add(Switch("Set up weather using approximate location", "\uE81D", "If you set no city, Holm asks Windows once for your location. Holm rounds the coordinates and sends them to Open-Meteo for the forecast.",
                s => s.WeatherAutoLocation, SetWeatherAutoLocation));
        list.Add(Switch("Sky colours", "\uE790", "With nothing playing, the open notch takes the colours of the time of day and the weather. Off, it stays black.",
            s => s.SkyColors, SetSkyColors));
        return list;
    }

    // ───────────── memory ─────────────

    /// <summary>A while after the notch rests (started, closed, music stopped), Holm hands unused memory back, as the WPF app.</summary>
    DispatcherTimer? _trimTimerField;
    DispatcherTimer _trimTimer => _trimTimerField ??= OneShot(6000, () => { if (!_hovered && _settingsWindow == null) _platform.TrimMemory(); });
    DispatcherTimer? _handBack;

    static DispatcherTimer OneShot(double ms, Action act)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); act(); };
        return timer;
    }

    /// <summary>
    /// The Holm window closed. Its pictures and the decoded GIFs of its chats would otherwise stay in memory; they go,
    /// and a moment later Holm hands the memory back to the system.
    /// </summary>
    void HandMemoryBackSoon()
    {
        _handBack ??= OneShot(5000, HandMemoryBack);
        _handBack.Stop();
        _handBack.Start();
    }

    void HandMemoryBack()
    {
        if (_settingsWindow != null) return; // opened again meanwhile
        GC.Collect();
        GC.WaitForPendingFinalizers(); // the pictures' memory goes with their finalizers
        GC.Collect();
        if (!_hovered) _platform.TrimMemory();
    }

    void ApplyBounce() => Spring.Bounce = _settings.Bounce ? _settings.BounceLevel : 0;

    /// <summary>Customize › Bounce: Subtle, Normal or Lively, while Bouncy motion is on.</summary>
    void FillBounceMenu(MenuItem menu)
    {
        menu.Items.Clear();
        menu.IsEnabled = _settings.Bounce;
        foreach (var (name, level) in BounceLevels)
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = Math.Abs(_settings.BounceLevel - level) < 0.01 };
            item.Click += (_, _) =>
            {
                _settings.BounceLevel = level;
                _settings.Save();
                ApplyBounce();
            };
            menu.Items.Add(item);
        }
    }
}
