using System;
using System.Collections.Generic;

namespace Holm.Platform;

/// <summary>
/// Says when the clipboard changed, so the clipboard history can look (Windows: a clipboard listener; Linux: XFixes). Reading and writing the clipboard goes through Avalonia, the same on
/// every system; only the notice is the system's.
/// </summary>
public interface IClipboardPlatform
{
    /// <summary>Something new is on the clipboard; raised on any thread.</summary>
    event Action? Changed;

    void Start();
    void Stop();
}

/// <summary>For systems without a backend yet: nobody says when the clipboard changes.</summary>
public sealed class NoClipboardPlatform : IClipboardPlatform
{
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
}

/// <summary>What the formats on the clipboard say about its content, on each system's terms.</summary>
public enum ClipPrivacy
{
    /// <summary>Ordinary content.</summary>
    Keep,
    /// <summary>The app asked clipboard tools to leave it alone: don't keep it.</summary>
    Skip,
    /// <summary>A password manager marked it secret: don't read it, don't keep it.</summary>
    Private,
}

public static class ClipboardFormats
{
    /// <summary>
    /// Judges the clipboard by the names of its formats. Windows: "ExcludeClipboardContentFromMonitorProcessing"
    /// (password managers) and "Clipboard Viewer Ignore"; "CanIncludeInClipboardHistory" set to 0 is passed in as
    /// <paramref name="historyAllowed"/>. Linux: KeePassXC and KDE's "x-kde-passwordManagerHint".
    /// </summary>
    public static ClipPrivacy Judge(IEnumerable<string> formats, bool? historyAllowed = null)
    {
        bool skip = historyAllowed == false;
        foreach (string format in formats)
        {
            switch (format)
            {
                case "ExcludeClipboardContentFromMonitorProcessing":
                case "x-kde-passwordManagerHint":
                    return ClipPrivacy.Private;
                case "Clipboard Viewer Ignore":
                    skip = true;
                    break;
            }
        }
        return skip ? ClipPrivacy.Skip : ClipPrivacy.Keep;
    }
}
