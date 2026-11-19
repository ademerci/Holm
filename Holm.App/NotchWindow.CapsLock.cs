using Avalonia.Threading;
using Holm.Platform;

namespace Holm.App;

/// <summary>A moment in the notch when Caps Lock goes on or off (the WPF app's MainWindow.CapsLock.cs).</summary>
public sealed partial class NotchWindow
{
    ICapsLockPlatform _capsLock = null!;

    void InitCapsLock(IPlatformServices platform)
    {
        _capsLock = platform.CapsLock;
        _capsLock.Changed += on => Dispatcher.UIThread.Post(() => ShowCapsLock(on));
    }

    void StartCapsLock()
    {
        _capsLock.Stop();
        if (_settings.CapsLock) _capsLock.Start();
    }

    void ShowCapsLock(bool on)
    {
        if (!_settings.CapsLock) return;
        ShowPeek(null, on ? "Caps Lock on" : "Caps Lock off", on ? "ABC" : "abc", on ? TimerOrange : Dim, seconds: on ? 2 : 1.5);
    }

    void SetCapsLock(bool on)
    {
        _settings.CapsLock = on;
        _settings.Save();
        StartCapsLock();
    }
}
