using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch;

/// <summary>Tells a call from other recording: a call app (or a browser, for Meet and the like) using the microphone.</summary>
internal static class Calls
{
    static readonly HashSet<string> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord", "DiscordPTB", "DiscordCanary", "Teams", "ms-teams", "Zoom", "slack", "Telegram", "Signal", "Skype",
        "WhatsApp", "WhatsApp.Root", "CiscoCollabHost", "webexmta", "Webex",
    };

    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "librewolf", "vivaldi",
    };

    /// <summary>Parts of Store app package names.</summary>
    static readonly string[] Packages = { "WhatsAppDesktop", "MSTeams", "MicrosoftTeams", "SkypeApp", "TelegramDesktop", "Zoom" };

    public static bool IsCallApp(string key)
        => Apps.Contains(key) || Browsers.Contains(key) || Packages.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsBrowser(string key) => Browsers.Contains(key);

    /// <summary>The call going on: a call app before a browser, the longest running first. Null if there is none.</summary>
    public static SensorUse? Current(IEnumerable<SensorUse> uses)
        => uses.Where(u => u.Sensor == Sensor.Microphone && IsCallApp(u.Key))
               .OrderBy(u => IsBrowser(u.Key) ? 1 : 0)
               .ThenBy(u => u.Since)
               .FirstOrDefault();

    /// <summary>"Discord call", "Call in Chrome".</summary>
    public static string Title(SensorUse call) => IsBrowser(call.Key) ? $"Call in {call.Name}" : $"{call.Name} call";
}
