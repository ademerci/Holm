using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinNotch;

/// <summary>
/// Reading Windows' record of who uses the microphone and the camera (the CapabilityAccessManager consent store)
/// and naming the apps in it. No WPF: PrivacyMonitor watches the record with it, and so does the Avalonia app's
/// Windows backend.
/// </summary>
internal static class SensorRecord
{
    /// <summary>A Store app's entry left behind by a crash would otherwise show forever; nobody records for half a day.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    internal static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["brave"] = "Brave", ["opera"] = "Opera",
        ["librewolf"] = "LibreWolf", ["Discord"] = "Discord", ["Teams"] = "Teams", ["ms-teams"] = "Teams", ["Zoom"] = "Zoom",
        ["slack"] = "Slack", ["Telegram"] = "Telegram", ["Signal"] = "Signal", ["Skype"] = "Skype", ["WhatsApp"] = "WhatsApp",
        ["obs64"] = "OBS Studio", ["obs32"] = "OBS Studio", ["Resolve"] = "DaVinci Resolve", ["CiscoCollabHost"] = "Webex",
        ["webexmta"] = "Webex", ["WhatsAppDesktop"] = "WhatsApp", ["WindowsCamera"] = "Camera",
        ["WindowsSoundRecorder"] = "Sound Recorder", ["MSTeams"] = "Teams", ["MicrosoftTeams"] = "Teams",
    };

    internal static IReadOnlyList<SensorUse> StableLive(IEnumerable<SensorUse> current, IReadOnlyList<SensorUse> previous)
        => current.Select(use => use with { Since = previous.FirstOrDefault(old => old.Sensor == use.Sensor
            && old.Key.Equals(use.Key, StringComparison.OrdinalIgnoreCase))?.Since ?? use.Since }).ToArray();

    internal static IEnumerable<SensorEntry> Read(RegistryKey sensor)
    {
        var entries = new List<SensorEntry>();
        foreach (string name in sensor.GetSubKeyNames())
        {
            using var key = sensor.OpenSubKey(name);
            if (key == null) continue;
            if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string exe in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(exe);
                    if (app != null) entries.Add(new SensorEntry(exe, true, Time(app, "LastUsedTimeStart"), Time(app, "LastUsedTimeStop")));
                }
            }
            else entries.Add(new SensorEntry(name, false, Time(key, "LastUsedTimeStart"), Time(key, "LastUsedTimeStop")));
        }
        return entries;
    }

    static long Time(RegistryKey key, string value) => key.GetValue(value) switch
    {
        long l => l,
        int i => i,
        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
        _ => 0,
    };

    /// <summary>
    /// The entries in use now: started and not stopped. An app that crashed while recording never writes its stop
    /// time, so the same desktop executable must still be running from before the recording started.
    /// A Store app must have started recording in the last 12 hours.
    /// </summary>
    internal static IEnumerable<SensorUse> Active(Sensor sensor, IEnumerable<SensorEntry> entries, Func<string, DateTime, bool> isRunning,
                                                  DateTime nowUtc, Func<string, string>? exeName = null)
    {
        foreach (var entry in entries)
        {
            if (entry.Start <= 0 || entry.Stop != 0) continue;
            DateTime since;
            try { since = DateTime.FromFileTimeUtc(entry.Start); }
            catch (ArgumentOutOfRangeException) { continue; }

            if (entry.NonPackaged)
            {
                string path = entry.Name.Replace('#', '\\');
                string process = Path.GetFileNameWithoutExtension(path);
                if (process.Length == 0 || !isRunning(path, since)) continue;
                yield return new SensorUse(sensor, exeName?.Invoke(path) ?? NameOfProcess(process), process, since);
            }
            else
            {
                if (nowUtc - since > StaleAfter) continue;
                yield return new SensorUse(sensor, NameOfPackage(entry.Name), entry.Name, since);
            }
        }
    }

    internal static bool MatchesRecording(string recordedPath, DateTime since, string processPath, DateTime processStartedUtc)
        => string.Equals(recordedPath, processPath, StringComparison.OrdinalIgnoreCase) && processStartedUtc <= since;

    internal static bool IsRunning(string path, DateTime since)
    {
        var found = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        try
        {
            foreach (var process in found)
            {
                try
                {
                    if (MatchesRecording(path, since, Native.ProcessPath(process.Id), process.StartTime.ToUniversalTime())) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            return false;
        }
        finally { foreach (var process in found) process.Dispose(); }
    }

    /// <summary>Known apps by their usual name, others by the description in the exe ("Google Chrome"), or its file name.</summary>
    internal static string ExeName(string path, Dictionary<string, string> cache)
    {
        string process = Path.GetFileNameWithoutExtension(path);
        if (KnownNames.TryGetValue(process, out var known)) return known;
        if (cache.TryGetValue(path, out var cached)) return cached;
        string name = process;
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description) && description.Length <= 30) name = description;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException) { }
        cache[path] = name;
        return name;
    }

    internal static string NameOfProcess(string process) => KnownNames.TryGetValue(process, out var known) ? known : process;

    /// <summary>"5319275A.WhatsAppDesktop_cv1g1gvanyjgm" → "WhatsApp"; "Contoso.PhotoBooth_xyz" → "Photo Booth".</summary>
    internal static string NameOfPackage(string package)
    {
        string name = package.Split('_')[0];
        name = name[(name.LastIndexOf('.') + 1)..];
        if (KnownNames.TryGetValue(name, out var known)) return known;
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return name.Length > 0 ? name : package;
    }
}
