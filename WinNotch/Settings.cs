using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinNotch;

internal sealed class AppSettings
{
    public bool Animations { get; set; } = true;
    public bool MiniIdle { get; set; }
    /// <summary>Device name of the display the notch sits on; null means the main display.</summary>
    public string? Monitor { get; set; }

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
