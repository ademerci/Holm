namespace Holm.Platform;

/// <summary>
/// Files and the system's file manager: where downloads go, opening a file with its app, showing it in its folder
/// (Windows: Explorer; Linux: the desktop's file manager; macOS: Finder).
/// </summary>
public interface IShellPlatform
{
    /// <summary>The user's Downloads folder.</summary>
    string DownloadsFolder();

    /// <summary>Opens a file or folder with the app the system picks for it.</summary>
    void Open(string path);

    /// <summary>Shows a file selected in its folder.</summary>
    void Reveal(string path);

    /// <summary>The system drive's root ("C:\\", "/").</summary>
    string SystemDrive();

    /// <summary>Where to free space: the system's storage settings or disk usage tool.</summary>
    void OpenStorage();

    /// <summary>Where the system saves screenshots, or null when it has no such folder.</summary>
    string? ScreenshotsFolder();

    /// <summary>Whether a new picture in that folder is a screenshot (macOS marks its own; elsewhere the folder says it).</summary>
    bool IsScreenshot(string path);
}

/// <summary>For systems without a backend yet: ~/Downloads, and nothing opens.</summary>
public sealed class NoShellPlatform : IShellPlatform
{
    public string DownloadsFolder() => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
    public void Open(string path) { }
    public void Reveal(string path) { }
    public string SystemDrive() => "/";
    public void OpenStorage() { }
    public string? ScreenshotsFolder() => null;
    public bool IsScreenshot(string path) => false;
}
