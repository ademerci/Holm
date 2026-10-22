using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch.Notify;

/// <summary>A chat or mail app whose notifications pop out of the notch.</summary>
/// <param name="Key">Stable name for the settings.</param>
/// <param name="AppIds">Parts of the app id Windows reports for its notifications (case-insensitive).</param>
/// <param name="Processes">Its process names, to tell when you're looking at it.</param>
/// <param name="WebHosts">Where it runs in a browser; the browser's notification names the site in its last line.</param>
/// <param name="Color">Brand colour (ARGB) for its badge and the fallback logo.</param>
/// <param name="Glyph">Segoe Fluent icon for the fallback logo.</param>
/// <param name="OpenUri">Opens the app when we don't know its app id yet.</param>
public sealed record Messenger(string Key, string Name, string[] AppIds, string[] Processes, string[] WebHosts,
                               uint Color, string Glyph, string? OpenUri);

/// <summary>One notification from a messenger, or (<see cref="CountOnly"/>) only the number from its taskbar badge.</summary>
public sealed class ChatMessage
{
    public uint Id;
    public string App = "";
    /// <summary>First line of the notification: who wrote, or the chat's name.</summary>
    public string Title = "";
    public string Text = "";
    public DateTimeOffset Time;
    /// <summary>The app only told us how many messages are unread, through the number on its taskbar icon.</summary>
    public bool CountOnly;
    public int Count = 1;
    /// <summary>The site, when the message came from the app running in a browser ("web.whatsapp.com").</summary>
    public string? Site;
}

public static class Messengers
{
    const string Chat = "", Mail = "";

    public static readonly IReadOnlyList<Messenger> All = new Messenger[]
    {
        // Teams isn't one of them on purpose (since 1.5.1): its notifications stay out of the notch.
        new("whatsapp", "WhatsApp", new[] { "whatsapp" }, new[] { "whatsapp", "whatsapp.root" },
            new[] { "web.whatsapp.com" }, 0xFF25D366, Chat, "whatsapp:"),
        new("discord", "Discord", new[] { "discord" }, new[] { "discord", "discordptb", "discordcanary" },
            new[] { "discord.com" }, 0xFF5865F2, Chat, "discord:"),
        // New Outlook (Microsoft.OutlookForWindows_…), classic Outlook (Microsoft.Office.OUTLOOK.EXE.15) and the old Mail app.
        new("outlook", "Outlook", new[] { "outlook", "windowslive.mail" }, new[] { "olk", "outlook", "hxoutlook" },
            new[] { "outlook.office.com", "outlook.office365.com", "outlook.live.com", "outlook.cloud.microsoft" }, 0xFF0F6CBD, Mail, null),
        new("telegram", "Telegram", new[] { "telegram" }, new[] { "telegram" },
            new[] { "web.telegram.org" }, 0xFF2AABEE, Chat, "tg:"),
        new("signal", "Signal", new[] { "whispersystems", "signal-desktop" }, new[] { "signal" },
            Array.Empty<string>(), 0xFF3A76F0, Chat, null),
        new("slack", "Slack", new[] { "slack" }, new[] { "slack" },
            new[] { "app.slack.com" }, 0xFFE01E5A, Chat, "slack:"),
    };

    static readonly Regex Whitespace = new(@"\s+");

    public static Messenger? Get(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>The messenger an app id belongs to; with no app id, the name Windows shows for the app.</summary>
    public static Messenger? ForApp(string? appId, string? displayName = null)
    {
        if (!string.IsNullOrEmpty(appId))
            return All.FirstOrDefault(m => m.AppIds.Any(part => appId.Contains(part, StringComparison.OrdinalIgnoreCase)));
        if (string.IsNullOrEmpty(displayName)) return null;
        return All.FirstOrDefault(m => displayName.Contains(m.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The messenger a process belongs to (name without ".exe").</summary>
    public static Messenger? ForProcess(string? process)
        => string.IsNullOrEmpty(process) ? null
         : All.FirstOrDefault(m => m.Processes.Any(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A browser's last line names the site ("web.whatsapp.com"); short, so a message that mentions a site doesn't count.</summary>
    static (Messenger App, string Host)? ForSite(string line)
    {
        if (line.Length > 60) return null;
        foreach (var app in All)
            foreach (var host in app.WebHosts)
                if (line.Contains(host, StringComparison.OrdinalIgnoreCase)) return (app, host);
        return null;
    }

    /// <summary>
    /// Makes a message out of a notification, or null if it isn't from a messenger. <paramref name="lines"/>
    /// are the notification's text lines in order (title first, a browser's site name last).
    /// </summary>
    public static ChatMessage? Parse(string? appId, string? displayName, IReadOnlyList<string?> lines, uint id, DateTimeOffset time)
    {
        var text = lines.Select(l => Whitespace.Replace(l ?? "", " ").Trim()).Where(l => l.Length > 0).ToList();
        var app = ForApp(appId, displayName);
        string? site = null;
        if (app == null && text.Count > 1 && ForSite(text[^1]) is { } web)
        {
            (app, site) = web;
            text.RemoveAt(text.Count - 1);
        }
        if (app == null) return null;
        return new ChatMessage
        {
            Id = id,
            App = app.Key,
            Title = text.Count > 0 ? text[0] : app.Name,
            Text = text.Count > 1 ? text[1] : "",
            Time = time,
            Site = site,
        };
    }
}
