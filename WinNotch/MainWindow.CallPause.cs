using System;
using System.Windows;

namespace WinNotch;

/// <summary>Pause the music for a call, and play it again afterwards if Holm paused it (right-click › Pause music during calls).</summary>
public partial class MainWindow
{
    readonly CallPause _callPause = new();

    /// <summary>Whenever the call or the music changes. Works whether or not the call shows in the notch.</summary>
    void UpdateCallPause()
    {
        if (!_settings.PauseForCalls) return;
        var call = Calls.Current(AllSensors);
        string player = _info?.AppId is { Length: > 0 } id ? id : _info?.App ?? "";
        _callPause.Update(call?.Key, _info?.IsPlaying == true, player, out bool pause, out bool resume);
        if (pause)
        {
            Diag.Log("Music paused for a call");
            _media.Pause();
            ShowCallPausePeek();
        }
        else if (resume)
        {
            Diag.Log("Music resumed after the call");
            _media.Play();
        }
    }

    void ShowCallPausePeek() => ShowPeek("\uE769", "Music paused for the call", "", null, false, null);

    void MenuPauseForCalls_Click(object sender, RoutedEventArgs e)
    {
        _settings.PauseForCalls = MenuPauseForCalls.IsChecked;
        _settings.Save();
        if (!_settings.PauseForCalls) _callPause.Reset();
        else UpdateCallPause();
    }
}
