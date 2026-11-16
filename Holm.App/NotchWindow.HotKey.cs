using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Ctrl+H opens the notch from anywhere, and closes it again (the WPF app's MainWindow.HotKey.cs). The system hands
/// the combination to Holm alone while it is registered: Settings › General turns it off.
/// </summary>
public sealed partial class NotchWindow
{
    IHotKeyPlatform _hotKey = null!;

    void InitHotKey(IPlatformServices platform)
    {
        _hotKey = platform.HotKey;
        _hotKey.Pressed += () => Dispatcher.UIThread.Post(ToggleFromKeyboard);
    }

    void StartHotKey()
    {
        _hotKey.Stop();
        if (_settings.HotKey && !_hotKey.Start()) Diag.Log("Ctrl+H is taken by another app");
    }

    /// <summary>Ctrl+H: open the notch, or close it if it's open.</summary>
    void ToggleFromKeyboard()
    {
        if (_hovered)
        {
            EndTyping();
            _hovered = false;
        }
        else
        {
            _hovered = true; // stays open until Ctrl+H again or the mouse comes and goes
        }
        ApplyState(animate: true);
    }

    void SetHotKey(bool on)
    {
        _settings.HotKey = on;
        _settings.Save();
        StartHotKey();
    }
}
