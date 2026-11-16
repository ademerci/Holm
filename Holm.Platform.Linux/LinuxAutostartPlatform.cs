using System;
using System.IO;
using System.Linq;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// XDG autostart: a desktop entry in ~/.config/autostart, which every desktop (GNOME, KDE, Xfce, ...) starts at
/// sign-in. It names this very executable, so a moved app is no longer counted as on.
/// </summary>
public sealed class LinuxAutostartPlatform : IAutostartPlatform
{
    readonly string _file;

    public LinuxAutostartPlatform() : this(DefaultFile()) { }
    internal LinuxAutostartPlatform(string file) => _file = file;

    static string DefaultFile()
    {
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c
            ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "autostart", "holm.desktop");
    }

    public bool CanChange => true;

    public bool IsEnabled
    {
        get
        {
            try { return File.Exists(_file) && File.ReadAllText(_file).Contains("Exec=" + Exec(AppCommand.Current()) + "\n"); }
            catch { return false; }
        }
    }

    public void Set(bool enabled)
    {
        try
        {
            if (!enabled) { File.Delete(_file); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, Entry(AppCommand.Current()));
        }
        catch { }
    }

    internal static string Entry(string[] command) => string.Join("\n",
        "[Desktop Entry]",
        "Type=Application",
        "Name=Holm",
        "Comment=The notch at the top of the screen",
        "Exec=" + Exec(command),
        "Terminal=false",
        "NoDisplay=true",
        "X-GNOME-Autostart-enabled=true",
        "") ;

    /// <summary>The Exec line: each argument quoted, with what the desktop entry specification asks to escape.</summary>
    internal static string Exec(string[] command)
        => string.Join(" ", command.Select(a => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\""));
}
