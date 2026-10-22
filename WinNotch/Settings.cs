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
    /// <summary>Device name of the display the notch sits on; null means the main display.</summary>
    public string? Monitor { get; set; }
    /// <summary>What the AI button opens: "claude", "gemini", "chatgpt", "custom" (<see cref="AiPath"/>) or null for no button.</summary>
    public string? Ai { get; set; } = "claude";
    /// <summary>Program or shortcut picked with "Choose app…".</summary>
    public string? AiPath { get; set; }
    public bool TeamsMessages { get; set; } = true;
    /// <summary>Beam: drop files on the notch to send them; be visible to WinNotch users on this network; pick up copied codes.</summary>
    [JsonPropertyName("AirDrop")] // its name before 1.3, kept so the setting survives the update
    public bool Beam { get; set; } = true;
    /// <summary>Relay for sending over the internet, "host:port".</summary>
    public string? Relay { get; set; }
    /// <summary>Size of the notch; 1 is normal.</summary>
    public double Scale { get; set; } = 1;

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

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Diag.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WinNotch";

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
}

internal static class Diag
{
    public static bool Enabled;
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNotch");

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
