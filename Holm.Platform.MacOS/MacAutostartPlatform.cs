using System;
using System.IO;
using System.Linq;
using System.Security;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// A LaunchAgent in ~/Library/LaunchAgents, which macOS starts at sign-in (and lists under Login Items in System
/// Settings). It names this very executable, so a moved app is no longer counted as on.
/// </summary>
public sealed class MacAutostartPlatform : IAutostartPlatform
{
    internal const string Label = "app.holm.notch";
    readonly string _file;

    public MacAutostartPlatform()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist")) { }
    internal MacAutostartPlatform(string file) => _file = file;

    public bool CanChange => true;

    public bool IsEnabled
    {
        get
        {
            try { return File.Exists(_file) && File.ReadAllText(_file) == Plist(AppCommand.Current()); }
            catch { return false; }
        }
    }

    public void Set(bool enabled)
    {
        try
        {
            if (!enabled) { File.Delete(_file); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, Plist(AppCommand.Current()));
        }
        catch { }
    }

    internal static string Plist(string[] command) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{Label}</string>
            <key>ProgramArguments</key>
            <array>
        {string.Concat(command.Select(a => $"        <string>{SecurityElement.Escape(a)}</string>\n"))}    </array>
            <key>RunAtLoad</key>
            <true/>
            <key>ProcessType</key>
            <string>Interactive</string>
        </dict>
        </plist>

        """;
}
