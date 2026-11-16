using System;

namespace Holm.Platform;

/// <summary>
/// Ctrl+H from anywhere (Windows: RegisterHotKey; Linux: a key grab on the X root window; macOS: the Carbon hot key
/// service, Control+H). While registered the system hands the combination to Holm alone.
/// </summary>
public interface IHotKeyPlatform
{
    /// <summary>Ctrl+H was pressed; raised on any thread.</summary>
    event Action? Pressed;

    /// <summary>False when another app holds the combination already.</summary>
    bool Start();
    void Stop();
}

/// <summary>For systems without a backend yet.</summary>
public sealed class NoHotKeyPlatform : IHotKeyPlatform
{
    public event Action? Pressed { add { } remove { } }
    public bool Start() => false;
    public void Stop() { }
}
