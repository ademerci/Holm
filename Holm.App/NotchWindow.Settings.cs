using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
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

    SettingsSwitch Switch(string header, string glyph, string? help, Func<AppSettings, bool> get, Action<bool> set)
        => new(header, glyph, help, () => get(_settings), set, get(new AppSettings()));

    List<SettingsGroup> SettingsGroups()
    {
        void Save() => _settings.Save();
        var groups = new List<SettingsGroup>
        {
            new("general", "General", "", Color.FromRgb(0x63, 0x63, 0x66), "How the notch looks and starts", new[]
            {
                Switch("Animations", "", "Smooth transitions, the moving equalizer, GIFs and the animated sky.", s => s.Animations,
                    on => { _settings.Animations = on; Save(); ApplyState(animate: false); }),
                Switch("Bouncy motion", "", "The notch springs open with a little bounce. Off, it glides in without overshooting. How much it bounces: right-click the notch › Customize › Bounce.",
                    s => s.Bounce, on => { _settings.Bounce = on; Save(); ApplyBounce(); }),
                Switch("Equalizer follows the music", "", "The equalizer bars move with the music that plays. Only the loudness is read, nothing is recorded.",
                    s => s.ReactiveEqualizer, on => { _settings.ReactiveEqualizer = on; Save(); UpdateEqualizer(); }),
                Switch("Minimize when idle", "\uE921", "The notch shrinks to a small pill while nothing is going on.", s => s.MiniIdle,
                    on => { _settings.MiniIdle = on; Save(); ApplyState(animate: true); }),
                Switch("Ctrl+H opens and closes the notch", "\uE765", "Press Ctrl+H anywhere to open the notch, and again to close it. Other apps don't get Ctrl+H while this is on.",
                    s => s.HotKey, SetHotKey),
                Switch("Minimize over browser tabs", "\uE774", "While a maximized browser is in front on the notch's screen, the notch shrinks to a small pill so the tabs under it can be clicked. Hover over or click the pill to open Holm. Off by default.",
                    s => s.HideOverBrowsers, on => { _settings.HideOverBrowsers = on; Save(); WatchFullscreen(); }),
                // Not a setting of the file: the system's own sign-in list, so its default is "off".
                new SettingsSwitch("Start at sign-in", "", "Holm starts when you sign in.", () => _autostart.IsEnabled,
                    on => _autostart.Set(on), false),
            }),
            new("notch", "Open notch", "", Color.FromRgb(0x5E, 0x5C, 0xE6), "What the open notch shows and the tools in its bar", new[]
            {
                Switch("Notes", "", "A small note in the open notch, kept on this computer.", s => s.Notes, on => { _settings.Notes = on; Save(); RefreshToolBar(); }),
                Switch("Reminders", "", "Type “20 min laundry” and the notch rings on time.", s => s.Reminders,
                    on => { _settings.Reminders = on; Save(); RefreshToolBar(); ShowIdleStatus(); }),
                Switch("Calculator", "", "Type a sum; the result is ready to copy.", s => s.Calculator, on => { _settings.Calculator = on; Save(); RefreshToolBar(); }),
                Switch("Clipboard history", "", "Your last 20 copied texts and pictures, in memory only.", s => s.ClipboardHistory, SetClipboardHistory),
                Switch("Volume per app", "", "A volume slider for every app that plays sound.", s => s.AppVolume, on => { _settings.AppVolume = on; Save(); RefreshToolBar(); }),
                Switch("Color picker", "", "Pick any pixel's color and copy its hex code.", s => s.ColorPicker, on => { _settings.ColorPicker = on; Save(); RefreshToolBar(); }),
            }),
            new("popouts", "Pop-outs", "\uEA8F", Color.FromRgb(0xFF, 0x9F, 0x0A), "What pops out of the notch when it happens", new[]
            {
                Switch("Downloads", "\uE896", "A finished download pops out with Open and Show.", s => s.Downloads, SetDownloads),
                Switch("Screenshots", "\uE722", "A new screenshot pops out with Shelf and Show.", s => s.Screenshots, SetScreenshots),
                Switch("Caps Lock hint", "\uE765", "The notch says when Caps Lock goes on or off.", s => s.CapsLock, SetCapsLock),
                Switch("All notifications in the notch", "\uE7E7", OperatingSystem.IsWindows()
                        ? "Every app's notification pops out at the top in the notch, not only messages. Turn on Do not disturb in Windows to keep its own banners away; they still reach Holm."
                        : "Every app's notification pops out at the top in the notch, not only messages. Turn on Do not disturb in your desktop to keep its own banners away; they still reach Holm.",
                    s => s.AllNotifications, SetAllNotifications),
                Switch("Low disk space warning", "\uEDA2", "One warning when the system drive runs low.", s => s.DiskWarning, SetDiskWarning),
                Switch("USB drives", "\uE88E", "USB sticks pop out with Open and Eject.", s => s.UsbDrives, SetUsbDrives),
                Switch("Internet connection lost", "\uE701", "Says when the internet drops and when it's back.", s => s.Connectivity, SetConnectivity),
            }),
            new("beam", "Beam", "\uE724", Color.FromRgb(0x0A, 0x84, 0xFF), "Sending files to friends, people nearby and your phone", new[]
            {
                Switch("Beam (drop files on the notch)", "\uE724", "Drop files on the notch to send them. Also needed for friends and chat. Holm looks at copied text for friend and file codes; nothing of it is kept or sent.",
                    s => s.Beam, SetBeam),
                Switch("Send folders as ZIP", "\uE8B7", "Folders are packed as a ZIP before they're sent.", s => s.SendFolders, on => { _settings.SendFolders = on; Save(); }),
            }),
            new("weather", "Weather", "\uE706", Color.FromRgb(0x32, 0xAD, 0xE6), "The weather in the notch", new[]
            {
                Switch("Sky colours", "\uE790", "With nothing playing, the open notch glows in the colours of the time of day and the weather. Off, it stays plain black.",
                    s => s.SkyColors, SetSkyColors),
            }),
            new("privacy", "Calls and privacy", "\uE72E", Color.FromRgb(0x34, 0xC7, 0x59), "Calls, the microphone and camera, and your clipboard", new[]
            {
                Switch("Calls", "\uE717", "Shows a call with its time and a mute button.", s => s.Calls, SetCalls),
                Switch("Pause music during calls", "\uE769", "Music pauses when a call starts and plays on after.", s => s.PauseForCalls, SetPauseForCalls),
                Switch("Microphone and camera dot", "\uE720", "A dot beside the notch while an app uses the microphone or camera.", s => s.Privacy, SetPrivacy),
                Switch("VPN indicator", "\uE72E", "A small lock while a VPN is connected.", s => s.Vpn, SetVpn),
                Switch("Clear copied passwords after 30 s", "\uE8D7", "Copied passwords leave the clipboard after 30 seconds. Copied texts stay in memory; copied pictures wait in your temp folder until the list is cleared or Holm restarts. Passwords marked private by password managers are never read.",
                    s => s.ClearPasswords, SetClearPasswords),
            }),
        };
        return groups;
    }

    void OpenSettings() => ShowHub(SettingsGroups()[0].Key);

    /// <summary>Opens the Holm window on a page: a friends page ("chat", "add", …) or a group of settings.</summary>
    void ShowHub(string page)
    {
        // The notch closes first, so it can't hide the window.
        SetExpanded(false);
        if (_settingsWindow != null)
        {
            _settingsWindow.Navigate(page);
            _settingsWindow.Activate();
            return;
        }
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
        _settingsWindow = new SettingsWindow(SettingsGroups(), version, () => _settings.Animations, HubPages(), page);
        if ((Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null)) is { } screen)
            _settingsWindow.Height = Math.Min(740, screen.WorkingArea.Height / screen.Scaling - 24);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
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
