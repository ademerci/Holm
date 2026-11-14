using System;
using System.Windows;

namespace WinNotch;

/// <summary>
/// Pop-outs that would otherwise come twice or somewhere else: every app's notification at the top in the notch, and the
/// volume and media keys handled by Holm, so Windows doesn't open its own pop-up next to the notch's.
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

    MediaKeys? _mediaKeys;

    void MenuNotchOnlyPopups_Click(object sender, RoutedEventArgs e)
    {
        _settings.NotchOnlyPopups = MenuNotchOnlyPopups.IsChecked;
        _settings.Save();
        StartMediaKeys();
    }

    void StartMediaKeys()
    {
        _mediaKeys?.Dispose();
        _mediaKeys = null;
        if (!_settings.NotchOnlyPopups) return;
        try { _mediaKeys = new MediaKeys(Dispatcher, TakesKey, PressKey); }
        catch (System.ComponentModel.Win32Exception ex) { Diag.Log("Media keys hook: " + ex.Message); }
    }

    /// <summary>
    /// Volume keys always, while the notch can show the volume; media keys only while something plays that Holm can
    /// control. In a fullscreen game the notch is away, so Windows gets the keys and shows its own pop-up.
    /// </summary>
    bool TakesKey(int vk)
        => !_hiddenForFullscreen && (vk is MediaKeys.VolumeMute or MediaKeys.VolumeDown or MediaKeys.VolumeUp || _info != null);

    void PressKey(int vk)
    {
        switch (vk)
        {
            case MediaKeys.VolumeMute:
                _volume.ToggleMute();
                break;
            case MediaKeys.VolumeUp or MediaKeys.VolumeDown when _volume.TryGet(out float level, out bool muted):
                if (muted) _volume.SetMute(false); // as Windows does: a volume key brings the sound back
                _volume.SetLevel(MediaKeys.Step(level, vk == MediaKeys.VolumeUp));
                break;
            case MediaKeys.PlayPause: _media.PlayPause(); break;
            case MediaKeys.NextTrack: _media.Next(); break;
            case MediaKeys.PreviousTrack: _media.Previous(); break;
            case MediaKeys.StopMedia: _media.Pause(); break;
        }
    }
}
