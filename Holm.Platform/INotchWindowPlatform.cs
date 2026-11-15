using System;

namespace Holm.Platform;

/// <summary>
/// What the notch window needs from the OS beyond what Avalonia offers: sitting above everything without ever
/// taking the keyboard, and letting clicks next to the notch through to whatever is behind the window.
/// </summary>
public interface INotchWindowPlatform
{
    /// <summary>Turns the window into an overlay: no taskbar entry, no focus, above normal windows.</summary>
    void MakeOverlay(IntPtr window);

    /// <summary>
    /// Only this rectangle (in device pixels, relative to the window) takes the mouse; everything else in the
    /// window is click-through.
    /// </summary>
    void SetHitRegion(IntPtr window, int x, int y, int width, int height);

    /// <summary>
    /// Lets the overlay take the keyboard (the user clicked into one of its text fields) or makes it give the
    /// keyboard up again. Called right after a click on the window, when the system allows taking the foreground.
    /// </summary>
    void SetKeyboard(IntPtr window, bool take);
}

/// <summary>For systems without a backend yet: the window stays an ordinary topmost window.</summary>
public sealed class NoNotchWindowPlatform : INotchWindowPlatform
{
    public void MakeOverlay(IntPtr window) { }
    public void SetHitRegion(IntPtr window, int x, int y, int width, int height) { }
    public void SetKeyboard(IntPtr window, bool take) { }
}
