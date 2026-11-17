using System;

namespace Holm.Platform;

/// <summary>
/// Says when a mouse button goes down outside Holm's own windows, so the open notch and its pop-outs can close as a
/// menu does (Windows: the button state and the window under the pointer, read every 35 ms only while watching;
/// Linux: XInput2's raw button events). Only while <see cref="Watch"/> is on.
/// </summary>
public interface IClickAwayPlatform
{
    /// <summary>A click landed outside Holm; raised on any thread.</summary>
    event Action? ClickedElsewhere;

    void Watch(bool on);
}

/// <summary>For systems without a backend yet.</summary>
public sealed class NoClickAwayPlatform : IClickAwayPlatform
{
    public event Action? ClickedElsewhere { add { } remove { } }
    public void Watch(bool on) { }
}
