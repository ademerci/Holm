using System;

namespace Holm.Platform;

/// <summary>What the window in front is, as far as the notch cares.</summary>
/// <param name="Fullscreen">A game, video or presentation fills the screen the notch is on.</param>
/// <param name="ProcessName">Its program's name ("chrome", "firefox"), for the browser check; empty when unknown.</param>
/// <param name="Maximized">It is maximized (zoomed) on the notch's screen.</param>
public readonly record struct ForegroundWindow(bool Fullscreen, string ProcessName, bool Maximized);

/// <summary>
/// The window in front (Windows: the shell's notification state and the foreground window; Linux: the window
/// manager's active window and its state). Asked every 1.5 s, as the WPF app does.
/// </summary>
public interface IForegroundPlatform
{
    /// <summary>The front window on the screen with these bounds (device pixels), or default when it can't tell.</summary>
    ForegroundWindow Read(int x, int y, int width, int height);

    /// <summary>Calls <paramref name="changed"/> on the calling (UI) thread whenever another app's window comes to the
    /// front, so the notch doesn't wait for the next look. Nothing where the system can't say.</summary>
    void WatchChanges(Action changed) { }
}

/// <summary>For systems without a backend yet.</summary>
public sealed class NoForegroundPlatform : IForegroundPlatform
{
    public ForegroundWindow Read(int x, int y, int width, int height) => default;
}
