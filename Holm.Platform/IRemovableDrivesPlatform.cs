using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>A USB stick or SD card as the notch shows it.</summary>
/// <param name="Id">What to pass back to eject it (Windows: its letter; elsewhere: its mount point).</param>
/// <param name="Root">The folder to open.</param>
public sealed record RemovableDrive(string Id, string Name, string Root, long Total, long Free);

/// <summary>
/// Removable drives coming and going, told by the system (Windows: WM_DEVICECHANGE; Linux: the automounter's folder
/// under /media or /run/media; macOS: /Volumes), and ejecting them safely.
/// </summary>
public interface IRemovableDrivesPlatform
{
    /// <summary>Raised on any thread.</summary>
    event Action<RemovableDrive>? Arrived;
    /// <summary>The Id of a drive that went; raised on any thread.</summary>
    event Action<string>? Removed;

    void Start();
    void Stop();

    /// <summary>The drives plugged in now. May take a moment: call it off the UI thread.</summary>
    List<RemovableDrive> Present();

    /// <summary>Ejects it; null when it's safe to remove, else why not. Blocks: call it off the UI thread.</summary>
    string? Eject(string id);
}

/// <summary>For systems without a backend yet.</summary>
public sealed class NoRemovableDrivesPlatform : IRemovableDrivesPlatform
{
    public event Action<RemovableDrive>? Arrived { add { } remove { } }
    public event Action<string>? Removed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public List<RemovableDrive> Present() => new();
    public string? Eject(string id) => "Not supported here";
}
