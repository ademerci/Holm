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
                Switch("Ctrl+H opens and closes the notch", "\uE765", "Press Ctrl+H anywhere to open the notch, and again to close it. Other apps don't get Ctrl+H while this is on.",
                    s => s.HotKey, SetHotKey),
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
                Switch("Low disk space warning", "\uEDA2", "One warning when the system drive runs low.", s => s.DiskWarning, SetDiskWarning),
                Switch("Caps Lock hint", "\uE765", "The notch says when Caps Lock goes on or off.", s => s.CapsLock, SetCapsLock),
                Switch("Internet connection lost", "\uE701", "Says when the internet drops and when it's back.", s => s.Connectivity, SetConnectivity),
            }),
            new("weather", "Weather", "\uE706", Color.FromRgb(0x32, 0xAD, 0xE6), "The weather in the notch", new[]
            {
                Switch("Sky colours", "\uE790", "With nothing playing, the open notch glows in the colours of the time of day and the weather. Off, it stays plain black.",
                    s => s.SkyColors, SetSkyColors),
            }),
        };
        return groups;
    }

    void OpenSettings()
    {
        // The notch closes first, so it can't hide the window.
        _hovered = false;
        ApplyState(animate: true);
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
        _settingsWindow = new SettingsWindow(SettingsGroups(), version, () => _settings.Animations);
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
