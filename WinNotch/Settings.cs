using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using WinNotch.Sharing;

namespace WinNotch;

internal sealed class AppSettings
{
    public bool Animations { get; set; } = true;
    public bool MiniIdle { get; set; }
    /// <summary>Check GitHub for a newer stable release at startup and every six hours.</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    /// <summary>Device name of the display the notch sits on; null means the main display.</summary>
    public string? Monitor { get; set; }
    /// <summary>What the AI button opens: "claude", "gemini", "chatgpt", "custom" (<see cref="AiPath"/>) or null for no button.</summary>
    public string? Ai { get; set; } = "claude";
    /// <summary>Program or shortcut picked with "Choose app…".</summary>
    public string? AiPath { get; set; }
    /// <summary>Messages from chat and mail apps pop out of the notch.</summary>
    [JsonPropertyName("TeamsMessages")] // its name before 1.4, when only Teams was shown
    public bool Messages { get; set; } = true;
    /// <summary>Messengers switched off (<see cref="Notify.Messenger.Key"/>); new ones are on by default.</summary>
    public List<string> MutedApps { get; set; } = new();
    /// <summary>Downloads in the Downloads folder show in the notch.</summary>
    public bool Downloads { get; set; } = true;
    /// <summary>A lock in the notch while a VPN is connected, and a note when it connects or drops.</summary>
    public bool Vpn { get; set; } = true;
    /// <summary>Beam: drop files on the notch to send them; be visible to Holm users on this network; pick up copied codes.</summary>
    [JsonPropertyName("AirDrop")] // its name before 1.3, kept so the setting survives the update
    public bool Beam { get; set; } = true;
    /// <summary>Relay for sending over the internet, "host:port".</summary>
    public string? Relay { get; set; }
    double? _collapsedScale, _expandedScale;
    double _legacyScale = 1;

    /// <summary>Size of the notch while collapsed; 1 is normal.</summary>
    public double CollapsedScale
    {
        get => _collapsedScale ?? _legacyScale;
        set => _collapsedScale = value;
    }

    /// <summary>Size of the expanded notch; 1 is normal.</summary>
    public double ExpandedScale
    {
        get => _expandedScale ?? _legacyScale;
        set => _expandedScale = value;
    }

    // Read the old shared size as a fallback for either missing setting, regardless of JSON order.
    // Returning null keeps it out of new saves, which always contain the two independent sizes.
    [JsonPropertyName("Scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyScale
    {
        get => null;
        set => _legacyScale = value ?? 1;
    }

    // Friends (see Sharing/Friends.cs)
    public List<Friend> Friends { get; set; } = new();
    public string? FriendsTopic { get; set; }
    /// <summary>Our private key, encrypted for this Windows account (DPAPI).</summary>
    public string? FriendsKey { get; set; }
    /// <summary>ntfy server for our inbox; null means ntfy.sh. Set it before copying your friend code.</summary>
    public string? FriendsServer { get; set; }
    /// <summary>Server time of the last note in our inbox, to catch up from after a restart.</summary>
    public long? FriendsSince { get; set; }
    /// <summary>Ids of the last notes handled, so catching up never handles one twice.</summary>
    public List<string> FriendsSeen { get; set; } = new();

    static string FilePath => Path.Combine(Diag.Folder, "settings.json");

    /// <summary>Set while posing for screenshots, which change settings that must never reach your settings file.</summary>
    internal static bool ReadOnly;

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Diag.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

/// <summary>WinNotch was renamed Holm in 1.5.1: its settings and autostart entry carry over on the first start.</summary>
internal static class FormerName
{
    public const string Name = "WinNotch";

    public static void Migrate()
    {
        try
        {
            // Copied, not moved, so the old version still finds its settings should someone go back.
            string before = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name, "settings.json");
            string now = Path.Combine(Diag.Folder, "settings.json");
            if (!File.Exists(now) && File.Exists(before))
            {
                Directory.CreateDirectory(Diag.Folder);
                File.Copy(before, now);
            }
        }
        catch (Exception ex) { Diag.Error(ex); }
        try { Autostart.TakeOver(Name); }
        catch (Exception ex) { Diag.Error(ex); }
    }
}

internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Holm";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string cmd
                && cmd.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, false);
    }

    /// <summary>Replaces the entry of the app's former name with ours: on if it was on, so it doesn't start twice.</summary>
    public static void TakeOver(string formerName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(formerName) == null) return;
        key.DeleteValue(formerName, false);
        if (key.GetValue(ValueName) == null) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
    }
}

internal static class Diag
{
    public static bool Enabled;
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Holm");

    public static void Log(string message)
    {
        if (Enabled) Write("diag.log", message);
    }

    public static void Error(Exception ex) => Write("error.log", ex.ToString());

    static void Write(string file, string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, file), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
