using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A click anywhere that isn't Holm closes the notch (the WPF app's MainWindow.ClickAway.cs): the open notch and the
/// pop-outs that come and go on their own shrink back at once, as a menu closes when you click away. Pop-outs that
/// wait for a decision stay until they're answered. The system says when a click lands; nothing is watched while
/// nothing is open.
/// </summary>
public sealed partial class NotchWindow
{
    IClickAwayPlatform _clickAway = null!;

    void InitClickAway(IPlatformServices platform)
    {
        _clickAway = platform.ClickAway;
        _clickAway.ClickedElsewhere += () => Dispatcher.UIThread.Post(OnClickedElsewhere);
    }

    /// <summary>Something is open that a click elsewhere should close.</summary>
    bool ClosesOnClickAway => (_hovered && !PinnedClockOpen) || _peekActive || _volumePeek || (_taskVisible && _taskHide.IsEnabled);

    /// <summary>Starts or stops watching for clicks away as the notch opens and closes (from ApplyState).</summary>
    void WatchClicksAway() => _clickAway.Watch(ClosesOnClickAway);

    void OnClickedElsewhere()
    {
        if (!ClosesOnClickAway || _menuOpen || IsHolmUnderPointer()) return;
        Diag.Log("Closed by a click elsewhere");
        _expandDelay.Stop();
        _peekTimer.Stop();
        _volumeTimer.Stop();
        _peekActive = false;
        _volumePeek = false;
        if (_taskVisible && _taskHide.IsEnabled) HideTask();
        if (_hovered && !PinnedClockOpen)
        {
            EndTyping();
            SetExpanded(false); // also redraws
        }
        else ApplyState(animate: true);
    }

    /// <summary>Whether the pointer is over one of Holm's own windows: the notch, its menus, the settings window.</summary>
    bool IsHolmUnderPointer() => _island.IsPointerOver || _settingsWindow?.IsPointerOver == true;
}
