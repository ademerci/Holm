using System;
using System.ComponentModel;
using System.Windows;

namespace WinNotch;

/// <summary>A moment in the notch when Caps Lock goes on or off.</summary>
public partial class MainWindow
{
    CapsLockWatcher? _caps;

    void StartCapsLock()
    {
        StopCapsLock();
        if (!_settings.CapsLock) return;
        try
        {
            _caps = new CapsLockWatcher(Dispatcher);
            _caps.Changed += ShowCapsLock;
        }
        catch (Win32Exception ex) { Diag.Log("Caps Lock hook: " + ex.Message); }
    }

    void StopCapsLock()
    {
        _caps?.Dispose();
        _caps = null;
    }

    void ShowCapsLock(bool on)
    {
        ShowPeek(null, on ? "Caps Lock on" : "Caps Lock off", on ? "ABC" : "abc", null, false, on ? TimerOrange : DimBrush, seconds: on ? 2 : 1.5);
    }

    void MenuCapsLock_Click(object sender, RoutedEventArgs e)
    {
        _settings.CapsLock = MenuCapsLock.IsChecked;
        _settings.Save();
        StartCapsLock();
    }
}
