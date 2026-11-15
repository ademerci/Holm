using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WinNotch;

// Ctrl+H opens the notch from anywhere, and closes it again. Windows hands the combination to Holm alone while it is
// registered, so other apps don't see it (browser history, Replace in editors): Settings › General turns it off.
public partial class MainWindow
{
    const int WM_HOTKEY = 0x0312, HotKeyId = 0x484F; // "HO"
    const uint MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000, VK_H = 0x48;
    bool _hotKeyRegistered;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    void StartHotKey()
    {
        StopHotKey();
        if (!_settings.HotKey || _hwnd == IntPtr.Zero) return;
        _hotKeyRegistered = RegisterHotKey(_hwnd, HotKeyId, MOD_CONTROL | MOD_NOREPEAT, VK_H);
        if (!_hotKeyRegistered) Diag.Log("Ctrl+H is taken by another app (error " + Marshal.GetLastWin32Error() + ")");
    }

    void StopHotKey()
    {
        if (!_hotKeyRegistered) return;
        UnregisterHotKey(_hwnd, HotKeyId);
        _hotKeyRegistered = false;
    }

    /// <summary>Ctrl+H: open the notch, or close it if it's open.</summary>
    internal void ToggleFromKeyboard()
    {
        if (_hiddenForFullscreen) return;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        if (_expanded)
        {
            EndTyping();
            SetExpanded(false);
        }
        else SetExpanded(true); // stays open until Ctrl+H again or a click elsewhere
    }

    void MenuHotKey_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotKey = MenuHotKey.IsChecked;
        _settings.Save();
        StartHotKey();
    }
}
