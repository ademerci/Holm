using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch;

/// <summary>One audio session as Windows reports it (an app may have several: a browser has one per tab process).</summary>
internal sealed record AudioSessionInfo(int ProcessId, string ExePath, string Name, int State, float Volume, bool Muted, bool IsSystemSounds)
{
    public const int Inactive = 0, Active = 1, Expired = 2;
}

/// <summary>An app in the mixer: all its sessions under one slider.</summary>
internal sealed record AppVolume(string Key, string Name, string ExePath, float Volume, bool Muted, bool Playing, IReadOnlyList<int> Sessions);

/// <summary>Turns Windows' audio sessions into the mixer's rows.</summary>
internal static class AppVolumeList
{
    public const int MaxRows = 6;

    /// <summary>
    /// One row per app (by its program file), apps playing right now first, then by name; the system sounds and
    /// sessions that are gone are left out. <paramref name="sessions"/> carry their index in the session list.
    /// </summary>
    public static List<AppVolume> Build(IReadOnlyList<AudioSessionInfo> sessions, int ownProcessId = 0)
    {
        return sessions
            .Select((session, index) => (session, index))
            .Where(s => !s.session.IsSystemSounds && s.session.State != AudioSessionInfo.Expired && s.session.ProcessId != 0 && s.session.ProcessId != ownProcessId)
            .GroupBy(s => KeyOf(s.session), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var all = group.ToList();
                var first = all[0].session;
                string name = all.Select(s => s.session.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim()
                              ?? ProgramName(first.ExePath);
                return new AppVolume(group.Key, name, first.ExePath,
                                     all.Max(s => s.session.Volume),
                                     all.All(s => s.session.Muted),
                                     all.Any(s => s.session.State == AudioSessionInfo.Active),
                                     all.Select(s => s.index).ToList());
            })
            .OrderByDescending(app => app.Playing)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxRows)
            .ToList();
    }

    /// <summary>"chrome" for C:\…\chrome.exe.</summary>
    static string ProgramName(string exePath)
    {
        string file = exePath[(exePath.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    /// <summary>What makes sessions one app: the program file (in lower case), or the process when that is unknown.</summary>
    public static string KeyOf(AudioSessionInfo session)
        => session.ExePath.Length > 0 ? session.ExePath.ToLowerInvariant() : "pid:" + session.ProcessId;

    /// <summary>"62 %", or "Muted".</summary>
    public static string Describe(float volume, bool muted) => muted ? "Muted" : $"{(int)Math.Round(volume * 100)} %";
}
