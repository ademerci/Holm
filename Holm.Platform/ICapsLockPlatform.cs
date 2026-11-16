using System;

namespace Holm.Platform;

/// <summary>
/// Says when Caps Lock goes on or off (Windows: a keyboard hook that looks at that one key; Linux: the X keyboard
/// extension's indicator events; macOS: the keyboard's modifier state). No other key is ever looked at.
/// </summary>
public interface ICapsLockPlatform
{
    /// <summary>The new state; raised on any thread, only on a change.</summary>
    event Action<bool>? Changed;

    void Start();
    void Stop();
}

/// <summary>For systems without a backend yet.</summary>
public sealed class NoCapsLockPlatform : ICapsLockPlatform
{
    public event Action<bool>? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
}
