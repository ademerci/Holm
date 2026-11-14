using System;
using System.Windows;

namespace WinNotch;

/// <summary>
/// Pop-outs that would otherwise come twice or somewhere else: every app's notification at the top in the notch, and
/// Windows' own volume pop-up kept away while the notch shows the volume.
/// </summary>
public partial class MainWindow
{
    async void MenuAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        _settings.AllNotifications = MenuAllNotifications.IsChecked;
        _settings.Save();
        _chat.EveryApp = _settings.AllNotifications;
        if (!_settings.AllNotifications)
        {
            if (!_settings.Messages) _chat.Stop();
            return;
        }
        if (!_chat.IsRunning) await _chat.StartAsync(ask: true);
        // Once refused, Windows doesn't ask again; the switch is in Settings.
        if (_chat.Access == MessagesService.AccessState.Denied) { TryOpen("ms-settings:privacy-notifications"); return; }
        // Windows has no way for an app to move its banners; with Do not disturb they stay away and still reach Holm.
        ShowTask("", ShareBlue, "Notifications show up here now", "Windows' own banners stay away with Do not disturb", null,
                 ("Open settings", () => { HideTask(); TryOpen("ms-settings:notifications"); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(10);
        _taskHide.Start();
    }

    void MenuNotchOnlyPopups_Click(object sender, RoutedEventArgs e)
    {
        _settings.NotchOnlyPopups = MenuNotchOnlyPopups.IsChecked;
        _settings.Save();
        if (!_settings.NotchOnlyPopups) { NativeOsd.Restore(); return; }
        // Windows 10 makes its pop-up at the first key press: until then there's nothing to find, and that's fine.
        if (!NativeOsd.Hide() && Environment.OSVersion.Version.Build >= 22000)
            ShowPeek("", "Windows 11 keeps its own pop-up", "", null, false, null, seconds: 4);
    }
}
